using System.Buffers;
using System.Text.Json;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using Xunit;
using static Amr.Simulator.Tests.ConfigurationTests;

namespace Amr.Simulator.Tests;

public sealed class MqttSimulationTests
{
    private const string Route = """{"amrId":"AMR_001","initialPosition":{"x":-1,"y":0},"speedMps":1,"updateIntervalMs":100,"waypoints":[{"x":1,"y":0},{"x":1,"y":2}]}""";
    private static SimulatorMqttOptions Options => new(new MqttConnectionSettings(), 20, 30);

    [Fact]
    public void GeneratedMessages_PassExistingReceiverContractAndPreserveActualSnapshot()
    {
        var snapshot = new SimulationSnapshot("AMR_001", -1.25, 2.5, 0.75, "BUSY");
        const long ts = 1792590000123;
        var telemetry = SimulationMessages.Create(snapshot, false, ts);
        var result = new TelemetryValidator().Validate(telemetry.Topic, new ReadOnlySequence<byte>(telemetry.Payload), DateTimeOffset.UtcNow);
        Assert.True(result.IsValid);
        Assert.Equal(snapshot.X, result.Message!.Position.X);
        Assert.Equal(snapshot.Y, result.Message.Position.Y);
        Assert.Equal(snapshot.Speed, result.Message.Speed);
        Assert.Equal(snapshot.State, result.Message.State);
        Assert.Equal(ts, result.Message.Timestamp);
        var heartbeat = SimulationMessages.Create(snapshot, true, ts);
        var alive = new HeartbeatValidator().Validate(heartbeat.Topic, new ReadOnlySequence<byte>(heartbeat.Payload), DateTimeOffset.UtcNow);
        Assert.True(alive.IsValid);
        Assert.Equal("OK", alive.Message!.Status);
        Assert.Equal(ts, alive.Message.Timestamp);
    }

    [Theory]
    [InlineData(70000, false)]
    [InlineData(70000, true)]
    public void OversizedTopic_IsRejectedBeforeSending(int count, bool heartbeat) =>
        Assert.Throws<ArgumentException>(() => SimulationMessages.Create(new(new string('a', count), 0, 0, 0, "IDLE"), heartbeat, 0));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void InvalidIntervals_DoNotEnableMqtt(string value) => Assert.Throws<ArgumentException>(() =>
        SimulatorMqttOptions.Parse(["--config", "a.json", "--mqtt", "--host", "127.0.0.1", "--port", "1883", "--telemetry-ms", value, "--heartbeat-ms", "1000"]));

    [Fact]
    public void SeparateExplicitPeriods_AreRequired()
    {
        Assert.Throws<ArgumentException>(() => SimulatorMqttOptions.Parse(["--config", "a.json", "--mqtt"]));
        Assert.Throws<ArgumentException>(() => SimulatorMqttOptions.Parse(["--config", "a.json", "--port", "1883"]));
        var parsed = SimulatorMqttOptions.Parse(["--mqtt", "--config", "a.json", "--host", "127.0.0.1", "--port", "1883", "--telemetry-ms", "200", "--heartbeat-ms", "1000"]);
        Assert.Equal(200, parsed.Mqtt!.TelemetryMs);
        Assert.Equal(1000, parsed.Mqtt.HeartbeatMs);
        Assert.Null(SimulatorMqttOptions.Parse(["--config", "a.json"]).Mqtt);
    }

    [Fact]
    public async Task Completion_PublishesInitialAndFinalIdleEvenBeforeFirstPeriodicTick()
    {
        var transport = new FakeTransport();
        var result = await Run(transport, clock: new TestSimulationClock(10));
        Assert.Equal(0, result.Code);
        var points = transport.Messages.Where(m => m.Kind == "Telemetry").Select(Read).ToArray();
        Assert.Equal(-1, points[0].X);
        Assert.Equal(0, points[0].Speed);
        Assert.Equal((1d, 2d, 0d, "IDLE"), points[^1]);
        Assert.True(transport.Disconnected && transport.Disposed);
        Assert.Contains("MqttSimulationCompleted", result.Output);
    }

    [Fact]
    public async Task CancellationAfterFinalPublication_DoesNotRelabelCompletedRun()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { OnDisconnect = cancellation.Cancel };
        var result = await Run(transport, cancellation.Token, new TestSimulationClock(10));
        Assert.Equal(0, result.Code);
        Assert.Contains("MqttSimulationCompleted", result.Output);
        Assert.DoesNotContain("MqttSimulationCanceled", result.Output);
    }

    [Fact]
    public async Task DisconnectFailure_RemainsErrorAfterArrival()
    {
        var transport = new FakeTransport { OnDisconnect = () => throw new IOException() };
        var result = await Run(transport, clock: new TestSimulationClock(10));
        Assert.Equal(1, result.Code);
        Assert.Contains("MqttDisconnectFailed", result.Error);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task HeartbeatAndTelemetry_ContinueWhileMovementWaitsForVeryLongUpdateInterval()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { AfterPublish = messages => { if (messages.Count(m => m.Kind == "Heartbeat") >= 3) cancellation.Cancel(); } };
        var result = await Run(transport, cancellation.Token, route: Route.Replace("\"updateIntervalMs\":100", "\"updateIntervalMs\":9223372036854775807"));
        Assert.Equal(0, result.Code);
        Assert.True(transport.Messages.Count(m => m.Kind == "Telemetry") >= 2);
        Assert.All(transport.Messages.Where(m => m.Kind == "Telemetry"), m => Assert.Equal(-1, Read(m).X));
        Assert.Contains("Reason=UserCanceled", result.Output);
        Assert.DoesNotContain("SimulationCompleted", result.Output);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task PublishFailure_CancelsMovementAndDisposesTransport()
    {
        var transport = new FakeTransport { FailAt = 3 };
        var result = await Run(transport);
        Assert.Equal(1, result.Code);
        Assert.Contains("MqttSimulationFailed", result.Error);
        Assert.Contains("SimulationCanceled", result.Output);
        Assert.DoesNotContain("SimulationCompleted", result.Output);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task ConnectionLoss_IsNotUserCancellation()
    {
        var transport = new FakeTransport { LoseAt = 3 };
        var result = await Run(transport);
        Assert.Equal(1, result.Code);
        Assert.Contains("Reason=ConnectionLost", result.Error);
        Assert.DoesNotContain("Reason=UserCanceled", result.Output);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task ConnectionFailure_DoesNotStartMovement()
    {
        var transport = new FakeTransport { FailConnect = true };
        var result = await Run(transport);
        Assert.Equal(1, result.Code);
        Assert.DoesNotContain("SimulationStarted", result.Output);
        Assert.Empty(transport.Messages);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task SlowPublication_DoesNotBlockMovement_AndCancellationJoinsPublication()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport { BlockAt = 3 };
        using var output = new ActionWriter(line => { if (line?.StartsWith("AmrMoved") == true) cancellation.Cancel(); });
        var result = await Run(transport, cancellation.Token, output: output);
        Assert.Equal(0, result.Code);
        Assert.Contains("AmrMoved", result.Output);
        Assert.True(transport.BlockedPublicationEnded);
        Assert.True(transport.Disposed);
    }

    private static (double X, double Y, double Speed, string State) Read(SimulationPublication message)
    {
        using var doc = JsonDocument.Parse(message.Payload);
        var value = doc.RootElement;
        return (value.GetProperty("pos").GetProperty("x").GetDouble(), value.GetProperty("pos").GetProperty("y").GetDouble(), value.GetProperty("sp").GetDouble(), value.GetProperty("state").GetString()!);
    }

    private static async Task<(int Code, string Output, string Error)> Run(FakeTransport transport,
        CancellationToken token = default, ISimulationClock? clock = null, string route = Route, StringWriter? output = null)
    {
        using var config = new TemporaryConfig(route);
        var amr = new SimulatedAmr(await SimulatorSettingsLoader.LoadAsync(config.FilePath));
        using var ownedOutput = output ?? new StringWriter();
        using var error = new StringWriter();
        int code = await MqttSimulationSession.RunAsync(amr, Options, transport, ownedOutput, error, token, clock).WaitAsync(TimeSpan.FromSeconds(5));
        return (code, ownedOutput.ToString(), error.ToString());
    }

    private sealed class ActionWriter(Action<string?> action) : StringWriter
    {
        public override void WriteLine(string? value) { base.WriteLine(value); action(value); }
        public override Task WriteLineAsync(string? value) { WriteLine(value); return Task.CompletedTask; }
    }

    private sealed class FakeTransport : ISimulationTransport
    {
        public event Action? ConnectionLost;
        public List<SimulationPublication> Messages { get; } = [];
        public Action<List<SimulationPublication>>? AfterPublish { get; init; }
        public Action? OnDisconnect { get; init; }
        public int FailAt { get; init; }
        public int LoseAt { get; init; }
        public int BlockAt { get; init; }
        public bool FailConnect { get; init; }
        public bool Disconnected { get; private set; }
        public bool Disposed { get; private set; }
        public bool BlockedPublicationEnded { get; private set; }
        private int _calls;
        public Task ConnectAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return FailConnect ? Task.FromException(new IOException()) : Task.CompletedTask;
        }
        public async Task PublishAsync(SimulationPublication message, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int call = ++_calls;
            if (call == FailAt) throw new IOException();
            if (call == LoseAt) { ConnectionLost?.Invoke(); token.ThrowIfCancellationRequested(); }
            if (call == BlockAt)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { BlockedPublicationEnded = true; }
            }
            Messages.Add(message);
            AfterPublish?.Invoke(Messages);
        }
        public Task DisconnectAsync() { Disconnected = true; OnDisconnect?.Invoke(); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}
