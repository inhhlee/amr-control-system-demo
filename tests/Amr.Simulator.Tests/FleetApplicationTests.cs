using Amr.Communication.Mqtt;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class FleetApplicationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FleetRunners_PreserveEachIdRouteAndFinalPosition(int count)
    {
        var fleet = FleetSettingsLoader.Parse(FleetSettingsTests.FleetJson(count));
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(0, await FleetApplication.RunAsync(fleet, null, output, error,
            clockFactory: _ => new TestSimulationClock(0.25)));
        for (int i = 0; i < count; i++)
            Assert.Contains($"SimulationCompleted AmrId=AMR_{i + 1:D3} X={i * 2 + 1} Y={-i} State=IDLE CurrentSpeedMps=0", output.ToString());
        Assert.Contains($"FleetConfigurationLoaded AmrCount={count}", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task BadLastAmr_IsRejectedBeforeAnyInitializationOrMqtt()
    {
        using var config = new TemporaryConfig(FleetSettingsTests.FleetJson(change: r => r["amrs"]![1]!["speedMps"] = 0));
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(1, await SimulatorApplication.RunAsync(["--fleet-config", config.FilePath,
            "--mqtt", "--host", "unreachable.invalid", "--port", "1884", "--telemetry-ms", "100", "--heartbeat-ms", "1000"], output, error));
        Assert.Empty(output.ToString());
        Assert.Contains("Field=$.amrs[1].speedMps", error.ToString());
    }

    [Fact]
    public async Task MixingSingleAndFleetOptions_IsRejected()
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(1, await SimulatorApplication.RunAsync(["--fleet-config", "a", "--config", "b"], output, error));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task Cancellation_WaitsForAllIndependentRunnersToFinish()
    {
        var fleet = FleetSettingsLoader.Parse(FleetSettingsTests.FleetJson());
        using var stop = new CancellationTokenSource();
        using var output = new StringWriter(); using var error = new StringWriter();
        var clocks = new[] { new WaitingClock(), new WaitingClock() };
        int index = 0;
        var running = FleetApplication.RunAsync(fleet, null, output, error, stop.Token,
            clockFactory: _ => clocks[index++]);
        await Task.WhenAll(clocks.Select(c => c.Started.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(clocks, c => Assert.True(c.Ended));
    }

    [Fact]
    public async Task MqttFleet_HasSeparateTransportsAndTargetedPublications()
    {
        var fleet = FleetSettingsLoader.Parse(FleetSettingsTests.FleetJson());
        var transports = new List<Transport>();
        using var output = new StringWriter(); using var error = new StringWriter();
        var options = new SimulatorMqttOptions(new MqttConnectionSettings(), 100, 1000);
        Assert.Equal(0, await FleetApplication.RunAsync(fleet, options, output, error,
            clockFactory: _ => new TestSimulationClock(1),
            transportFactory: _ => { var t = new Transport(); transports.Add(t); return t; }));
        Assert.Equal(2, transports.Count);
        for (int i = 0; i < transports.Count; i++)
        {
            Assert.True(transports[i].Disposed);
            Assert.All(transports[i].Messages, m => Assert.StartsWith($"amr/AMR_{i + 1:D3}/", m.Topic));
        }
    }

    [Fact]
    public async Task OneTransportFailure_CancelsAndJoinsSiblingBeforeReturningFailure()
    {
        var fleet = FleetSettingsLoader.Parse(FleetSettingsTests.FleetJson());
        using var output = new StringWriter(); using var error = new StringWriter();
        var first = new Transport();
        var second = new Transport { FailConnection = true };
        var clock = new WaitingClock();
        int index = 0;
        int code = await FleetApplication.RunAsync(fleet, new(new(), 100, 1000), output, error,
            clockFactory: _ => clock,
            transportFactory: _ => index++ == 0 ? first : second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, code);
        Assert.True(clock.Ended);
        Assert.True(first.Disposed && second.Disposed);
    }

    private sealed class WaitingClock : ISimulationClock
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Ended { get; private set; }
        public long GetTimestamp() => 0;
        public double GetElapsedSeconds(long start, long end) => 0;
        public async Task DelayAsync(long milliseconds, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Ended = true; }
        }
    }

    private sealed class Transport : ISimulationTransport
    {
        public event Action? ConnectionLost { add { } remove { } }
        public bool Disposed { get; private set; }
        public bool FailConnection { get; init; }
        public List<SimulationPublication> Messages { get; } = [];
        public Task ConnectAsync(CancellationToken token) => FailConnection ? Task.FromException(new IOException()) : Task.CompletedTask;
        public Task PublishAsync(SimulationPublication message, CancellationToken token) { Messages.Add(message); return Task.CompletedTask; }
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }
}
