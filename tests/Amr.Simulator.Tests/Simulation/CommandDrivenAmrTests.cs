using Amr.Communication.Command;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class CommandDrivenAmrTests
{
    private static SimulatorSettings Settings() => SimulatorSettingsLoader.Parse("""{"amrId":"ONE","initialPosition":{"x":0,"y":0},"speedMps":1,"updateIntervalMs":100,"waypoints":[{"x":10,"y":0}]}""");
    private static CommandMessage Move(string id = "M", double x = 10) => new(id, "MOVE", 1, new(x, 0));
    private static CommandMessage Stop(string id = "S") => new(id, "STOP", 1, new());

    [Fact]
    public void MoveRespondsOnlyAfterArrivalAndDoesNotMoveAgainAfterCompletion()
    {
        var amr = new CommandDrivenAmr(Settings());
        Assert.Empty(amr.Execute(Move()).Replies); Assert.Equal("BUSY", amr.State);
        Assert.Empty(amr.Advance(2)); Assert.Equal(2, amr.Position.X);
        var reply = Assert.Single(amr.Advance(8));
        Assert.Equal(("M", "SUCCESS", "ARRIVED"), (reply.CommandId, reply.Result, reply.Reason));
        Assert.Equal("IDLE", amr.State); Assert.Equal(0, amr.SpeedMps);
        Assert.Empty(amr.Advance(100)); Assert.Equal(10, amr.Position.X);
    }
    [Fact]
    public void StopHasOwnAckAndInterruptedMoveFailsThenNewMoveStartsAtCurrentPosition()
    {
        var amr = new CommandDrivenAmr(Settings());
        amr.Execute(Move()); amr.Advance(2);
        var stopped = amr.Execute(Stop()).Replies;
        Assert.Collection(stopped, move => { Assert.Equal("M", move.CommandId); Assert.Equal("FAILURE", move.Result); },
            stop => { Assert.Equal("S", stop.CommandId); Assert.Equal("SUCCESS", stop.Result); });
        Assert.Equal("STOPPED", amr.State); amr.Advance(100); Assert.Equal(2, amr.Position.X);
        Assert.Empty(amr.Execute(Move("M2", 5)).Replies);
        Assert.Equal(2, amr.Position.X);
        Assert.Equal("SUCCESS", Assert.Single(amr.Advance(3)).Result);
        Assert.Equal(5, amr.Position.X);
    }
    [Fact]
    public void DuplicateAndIdConflictNeverRepeatOrRedirectAnActiveMove()
    {
        var amr = new CommandDrivenAmr(Settings());
        amr.Execute(Move()); amr.Advance(2);
        Assert.Empty(amr.Execute(Move()).Replies);
        Assert.Equal("REQUEST_ID_CONFLICT", amr.Execute(Move("M", 99)).Diagnostic);
        Assert.Equal(2, amr.Position.X);
        var complete = Assert.Single(amr.Advance(8));
        Assert.Equal(complete, Assert.Single(amr.Execute(Move()).Replies));
        Assert.Equal(10, amr.Position.X);
    }
    [Fact]
    public void BusyMoveIsRejectedAndArrivalBeforeStopKeepsSuccessfulMove()
    {
        var amr = new CommandDrivenAmr(Settings());
        amr.Execute(Move());
        Assert.Equal("BUSY_MOVE_REJECTED", Assert.Single(amr.Execute(Move("EXTRA", 1)).Replies).Reason);
        var complete = Assert.Single(amr.Advance(10));
        Assert.Equal("SUCCESS", complete.Result);
        Assert.Equal("S", Assert.Single(amr.Execute(Stop()).Replies).CommandId);
        Assert.Equal(complete, Assert.Single(amr.Execute(Move()).Replies));
    }
    [Fact]
    public void CapacityReservesStopForTheActiveMove()
    {
        var amr = new CommandDrivenAmr(Settings(), requestCapacity: 2);
        amr.Execute(Move()); amr.Advance(2);
        Assert.Equal("REQUEST_CAPACITY_REACHED", Assert.Single(amr.Execute(Move("EXTRA")).Replies).Reason);
        Assert.Equal(2, amr.Execute(Stop()).Replies.Count);
        Assert.Equal("STOPPED", amr.State);
    }
    [Fact]
    public void ResetIsExplicitlyUnsupportedAndZeroDistanceCompletesWithoutFakeMotion()
    {
        var amr = new CommandDrivenAmr(Settings());
        Assert.Equal("RESET_NOT_IMPLEMENTED", Assert.Single(amr.Execute(new("R", "RESET", 1, new())).Replies).Reason);
        Assert.Equal("SUCCESS", Assert.Single(amr.Execute(Move(x: 0)).Replies).Result);
        Assert.Equal("IDLE", amr.State); Assert.Equal(0, amr.Position.X);
    }
}
