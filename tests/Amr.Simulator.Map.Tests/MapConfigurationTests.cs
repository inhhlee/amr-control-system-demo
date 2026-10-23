using System.Text.Json;
using System.Text.Json.Nodes;
using Amr.Communication.Models;
using Amr.Simulator.Map.Navigation;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class MapConfigurationTests
{
    private const string Configuration = """
        {"amrId":"CUSTOM_AMR","map":[[0,0,0,0,0,0,0,0],[0,1,1,1,1,1,1,0],[0,0,0,0,0,0,0,0]],
         "start":{"row":0,"col":0},"waypoints":[{"row":0,"col":7},{"row":2,"col":7}],
         "stepIntervalMs":250,"webPort":59001,
         "mqtt":{"enabled":true,"host":"127.0.0.1","port":1889,"telemetryMs":150,"heartbeatMs":900}}
        """;

    [Fact]
    public async Task CustomMapTimingAndMqttAreLoadedTogether()
    {
        await WithFile(Configuration, async (path, _) =>
        {
            var settings = await GridMissionSettings.LoadAsync(path);
            Assert.Equal(250, settings.StepIntervalMs);
            Assert.Equal(59001, settings.WebPort);
            Assert.Equal(1889, settings.Mqtt!.Connection.Port);
            Assert.Equal(150, settings.Mqtt.TelemetryMs);
            Assert.Equal(900, settings.Mqtt.HeartbeatMs);
            var map = settings.Map;
            Assert.Equal(3, map.GetLength(0));
            Assert.Equal(8, map.GetLength(1));
            var navigator = new GridNavigator(settings.Start, settings.Waypoints, map);
            map[0, 0] = 1;
            Assert.Equal(0, settings.Map[0, 0]);
            navigator.Start();
            while (navigator.Step()) { }
            Assert.Equal("Completed", navigator.Read().Phase);
            Assert.Equal(9, navigator.Read().Moves);
            navigator.Reset();
            Assert.Equal(8, navigator.Read().Map[0].Length);
            Assert.True(navigator.Read().FixedObstacles[1][1]);
            Assert.Throws<ArgumentException>(() => navigator.SetObstacle(new(1, 1), false));
        });
    }

    [Theory]
    [InlineData("map", "[]")]
    [InlineData("map", "[[]]")]
    [InlineData("map", "[[0,0],[0]]")]
    [InlineData("map", "[[0,2],[0,0]]")]
    [InlineData("map", "[[0,0.5],[0,0]]")]
    [InlineData("map", "[[\"0\",0],[0,0]]")]
    [InlineData("map", "[0,0]")]
    [InlineData("map", "null")]
    [InlineData("stepIntervalMs", "0")]
    [InlineData("stepIntervalMs", "-1")]
    [InlineData("stepIntervalMs", "1.5")]
    [InlineData("webPort", "65536")]
    [InlineData("mqtt.enabled", "\"true\"")]
    [InlineData("mqtt.port", "0")]
    [InlineData("mqtt.port", "65536")]
    [InlineData("mqtt.host", "\"\"")]
    [InlineData("mqtt.telemetryMs", "0")]
    [InlineData("mqtt.heartbeatMs", "-1")]
    [InlineData("mqtt.extra", "1")]
    public async Task InvalidEditableSettingsAreRejected(string field, string value)
    {
        var root = JsonNode.Parse(Configuration)!;
        var parts = field.Split('.');
        if (parts.Length == 1) root[field] = JsonNode.Parse(value);
        else root[parts[0]]![parts[1]] = JsonNode.Parse(value);
        await WithFile(root.ToJsonString(), (path, _) => Assert.ThrowsAsync<ArgumentException>(() => GridMissionSettings.LoadAsync(path)));
    }

    [Fact]
    public async Task DisabledMqttAndExactInputSnapshotSurviveFileEdits()
    {
        string original = Configuration.Replace("\"enabled\":true", "\"enabled\":false");
        await WithFile(original, async (path, directory) =>
        {
            var settings = await GridMissionSettings.LoadAsync(path);
            Assert.Null(settings.Mqtt);
            await File.WriteAllTextAsync(path, "{}");
            await using var session = await GridNavigationSession.CreateAsync(path, directory, null, settings);
            Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(session.EvidenceDirectory, "config.json")));
            var state = JsonSerializer.SerializeToElement(session.Read(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(250, state.GetProperty("stepIntervalMs").GetInt32());
            Assert.Equal("csharp-grid-navigation", state.GetProperty("source").GetString());
            Assert.Equal(8, state.GetProperty("navigation").GetProperty("map")[0].GetArrayLength());
            Assert.Equal(path, state.GetProperty("configurationPath").GetString());
            Assert.True(File.Exists(Path.Combine(session.EvidenceDirectory, "effective-settings.json")));
        });
    }

    [Fact]
    public void ReceivedCoordinatesUseConfiguredMapDimensionsAndSpeedUsesConfiguredInterval()
    {
        var telemetry = new TelemetryMessage("CUSTOM_AMR", 1, new(7, -2), 0, "IDLE", DateTimeOffset.UtcNow);
        Assert.Equal(new GridCell(2, 7), GridTelemetry.ReceivedCell(telemetry, 3, 8));
        Assert.Null(GridTelemetry.ReceivedCell(telemetry, 3, 7));
        var navigator = new GridNavigator(new(0, 0), [new(0, 7)], new int[3, 8]);
        navigator.Start();
        Assert.Equal(4, GridTelemetry.Capture("CUSTOM_AMR", navigator.Read(), stepIntervalMs: 250).Speed);
    }

    private static async Task WithFile(string json, Func<string, string, Task> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "map-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, json);
        try { await action(path, directory); }
        finally { Directory.Delete(directory, true); }
    }
}
