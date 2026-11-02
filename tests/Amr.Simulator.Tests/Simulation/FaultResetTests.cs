using Amr.Communication.Command;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class FaultResetTests
{
    private static SimulatorSettings Settings() => SimulatorSettingsLoader.Parse("""{"amrId":"ONE","initialPosition":{"x":4,"y":2},"speedMps":1,"updateIntervalMs":100,"waypoints":[{"x":100,"y":2}]}""");
    private static CommandMessage Move(string id = "M", double x = 100) => new(id, "MOVE", 1, new(x, 2));
    private static CommandMessage Reset(string id = "R") => new(id, "RESET", 1, new());
    private static FaultScenario Fault(string code = "F1", long at = 1000, long resolved = 3000) => new(code, "ERR", at, resolved, null);

    [Fact]
    public void FaultStopsMoveAndRequiresNewResetAfterCauseResolutionWithoutChangingCoordinates()
    {
        var amr = new CommandDrivenAmr(Settings());
        amr.Execute(Move()); amr.Advance(2);
        var fault = amr.ApplyFault(0, Fault());
        var failedMove = Assert.Single(fault.Replies);
        Assert.Equal(("M", "FAILURE", "FAULT_INTERRUPTED"), (failedMove.CommandId, failedMove.Result, failedMove.Reason));
        Assert.Equal("ACTIVE", Assert.Single(fault.Alarms).AlarmState);
        Assert.Equal(("FAULT", 0d, 6d, 2d), (amr.State, amr.SpeedMps, amr.Position.X, amr.Position.Y));
        Assert.Equal("FAULT_ACTIVE", Assert.Single(amr.Execute(Move("BLOCKED")).Replies).Reason);
        var early = amr.Execute(Reset());
        Assert.Equal("FAULT_CAUSE_ACTIVE", Assert.Single(early.Replies).Reason); Assert.Empty(early.Alarms);
        amr.ResolveFaultCause(0); amr.Advance(100);
        Assert.Equal("FAULT", amr.State); Assert.Equal(6, amr.Position.X);
        Assert.Equal(early.Replies, amr.Execute(Reset()).Replies); // 同 ID 재시도는 기존 실패 보존
        var recovery = amr.Execute(Reset("R2"));
        Assert.Equal("RESET_COMPLETED", Assert.Single(recovery.Replies).Reason);
        Assert.Equal("CLEARED", Assert.Single(recovery.Alarms).AlarmState);
        Assert.Equal(("IDLE", 0d, 6d, 2d), (amr.State, amr.SpeedMps, amr.Position.X, amr.Position.Y));
        Assert.Empty(amr.Advance(100)); Assert.Equal(6, amr.Position.X);
        Assert.Equal(failedMove, Assert.Single(amr.Execute(Move()).Replies));
        Assert.Empty(amr.Execute(Reset("R2")).Alarms);
        amr.Execute(Move("M2", 7)); Assert.Equal("ARRIVED", Assert.Single(amr.Advance(1)).Reason);
    }

    [Fact]
    public void BusyResetIsRejectedAndStopCannotBypassFaultLatch()
    {
        var amr = new CommandDrivenAmr(Settings()); amr.Execute(Move());
        Assert.Equal("BUSY_RESET_REJECTED", Assert.Single(amr.Execute(Reset()).Replies).Reason);
        Assert.Equal("BUSY", amr.State);
        amr.ApplyFault(0, Fault());
        Assert.Equal("STOPPED_FAULT_LATCHED", Assert.Single(amr.Execute(new("S", "STOP", 1, new())).Replies).Reason);
        Assert.Equal("FAULT", amr.State); Assert.False(amr.CausesResolved);
    }

    [Fact]
    public void OverlappingCausesWithSameCodeCannotPrematurelyClearAndCanRecurAfterReset()
    {
        var amr = new CommandDrivenAmr(Settings());
        Assert.Single(amr.ApplyFault(0, Fault()).Alarms);
        Assert.Empty(amr.ApplyFault(0, Fault()).Alarms);
        Assert.Empty(amr.ApplyFault(1, Fault()).Alarms);
        amr.ResolveFaultCause(0);
        Assert.Equal("FAULT_CAUSE_ACTIVE", Assert.Single(amr.Execute(Reset()).Replies).Reason);
        amr.ResolveFaultCause(1);
        Assert.Single(amr.Execute(Reset("R2")).Alarms);
        Assert.Single(amr.ApplyFault(2, Fault()).Alarms); Assert.Equal("FAULT", amr.State);
    }

    [Fact]
    public void DelayedTickStopsAtFaultDeadlineAndResolutionAloneDoesNotClear()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        var amr = new CommandDrivenAmr(Settings()); amr.Execute(Move(x: 6));
        var runner = new FaultScenarioRunner([Fault()], () => elapsed);
        elapsed = TimeSpan.FromSeconds(10); // 한 Tick이 발생/도착/해소 예정 시각을 모두 지남
        var tick = runner.Poll(amr);
        Assert.Equal("FAULT_INTERRUPTED", Assert.Single(tick.Replies).Reason);
        Assert.Equal("ACTIVE", Assert.Single(tick.Alarms).AlarmState);
        Assert.Equal(5, amr.Position.X); Assert.Equal("FAULT", amr.State); Assert.True(amr.CausesResolved);
        Assert.Empty(runner.Poll(amr).Alarms);
        Assert.Equal("SUCCESS", Assert.Single(amr.Execute(Reset()).Replies).Result);
    }

    [Fact]
    public void ZeroTimeFaultAndSimultaneousArrivalHaveDeterministicOrderAndAmrsAreIsolated()
    {
        var first = new CommandDrivenAmr(Settings()); var second = new CommandDrivenAmr(Settings());
        var runner = new FaultScenarioRunner([Fault(at: 0)], () => TimeSpan.Zero);
        Assert.Single(runner.Poll(first).Alarms); Assert.Equal("IDLE", second.State);
        second.Execute(Move(x: 5));
        var sameTime = new FaultScenarioRunner([Fault()], () => TimeSpan.FromSeconds(1));
        var result = sameTime.Poll(second);
        Assert.Equal("ARRIVED", Assert.Single(result.Replies).Reason); // 도착을 먼저 확정한 뒤 장애 적용
        Assert.Equal("FAULT", second.State); Assert.Equal(5, second.Position.X);
    }

    [Fact]
    public void OptionalSingleFaultSettingsReuseStrictValidation()
    {
        const string json = """{"amrId":"ONE","initialPosition":{"x":4,"y":2},"speedMps":1,"updateIntervalMs":100,"waypoints":[{"x":10,"y":2}],"faults":[{"code":"F1","level":"ERR","activateAfterMs":0,"resolveAfterMs":3000}]}""";
        Assert.Equal(0, Assert.Single(SimulatorSettingsLoader.Parse(json).Faults).ActivateAfterMs);
        Assert.Throws<ConfigurationException>(() => SimulatorSettingsLoader.Parse(json.Replace("3000", "0")));
        Assert.Throws<ConfigurationException>(() => SimulatorSettingsLoader.Parse(json.Replace("\"ERR\"", "\"BROKEN\"")));
        Assert.Throws<ConfigurationException>(() => SimulatorSettingsLoader.Parse(json.Replace("\"code\":", "\"code\":\"DUP\",\"code\":")));
    }
}
