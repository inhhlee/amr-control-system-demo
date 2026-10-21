using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Simulator.Map;
using Amr.Simulator.Telemetry;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class ProjectionTests
{
    private static TelemetryMessage Point(long ts = 100, double x = 1, double y = 2, double speed = 0, string state = "IDLE", string id = "AMR_001") =>
        new(id, ts, new(x, y), speed, state, DateTimeOffset.UtcNow);

    [Fact]
    public void NoPositionUntilTelemetry_HeartbeatDoesNotMoveRobot()
    {
        var view = new MqttMapProjection("AMR_001");
        Assert.Null(view.Read().Telemetry);
        Assert.True(view.Apply(new HeartbeatMessage("AMR_001", 100, "OK", DateTimeOffset.UtcNow)));
        Assert.Null(view.Read().Telemetry);
        Assert.Equal(1, view.Read().HeartbeatCount);
    }

    [Fact]
    public void ReceivedValuesAndTimestamp_ArePreservedExactly()
    {
        var view = new MqttMapProjection("AMR_001");
        var message = Point(x: -0.8918197999999999, y: 0, speed: 1, state: "BUSY");
        view.Apply(message);
        Assert.Equal(message, view.Read().Telemetry);
        Assert.Equal(1, view.Read().TelemetryCount);
    }

    [Fact]
    public void OtherAmr_CannotOverwriteCurrentPosition()
    {
        var view = new MqttMapProjection("AMR_001");
        var current = Point(200);
        view.Apply(current);
        Assert.False(view.Apply(Point(300, x: 99, id: "AMR_002")));
        Assert.Equal(current, view.Read().Telemetry);
        Assert.Equal(1, view.Read().IgnoredCount);
    }

    [Fact]
    public void ClockCorrection_DoesNotDiscardLaterReceivedPosition()
    {
        var view = new MqttMapProjection("AMR_001");
        view.Apply(Point(200, x: -1, y: 0));
        var laterReceived = Point(199);
        Assert.True(view.Apply(laterReceived));
        Assert.Equal(laterReceived, view.Read().Telemetry);
    }

    [Fact]
    public void EqualMillisecondTimestamp_IsAllowedForDistinctUpdates()
    {
        var view = new MqttMapProjection("AMR_001");
        view.Apply(Point(x: -1, y: 0));
        Assert.True(view.Apply(Point()));
        Assert.True(view.MatchesFinal(100, 1, 2));
    }

    [Theory]
    [InlineData(99, 1, 2, 0, "IDLE")]
    [InlineData(100, 0, 2, 0, "IDLE")]
    [InlineData(100, 1, 2, 1, "BUSY")]
    public void Completion_RequiresMatchingPublicationTimestampPositionAndStop(long ts, double x, double y, double speed, string state)
    {
        var view = new MqttMapProjection("AMR_001");
        view.Apply(Point(ts, x, y, speed, state));
        Assert.False(view.MatchesFinal(100, 1, 2));
    }

    [Fact]
    public async Task MqttSessionReady_DoesNotUseConfiguredInitialPositionAsReceivedData()
    {
        string root = Path.Combine(Path.GetTempPath(), "amr-map-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "input.json");
        await File.WriteAllTextAsync(config, """{"amrId":"AMR_001","initialPosition":{"x":-1,"y":0},"speedMps":1,"updateIntervalMs":100,"waypoints":[{"x":1,"y":0},{"x":1,"y":2}]}""");
        try
        {
            await using var session = await MapSession.CreateAsync(config, root, new SimulatorMqttOptions(new MqttConnectionSettings(), 200, 1000));
            using var state = JsonDocument.Parse(JsonSerializer.Serialize(session.Read()));
            Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("position").ValueKind);
            Assert.Equal("mqtt-control-receiver", state.RootElement.GetProperty("source").GetString());
            Assert.Equal(0, state.RootElement.GetProperty("telemetryCount").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }
}
