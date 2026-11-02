using Amr.Core;
using Xunit;

namespace Amr.Core.Tests;

public sealed class CommandTrackerTests
{
    [Fact]
    public void ResetResultsMatchTargetAndIdWithoutRewritingFailedMove()
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5));
        string move = tracker.Register(new("ONE", CommandKind.MOVE, 8, 2)).CommandId!;
        tracker.Receive(new("ONE", move, "FAILURE", "FAULT_INTERRUPTED", 1, DateTimeOffset.UtcNow));
        string reset = tracker.Register(new("ONE", CommandKind.RESET)).CommandId!;
        Assert.Equal(AckDisposition.UnknownRequest, tracker.Receive(new("TWO", reset, "SUCCESS", "RESET_COMPLETED", 2, DateTimeOffset.UtcNow)));
        Assert.Equal(CommandResult.Unconfirmed, tracker.Find("ONE", reset)!.Result);
        var ack = new CommandResponseInput("ONE", reset, "SUCCESS", "RESET_COMPLETED", 2, DateTimeOffset.UtcNow);
        Assert.Equal(AckDisposition.Matched, tracker.Receive(ack));
        Assert.Equal(AckDisposition.Duplicate, tracker.Receive(ack));
        Assert.Equal(CommandResult.Success, tracker.Find("ONE", reset)!.Result);
        Assert.Equal(CommandResult.Failure, tracker.Find("ONE", move)!.Result);
    }

    [Fact]
    public void EarlyAckIsNotOverwrittenByPublicationReturn()
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5));
        string id = tracker.Register(new("ONE", CommandKind.MOVE, 4, 2)).CommandId!;
        tracker.BeginPublication(id);
        Assert.Equal(AckDisposition.Matched, tracker.Receive(new("ONE", id, "SUCCESS", "ARRIVED", 100, DateTimeOffset.UtcNow)));
        tracker.PublicationReturned(id, true);
        var command = tracker.Find("ONE", id)!;
        Assert.Equal(CommandResult.Success, command.Result); Assert.Equal(PublishObservation.Returned, command.Publish);
        Assert.Equal(100, command.AckTimestampUnixMs);
    }

    [Fact]
    public void OtherTargetUnknownIdAndDuplicateCannotChangeResultAndConflictPreservesFirst()
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5));
        string id = tracker.Register(new("ONE", CommandKind.STOP)).CommandId!;
        var ack = new CommandResponseInput("TWO", id, "SUCCESS", "STOPPED", 1, DateTimeOffset.UtcNow);
        Assert.Equal(AckDisposition.UnknownRequest, tracker.Receive(ack));
        Assert.Equal(AckDisposition.UnknownRequest, tracker.Receive(ack with { AmrId = "ONE", CommandId = "OTHER" }));
        ack = ack with { AmrId = "ONE" };
        Assert.Equal(AckDisposition.Matched, tracker.Receive(ack));
        long revision = tracker.Find("ONE", id)!.Revision;
        Assert.Equal(AckDisposition.Duplicate, tracker.Receive(ack with { Timestamp = 2 }));
        Assert.Equal(revision, tracker.Find("ONE", id)!.Revision);
        Assert.Equal(AckDisposition.Conflict, tracker.Receive(ack with { Result = "FAILURE" }));
        var command = tracker.Find("ONE", id)!;
        Assert.Equal(CommandResult.Success, command.Result); Assert.True(command.HasConflictingResult);
        Assert.Equal("STOPPED", command.Reason);
    }

    [Fact]
    public void DeadlineStartsAtPublicationAndLateAckKeepsPastTimeout()
    {
        var clock = new ManualClock();
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5), clock: clock);
        string id = tracker.Register(new("ONE", CommandKind.MOVE, 4, 2)).CommandId!;
        clock.Advance(20); Assert.False(tracker.CheckDeadlines());
        tracker.BeginPublication(id); clock.Advance(5);
        Assert.True(tracker.CheckDeadlines()); Assert.False(tracker.CheckDeadlines());
        Assert.Equal(CommandResult.Unconfirmed, tracker.Find("ONE", id)!.Result);
        tracker.Receive(new("ONE", id, "SUCCESS", "ARRIVED", 100, clock.GetUtcNow()));
        Assert.Equal(CommandResult.Success, tracker.Find("ONE", id)!.Result);
        Assert.True(tracker.Find("ONE", id)!.ResultDeadlineExceeded);
    }

    [Fact]
    public void CapacityReservesStopAndDoesNotEvictExistingCommands()
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5), 2);
        Assert.True(tracker.Register(new("ONE", CommandKind.MOVE, 4, 2)).Accepted);
        Assert.False(tracker.Register(new("ONE", CommandKind.MOVE, 5, 3)).Accepted);
        Assert.True(tracker.Register(new("ONE", CommandKind.STOP)).Accepted);
        Assert.False(tracker.Register(new("ONE", CommandKind.STOP)).Accepted);
        Assert.Equal(2, tracker.Read().Length);
    }

    [Theory]
    [InlineData(false, CommandResult.Unconfirmed, PublishObservation.Unknown)]
    [InlineData(true, CommandResult.Failure, PublishObservation.NotAttempted)]
    public void UnknownPublicationAndDefinitelyNotSentAreDistinct(bool notSent, CommandResult expected, PublishObservation publication)
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5));
        string id = tracker.Register(new("ONE", CommandKind.STOP)).CommandId!;
        tracker.PublicationReturned(id, false, "TEST", notSent);
        Assert.Equal(expected, tracker.Find("ONE", id)!.Result);
        Assert.Equal(publication, tracker.Find("ONE", id)!.Publish);
    }

    [Fact]
    public void RecoveryStopPreservesUnknownMoveButAllowsANewMission()
    {
        var tracker = new CommandTracker(TimeSpan.FromSeconds(5));
        string move = tracker.Register(new("ONE", CommandKind.MOVE, 4, 2)).CommandId!;
        string stop = tracker.Register(new("ONE", CommandKind.STOP)).CommandId!;
        tracker.Receive(new("ONE", stop, "SUCCESS", "STOPPED", 100, DateTimeOffset.UtcNow));
        var previous = tracker.Find("ONE", move)!;
        Assert.Equal(CommandResult.Unconfirmed, previous.Result);
        Assert.Null(previous.AckResult); Assert.True(previous.SupersededByConfirmedStop);
        Assert.True(tracker.Find("ONE", stop)!.RequestSequence > previous.RequestSequence);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        internal void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
