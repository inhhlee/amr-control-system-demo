using Amr.Communication.Command;
using Amr.Simulator.Configuration;

namespace Amr.Simulator.Simulation;

public sealed record CommandFinalReply(string CommandId, string Result, string Reason, long Timestamp);
public sealed record AlarmTransition(string Code, string Level, string AlarmState, string? Message, long Timestamp);
public sealed record CommandExecutionResult(IReadOnlyList<CommandFinalReply> Replies, string? Diagnostic = null)
{
    public IReadOnlyList<AlarmTransition> Alarms { get; init; } = [];
}

// 한 실행 루프가 명령과 시간을 순차 전달한다. 정지/도착 경합도 이 경계의 순서로 결정한다.
public sealed class CommandDrivenAmr(SimulatorSettings settings, TimeProvider? clock = null, int requestCapacity = 128)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (CommandMessage Request, CommandFinalReply? Reply)> _requests = new(StringComparer.Ordinal);
    private SimulatedAmr? _motion;
    private string? _movingCommand;
    private readonly Dictionary<int, (FaultScenario Scenario, bool Resolved)> _faults = [];
    private readonly Dictionary<string, FaultScenario> _latchedAlarms = new(StringComparer.Ordinal);
    public Position2D Position { get; private set; } = settings.InitialPosition;
    public string State { get; private set; } = "IDLE";
    public string AmrId => settings.AmrId;
    public double SpeedMps => State == "BUSY" ? settings.SpeedMps : 0;
    public bool CausesResolved => _faults.Values.All(x => x.Resolved);

    // route 모드의 최초 경로는 외부 명령이 아니므로 가짜 cmdId/ACK를 생성하지 않는다.
    // 실행 소유자가 준비 완료 직후 한 번만 호출한다. STOP/FAULT/RESET 후 자동 재개하지 않는다.
    public void StartConfiguredRoute()
    {
        if (State != "IDLE" || _motion is not null)
            throw new InvalidOperationException("Configured route can only start from idle.");
        _motion = new(settings);
        if (_motion.IsCompleted) { _motion = null; return; }
        State = "BUSY";
    }

    public CommandExecutionResult Execute(CommandMessage command)
    {
        if (_requests.TryGetValue(command.CommandId, out var existing))
        {
            if (existing.Request.Type != command.Type || existing.Request.Parameters != command.Parameters)
                return new([], "REQUEST_ID_CONFLICT"); // 원 요청의 결과를 잘못 끝내는 같은 ID 응답을 만들지 않는다.
            return new(existing.Reply is null ? [] : [existing.Reply], "DUPLICATE_REQUEST");
        }
        if (_requests.Count >= requestCapacity || command.Type != "STOP" && _requests.Count >= requestCapacity - 1)
            return new([Reply(command.CommandId, "FAILURE", "REQUEST_CAPACITY_REACHED")]);
        _requests.Add(command.CommandId, (command, null));
        if (command.Type == "RESET") return ExecuteReset(command.CommandId);
        if (command.Type == "STOP")
        {
            var replies = new List<CommandFinalReply>();
            if (_movingCommand is { } interrupted) replies.Add(Finish(interrupted, "FAILURE", "STOP_INTERRUPTED"));
            _motion = null; _movingCommand = null;
            if (State != "FAULT") State = "STOPPED"; // STOP은 장애 래치를 해제하지 않는다.
            replies.Add(Finish(command.CommandId, "SUCCESS", State == "FAULT" ? "STOPPED_FAULT_LATCHED" : "STOPPED"));
            return new(replies);
        }
        if (State == "BUSY") return Complete(command.CommandId, "FAILURE", "BUSY_MOVE_REJECTED");
        if (State == "FAULT") return Complete(command.CommandId, "FAILURE", "FAULT_ACTIVE");
        if (command.Type != "MOVE" || command.Parameters.X is not double x || command.Parameters.Y is not double y || !double.IsFinite(x) || !double.IsFinite(y))
            return Complete(command.CommandId, "FAILURE", "INVALID_MOVE");
        _motion = new(new SimulatorSettings(AmrId, Position, settings.SpeedMps, settings.UpdateIntervalMs, [new(x, y)]));
        if (_motion.IsCompleted) { _motion = null; State = "IDLE"; return Complete(command.CommandId, "SUCCESS", "ARRIVED"); }
        _movingCommand = command.CommandId; State = "BUSY";
        return new([]); // 접수 시 최종 ACK를 보내지 않는다.
    }
    public IReadOnlyList<CommandFinalReply> Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_motion is null) return [];
        _motion.Advance(seconds); Position = _motion.Position;
        if (!_motion.IsCompleted) return [];
        _motion = null; State = "IDLE";
        string? id = _movingCommand; _movingCommand = null;
        return id is null ? [] : [Finish(id, "SUCCESS", "ARRIVED")];
    }

    // occurrenceId는 시나리오의 인덱스다. 같은 Code의 겹친 원인을 개별 추적한다.
    // 아래 API도 MQTT 콜백에서 호출하지 않고 실행 루프만 호출한다.
    public CommandExecutionResult ApplyFault(int occurrenceId, FaultScenario scenario)
    {
        if (!_faults.TryAdd(occurrenceId, (scenario, false))) return new([]);
        var replies = new List<CommandFinalReply>();
        if (_movingCommand is { } interrupted) replies.Add(Finish(interrupted, "FAILURE", "FAULT_INTERRUPTED"));
        _motion = null; _movingCommand = null; State = "FAULT";
        // 원인이 반복되어도 아직 해제되지 않은 같은 알람은 매번 ACTIVE로 재발행하지 않는다.
        bool raised = _latchedAlarms.TryAdd(scenario.Code, scenario);
        return new(replies) { Alarms = raised ? [Alarm(scenario, "ACTIVE")] : [] };
    }

    public void ResolveFaultCause(int occurrenceId)
    {
        if (_faults.TryGetValue(occurrenceId, out var fault)) _faults[occurrenceId] = (fault.Scenario, true);
        // 원인만 해소됨. FAULT/알람/좌표를 변경하거나 중단 MOVE를 재개하지 않는다.
    }

    private CommandExecutionResult ExecuteReset(string commandId)
    {
        if (State == "BUSY") return Complete(commandId, "FAILURE", "BUSY_RESET_REJECTED");
        if (State != "FAULT") return Complete(commandId, "FAILURE", "NOT_IN_FAULT");
        if (!CausesResolved) return Complete(commandId, "FAILURE", "FAULT_CAUSE_ACTIVE");
        var cleared = _latchedAlarms.Values.Select(x => Alarm(x, "CLEARED")).ToArray();
        _latchedAlarms.Clear();
        State = "IDLE"; // Position은 그대로. 중단된 MOVE도 복원하지 않는다.
        return Complete(commandId, "SUCCESS", "RESET_COMPLETED") with { Alarms = cleared };
    }

    private AlarmTransition Alarm(FaultScenario fault, string state) =>
        new(fault.Code, fault.Level, state, fault.Message, _clock.GetUtcNow().ToUnixTimeMilliseconds());
    private CommandExecutionResult Complete(string id, string result, string reason) => new([Finish(id, result, reason)]);
    private CommandFinalReply Finish(string id, string result, string reason)
    {
        var reply = Reply(id, result, reason);
        _requests[id] = (_requests[id].Request, reply);
        return reply;
    }
    private CommandFinalReply Reply(string id, string result, string reason) => new(id, result, reason, _clock.GetUtcNow().ToUnixTimeMilliseconds());
}
