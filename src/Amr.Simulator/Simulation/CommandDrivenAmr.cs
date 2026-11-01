using Amr.Communication.Command;
using Amr.Simulator.Configuration;

namespace Amr.Simulator.Simulation;

public sealed record CommandFinalReply(string CommandId, string Result, string Reason, long Timestamp);
public sealed record CommandExecutionResult(IReadOnlyList<CommandFinalReply> Replies, string? Diagnostic = null);

// 한 실행 루프가 명령과 시간을 순차 전달한다. 정지/도착 경합도 이 경계의 순서로 결정한다.
public sealed class CommandDrivenAmr(SimulatorSettings settings, TimeProvider? clock = null, int requestCapacity = 128)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (CommandMessage Request, CommandFinalReply? Reply)> _requests = new(StringComparer.Ordinal);
    private SimulatedAmr? _motion;
    private string? _movingCommand;
    public Position2D Position { get; private set; } = settings.InitialPosition;
    public string State { get; private set; } = "IDLE";
    public string AmrId => settings.AmrId;
    public double SpeedMps => State == "BUSY" ? settings.SpeedMps : 0;

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
        if (command.Type == "RESET") return Complete(command.CommandId, "FAILURE", "RESET_NOT_IMPLEMENTED");
        if (command.Type == "STOP")
        {
            var replies = new List<CommandFinalReply>();
            if (_movingCommand is { } interrupted) replies.Add(Finish(interrupted, "FAILURE", "STOP_INTERRUPTED"));
            _motion = null; _movingCommand = null; State = "STOPPED";
            replies.Add(Finish(command.CommandId, "SUCCESS", "STOPPED"));
            return new(replies);
        }
        if (State == "BUSY") return Complete(command.CommandId, "FAILURE", "BUSY_MOVE_REJECTED");
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
        string id = _movingCommand!; _movingCommand = null;
        return [Finish(id, "SUCCESS", "ARRIVED")];
    }
    private CommandExecutionResult Complete(string id, string result, string reason) => new([Finish(id, result, reason)]);
    private CommandFinalReply Finish(string id, string result, string reason)
    {
        var reply = Reply(id, result, reason);
        _requests[id] = (_requests[id].Request, reply);
        return reply;
    }
    private CommandFinalReply Reply(string id, string result, string reason) => new(id, result, reason, _clock.GetUtcNow().ToUnixTimeMilliseconds());
}
