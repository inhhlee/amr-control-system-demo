using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class FeedbackSimulationTests
{
    private static MockAmrDataProvider Demo(int capacity = 128) => new(64, capacity, feedbackDemo: true);

    [Fact]
    public async Task RegisteredDevicesIncludeDisconnectedAndAlarmsCarrySeverityAndNotes()
    {
        await using var provider = Demo();
        var devices = await provider.GetDevicesAsync();
        Assert.Equal(5, devices.Count);
        Assert.Equal(4, devices.Count(x => x.Liveness == LivenessObservation.Alive));
        var alarms = await provider.GetAlarmsAsync(new());
        Assert.Single(alarms, x => x.Severity == AlarmSeverity.Error);
        Assert.Single(alarms, x => x.Severity == AlarmSeverity.Warning);
        Assert.All(alarms, x => { Assert.True(x.IsActive); Assert.Empty(x.Code); Assert.NotEmpty(x.Note); });
    }

    [Fact]
    public async Task SourceStartStopPreservePositionAndDoNotResumeOrMoveOtherDevices()
    {
        await using var provider = Demo();
        var dispatcher = new MockCommandDispatcher(provider);
        var other = await provider.GetDeviceAsync("AMR-02");
        provider.SetSimulationSource("AMR-01", "S-02");
        var ready = (await provider.GetDeviceAsync("AMR-01"))!;
        Assert.Equal((4d, 2d), (ready.X, ready.Y));
        Assert.Equal(21, ready.Journey!.TotalDistance);
        var start = await dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10));
        Assert.True(start.Accepted);
        Assert.Equal(CommandResult.Unconfirmed, (await provider.GetCommandAsync("AMR-01", start.CommandId!))!.Result);
        provider.AdvanceSimulations(2);
        Assert.Throws<InvalidOperationException>(() => provider.SetSimulationSource("AMR-01", "S-01"));
        var moving = (await provider.GetDeviceAsync("AMR-01"))!;
        Assert.Equal((7d, 2d), (moving.X, moving.Y));
        Assert.Equal(SimulationPhase.Moving, moving.Journey!.Phase);
        var stop = await dispatcher.DispatchAsync(new("AMR-01", CommandKind.STOP));
        Assert.True(stop.Accepted);
        provider.AdvanceSimulations(100);
        var stopped = (await provider.GetDeviceAsync("AMR-01"))!;
        Assert.Equal((moving.X, moving.Y), (stopped.X, stopped.Y));
        Assert.Equal(0, stopped.SpeedMps);
        Assert.Equal(SimulationPhase.Stopped, stopped.Journey!.Phase);
        Assert.False((await dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10))).Accepted);
        Assert.Equal(other, await provider.GetDeviceAsync("AMR-02"));
        Assert.Null((await provider.GetCommandAsync("AMR-01", stop.CommandId!))!.AckResult);
    }

    [Fact]
    public async Task ArrivalIsSeparateFromAcceptanceAndRecordedExactlyOnce()
    {
        await using var provider = Demo();
        var dispatcher = new MockCommandDispatcher(provider);
        var request = await dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10));
        provider.AdvanceSimulations(100);
        var device = (await provider.GetDeviceAsync("AMR-01"))!;
        Assert.Equal((17d, 10d), (device.X, device.Y));
        Assert.Equal(SimulationPhase.Arrived, device.Journey!.Phase);
        Assert.Equal(0, device.Journey.RemainingDistance);
        Assert.Equal(CommandResult.Success, (await provider.GetCommandAsync("AMR-01", request.CommandId!))!.Result);
        var history = await provider.GetHistoryAsync(new("AMR-01", Type: "상태"));
        Assert.Single(history.Entries);
        provider.AdvanceSimulations(100);
        Assert.Equal(history.Entries.ToArray(), (await provider.GetHistoryAsync(new("AMR-01", Type: "상태"))).Entries.ToArray());
    }

    [Theory]
    [InlineData("AMR-04")]
    [InlineData("AMR-05")]
    public async Task FaultOrDisconnectedDeviceRejectsStartWithReason(string id)
    {
        await using var provider = Demo();
        var before = await provider.GetDeviceAsync(id);
        var result = await new MockCommandDispatcher(provider).DispatchAsync(new(id, CommandKind.MOVE, 17, 10));
        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Reason!);
        Assert.Equal(before, await provider.GetDeviceAsync(id));
        Assert.Contains((await provider.GetHistoryAsync(new(id, Type: "명령"))).Entries, x => x.Description.Contains("거부"));
    }

    [Fact]
    public async Task StopRemainsAvailableAtCommandCapacityAndCloseStopsOnlyOwnedAmr()
    {
        await using var provider = Demo(2);
        var dispatcher = new MockCommandDispatcher(provider);
        Assert.True((await dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10))).Accepted);
        Assert.True((await dispatcher.DispatchAsync(new("AMR-02", CommandKind.MOVE, 17, 10))).Accepted);
        Assert.False((await dispatcher.DispatchAsync(new("AMR-03", CommandKind.MOVE, 17, 10))).Accepted);
        Assert.True((await dispatcher.DispatchAsync(new("AMR-01", CommandKind.STOP))).Accepted);
        provider.StopSimulationOnWindowClose("AMR-02");
        var before = await provider.GetDevicesAsync();
        provider.AdvanceSimulations(50);
        Assert.Equal(before, await provider.GetDevicesAsync());
        Assert.Equal(SimulationPhase.Stopped, (await provider.GetDeviceAsync("AMR-02"))!.Journey!.Phase);
    }

    [Fact]
    public async Task HistoryQueriesHaveInclusiveBoundsAndCannotDeleteStoredEvents()
    {
        await using var provider = Demo();
        provider.SetSimulationSource("AMR-01", "S-02");
        var all = await provider.GetHistoryAsync(new());
        var entry = Assert.Single((await provider.GetHistoryAsync(new("AMR-01", Type: "상태"))).Entries);
        Assert.Single((await provider.GetHistoryAsync(new("AMR-01", entry.ObservedAt, entry.ObservedAt, "상태"))).Entries);
        Assert.Empty((await provider.GetHistoryAsync(new("missing"))).Entries);
        Assert.Equal(all.Entries.ToArray(), (await provider.GetHistoryAsync(new())).Entries.ToArray());
    }

    [Fact]
    public async Task StopAndTicksAreSerializedAndCanceledStartDoesNotExecute()
    {
        await using var provider = Demo();
        var dispatcher = new MockCommandDispatcher(provider);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10), cancelled.Token));
        Assert.Equal(SimulationPhase.Ready, (await provider.GetDeviceAsync("AMR-01"))!.Journey!.Phase);
        await dispatcher.DispatchAsync(new("AMR-01", CommandKind.MOVE, 17, 10));
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 50; i++) provider.AdvanceSimulations(.01); }),
            Task.Run(() => provider.StopSimulationOnWindowClose("AMR-01")));
        var stopped = (await provider.GetDeviceAsync("AMR-01"))!;
        provider.AdvanceSimulations(100);
        Assert.Equal(stopped, await provider.GetDeviceAsync("AMR-01"));
        Assert.Equal(SimulationPhase.Stopped, stopped.Journey!.Phase);
    }
}
