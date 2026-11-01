using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;
using static Amr.Simulator.Tests.ConfigurationTests;

namespace Amr.Simulator.Tests;

internal sealed class TestSimulationClock(double secondsPerWait) : ISimulationClock
{
    public long Timestamp { get; set; }
    public List<long> Delays { get; } = [];
    public Action? OnDelay { get; set; }
    public long GetTimestamp() => Timestamp;
    public double GetElapsedSeconds(long start, long end) => (end - start) / 1000d;
    public Task DelayAsync(long milliseconds, CancellationToken cancellationToken)
    {
        Delays.Add(milliseconds);
        Timestamp += (long)(secondsPerWait * 1000);
        OnDelay?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class RunnerTests
{
    private const string Route = """
        {"amrId":"AMR_001","initialPosition":{"x":-1,"y":0},"speedMps":1,
        "updateIntervalMs":100,"waypoints":[{"x":1,"y":0},{"x":1,"y":2}]}
        """;

    [Fact]
    public async Task ActualElapsedTime_NotConfiguredInterval_DrivesMovementAndArrival()
    {
        using var file = new TemporaryConfig(Route);
        var amr = new SimulatedAmr(await SimulatorSettingsLoader.LoadAsync(file.FilePath));
        var clock = new TestSimulationClock(2.5);
        using var output = new StringWriter();
        await new SimulationRunner(clock).RunAsync(amr, output, default);
        Assert.Equal(new long[] {100, 100}, clock.Delays);
        Assert.Contains("AmrMoved AmrId=AMR_001 X=1 Y=0.5 State=BUSY CurrentSpeedMps=1", output.ToString());
        Assert.Contains("SimulationCompleted AmrId=AMR_001 X=1 Y=2 State=IDLE CurrentSpeedMps=0", output.ToString());
        Assert.True(amr.IsCompleted);
    }

    [Fact]
    public async Task OneUpdateCrossingAllWaypoints_RecordsThemInOrderAndCompletesOnce()
    {
        using var file = new TemporaryConfig(Route);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = new TestSimulationClock(10);
        Assert.Equal(0, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, clock: clock));
        var lines = output.ToString().Split(Environment.NewLine);
        var reached = lines.Where(line => line.StartsWith("WaypointReached")).ToArray();
        Assert.Equal(2, reached.Length);
        Assert.Contains("Index=0 X=1 Y=0", reached[0]);
        Assert.Contains("Index=1 X=1 Y=2", reached[1]);
        Assert.Single(lines, line => line.StartsWith("SimulationCompleted"));
        Assert.Single(clock.Delays);
    }

    [Fact]
    public async Task AlreadyAtDestination_CompletesWithoutWaitingOrBusy()
    {
        using var file = new TemporaryConfig(Route.Replace("\"x\":1", "\"x\":-1").Replace("\"y\":2", "\"y\":0"));
        using var output = new StringWriter();
        var amr = new SimulatedAmr(await SimulatorSettingsLoader.LoadAsync(file.FilePath));
        var clock = new TestSimulationClock(1);
        await new SimulationRunner(clock).RunAsync(amr, output, default);
        Assert.Empty(clock.Delays);
        Assert.DoesNotContain("BUSY", output.ToString());
        Assert.Contains("SimulationCompleted", output.ToString());
    }

    [Fact]
    public async Task CancellationDuringWait_DoesNotAdvanceOrClaimArrival()
    {
        using var file = new TemporaryConfig(Route);
        using var cancellation = new CancellationTokenSource();
        var clock = new TestSimulationClock(1);
        clock.OnDelay = () => { if (clock.Delays.Count == 2) cancellation.Cancel(); };
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, cancellation.Token, clock));
        Assert.Contains("SimulationCanceled AmrId=AMR_001 X=0 Y=0 State=BUSY", output.ToString());
        Assert.DoesNotContain("SimulationCompleted", output.ToString());
        Assert.DoesNotContain("InitializationCanceled", output.ToString());
        Assert.Equal(2, clock.Delays.Count);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task ExecutionError_IsReportedAsFailure()
    {
        using var file = new TemporaryConfig(Route);
        var clock = new TestSimulationClock(1) { OnDelay = () => throw new InvalidOperationException("test failure") };
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, clock: clock));
        Assert.Contains("SimulationFailed Reason=InvalidOperationException", error.ToString());
        Assert.DoesNotContain("SimulationCompleted", output.ToString());
    }

    [Fact]
    public async Task LongInterval_RemainsValidAndCanBeCanceled()
    {
        using var file = new TemporaryConfig(Route.Replace("\"updateIntervalMs\":100", "\"updateIntervalMs\":9223372036854775807"));
        using var cancellation = new CancellationTokenSource();
        using var output = new CancelOnStartWriter(cancellation);
        using var error = new StringWriter();
        Assert.Equal(0, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, cancellation.Token));
        Assert.Contains("SimulationCanceled", output.ToString());
        Assert.DoesNotContain("SimulationCompleted", output.ToString());
        Assert.Equal("", error.ToString());
    }

    private sealed class CancelOnStartWriter(CancellationTokenSource cancellation) : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            if (value?.StartsWith("SimulationStarted") == true) cancellation.Cancel();
            return base.WriteLineAsync(value);
        }
    }
}
