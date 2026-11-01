namespace Amr.Core;

public enum CommandKind { MOVE, STOP, RESET }
public enum PublishObservation { NotAttempted, MockOnly, Returned, Unknown }
public enum CommandResult { Unconfirmed, Success, Failure }
public enum CommandRejection { UnknownDevice, UnsupportedInMock, InvalidParameters, MockCapacityReached, NotConnected, InvalidState, CapacityReached }

public sealed record CommandRequest(string AmrId, CommandKind Kind, double? X = null, double? Y = null);
public sealed record CommandSubmission(bool Accepted, string? CommandId, CommandRejection? Rejection, string? Reason);

public sealed record CommandSnapshot(
    string AmrId,
    string CommandId,
    CommandKind Kind,
    double? TargetX,
    double? TargetY,
    DateTimeOffset RequestedAt,
    PublishObservation Publish,
    CommandResult Result,
    bool ResultDeadlineExceeded,
    string? AckResult,
    string? Reason,
    long? AckTimestampUnixMs,
    DateTimeOffset? AckReceivedAt,
    bool HasConflictingResult,
    long Revision)
{
    // 후속 STOP의 정지를 확인해 새 이동을 허용해도 원 MOVE의 미확인 결과는 보존한다.
    public bool SupersededByConfirmedStop { get; init; }
    public long RequestSequence { get; init; }
}
