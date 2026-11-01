using System.Collections.Immutable;

namespace Amr.Core;

public enum AckDisposition { Matched, Duplicate, Conflict, UnknownRequest }
public sealed record CommandResponseInput(string AmrId, string CommandId, string Result, string? Reason,
    long Timestamp, DateTimeOffset ReceivedAt);

// 업무 요청의 ID/결과를 소유한다. MQTT 반환·운행 State만으로 동작 완료를 만들지 않는다.
public sealed class CommandTracker(TimeSpan resultTimeout, int capacity = 128, TimeProvider? clock = null)
{
    private readonly Dictionary<string, CommandSnapshot> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _started = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _order = new(StringComparer.Ordinal);
    private long _nextOrder;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long _revision;

    // 이 객체는 공급자의 단일 잠금 경계에서 호출한다. 외부 콜백/네트워크를 실행하지 않는다.
    public ImmutableArray<CommandSnapshot> Read() => _commands.Values.OrderBy(x => x.RequestSequence).ToImmutableArray();
    public CommandSnapshot? Find(string amrId, string id) => _commands.TryGetValue(id, out var value) && value.AmrId == amrId ? value : null;
    public CommandSubmission Register(CommandRequest request)
    {
        if (resultTimeout <= TimeSpan.Zero || capacity < 1) throw new InvalidOperationException("명령 설정을 확인하세요.");
        if (_commands.Count >= capacity || request.Kind != CommandKind.STOP && _commands.Count >= capacity - 1)
            return new(false, null, CommandRejection.CapacityReached, "명령 기록 한도에 도달했습니다(정지 요청 한 자리 예약).");
        string id = $"GUI-{Guid.NewGuid():N}";
        _order.Add(id, ++_nextOrder);
        _commands.Add(id, new(request.AmrId, id, request.Kind, request.X, request.Y, _clock.GetUtcNow(),
            PublishObservation.NotAttempted, CommandResult.Unconfirmed, false, null, null, null, null, false, ++_revision)
            { RequestSequence = _order[id] });
        return new(true, id, null, null);
    }
    public void BeginPublication(string id) => _started.TryAdd(id, _clock.GetTimestamp());
    public void PublicationReturned(string id, bool success, string? reason = null, bool definitelyNotSent = false)
    {
        var current = _commands[id];
        _commands[id] = current with { Publish = success ? PublishObservation.Returned : definitelyNotSent ? PublishObservation.NotAttempted : PublishObservation.Unknown,
            Result = definitelyNotSent && current.AckResult is null ? CommandResult.Failure : current.Result,
            Reason = current.AckResult is null ? reason : current.Reason, Revision = ++_revision };
    }
    public AckDisposition Receive(CommandResponseInput input)
    {
        if (!_commands.TryGetValue(input.CommandId, out var current) || current.AmrId != input.AmrId) return AckDisposition.UnknownRequest;
        if (current.AckResult is not null)
        {
            // 재전송 시각이 달라도 같은 결과/사유는 중복이다. 다른 결과는 첫 결과를 보존한다.
            if (current.AckResult == input.Result && current.Reason == input.Reason) return AckDisposition.Duplicate;
            _commands[input.CommandId] = current with { HasConflictingResult = true, Revision = ++_revision };
            return AckDisposition.Conflict;
        }
        _commands[input.CommandId] = current with { Result = input.Result == "SUCCESS" ? CommandResult.Success : CommandResult.Failure,
            AckResult = input.Result, Reason = input.Reason, AckTimestampUnixMs = input.Timestamp,
            AckReceivedAt = input.ReceivedAt, Revision = ++_revision };
        if (current.Kind == CommandKind.STOP && input.Result == "SUCCESS")
            foreach (var (id, previous) in _commands.ToArray())
                if (previous.AmrId == input.AmrId && previous.Kind == CommandKind.MOVE && previous.Result == CommandResult.Unconfirmed && _order[id] < _order[input.CommandId])
                    _commands[id] = previous with { SupersededByConfirmedStop = true, Revision = ++_revision };
        return AckDisposition.Matched;
    }
    public bool CheckDeadlines()
    {
        bool changed = false;
        foreach (var (id, started) in _started)
        {
            var current = _commands[id];
            if (current.Result != CommandResult.Unconfirmed || current.ResultDeadlineExceeded || _clock.GetElapsedTime(started) < resultTimeout) continue;
            _commands[id] = current with { ResultDeadlineExceeded = true, Reason = "결과 대기 시간 경과 · 실제 수행 미확인", Revision = ++_revision };
            changed = true;
        }
        return changed;
    }
}
