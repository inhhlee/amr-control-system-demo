using Amr.Communication.Command;
using Amr.Communication.Models;
using Amr.Core;

namespace Amr.Infrastructure;

public sealed partial class MqttAmrDataProvider
{
    public Task<CommandSubmission> DispatchAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            CommandSubmission Reject(CommandRejection rejection, string reason) => new(false, null, rejection, reason);
            if (request.AmrId != _connection.AmrId) return Task.FromResult(Reject(CommandRejection.UnknownDevice, "관리 대상 AMR이 아닙니다."));
            if (request.Kind is not (CommandKind.MOVE or CommandKind.STOP)) return Task.FromResult(Reject(CommandRejection.InvalidParameters, "현재 실제 연결은 MOVE/STOP만 지원합니다."));
            if (request.Kind == CommandKind.MOVE && (request.X is not double x || request.Y is not double y || !double.IsFinite(x) || !double.IsFinite(y)))
                return Task.FromResult(Reject(CommandRejection.InvalidParameters, "목적지 X/Y를 설정하세요."));
            if (request.Kind == CommandKind.STOP && (request.X is not null || request.Y is not null))
                return Task.FromResult(Reject(CommandRejection.InvalidParameters, "STOP에는 좌표를 지정하지 않습니다."));
            if (!_session.IsReady || !_connection.TelemetryIsCurrent || _liveness != LivenessObservation.Alive || _device is null)
                return Task.FromResult(Reject(CommandRejection.NotConnected, "서버 연결과 AMR의 새 위치·생존 신호를 확인하세요."));
            var pending = _commands.Read().Where(x => x.Result == CommandResult.Unconfirmed).ToArray();
            if (request.Kind == CommandKind.MOVE && (_device.State is not (AmrState.IDLE or AmrState.STOPPED) || pending.Any(x => x.Kind == CommandKind.MOVE && !x.SupersededByConfirmedStop)))
                return Task.FromResult(Reject(CommandRejection.InvalidState, "이동 중이거나 이전 이동 결과가 미확인입니다."));
            if (request.Kind == CommandKind.STOP && pending.Any(x => x.Kind == CommandKind.STOP && !x.ResultDeadlineExceeded))
                return Task.FromResult(Reject(CommandRejection.InvalidState, "기존 정지 요청의 결과를 기다리고 있습니다."));
            var result = _commands.Register(request);
            if (!result.Accepted) return Task.FromResult(result);
            if (request.Kind == CommandKind.MOVE)
            {
                _source = new("출발 좌표", _device.X, _device.Y);
                _destination = new("목적 좌표", request.X!.Value, request.Y!.Value);
                _currentMoveId = result.CommandId; _movementObservedAt = null;
            }
            AddHistory("명령", $"{request.Kind} 요청 접수 · {result.CommandId}");
            Publish(); // ID/요청을 발행 전에 공개한다. 빠른 ACK도 대조할 수 있다.
            string id = result.CommandId!;
            var previous = _publicationTail;
            var session = _session.SessionId;
            var task = Task.Run(() => PublishRequestAsync(request, id, previous, session));
            _publicationTail = task;
            _publications.Add(task);
            _ = task.ContinueWith(completed => { lock (_gate) _publications.Remove(completed); }, TaskScheduler.Default);
            return Task.FromResult(result);
        }
    }

    private async Task PublishRequestAsync(CommandRequest request, string id, Task previous, Guid? session)
    {
        bool success = false, notSent = false;
        string? reason = null;
        try
        {
            await previous.ConfigureAwait(false); // 접수 순서대로 한 번씩 발행한다. 재접속 대기열은 아니다.
            if (session is null) throw new CommandPublishException(CommandPublishFailure.NotConnected);
            lock (_gate) _commands.BeginPublication(id);
            var message = new CommandMessage(id, request.Kind.ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new(request.X, request.Y));
            await _session.PublishCommandAsync(message.ToPayload(), _lifetime.Token, session).ConfigureAwait(false);
            success = true;
        }
        catch (CommandPublishException error)
        { notSent = error.Reason == CommandPublishFailure.NotConnected; reason = $"명령 발행 실패: {error.Reason}"; }
        catch (Exception error) { reason = $"전달 여부 미확인: {error.GetType().Name}"; }
        lock (_gate)
        {
            _commands.PublicationReturned(id, success, reason, notSent);
            if (!_disposed) { AddHistory("명령", $"{request.Kind} · {id} · {(success ? "MQTT 발행 반환" : reason)}"); Publish(); }
        }
    }

    public Task SetDestinationAsync(string amrId, MapStation destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!double.IsFinite(destination.X) || !double.IsFinite(destination.Y)) throw new ArgumentException("목적지 좌표를 확인하세요.");
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            if (amrId != _connection.AmrId) throw new ArgumentException("관리 대상 AMR이 아닙니다.");
            if (_device?.State == AmrState.BUSY || _commands.Read().Any(x => x.Kind == CommandKind.MOVE && x.Result == CommandResult.Unconfirmed && !x.SupersededByConfirmedStop))
                throw new InvalidOperationException("이동 중이거나 이전 이동 결과가 미확인입니다.");
            _destination = destination;
            if (_device is not null) _source = new("현재 대기 위치", _device.X, _device.Y);
            _currentMoveId = null; _movementObservedAt = null;
            AddHistory("계획", $"목적지 설정 ({destination.X}, {destination.Y}) m");
            Publish();
            return Task.CompletedTask;
        }
    }

    private bool Receive(ReceivedMessage message)
    {
        switch (message.Model)
        {
            case TelemetryMessage point:
                var state = Enum.Parse<AmrState>(point.State);
                if (!_connection.TelemetryIsCurrent && _currentMoveId is { } previousId && _destination is not null &&
                    _commands.Find(_connection.AmrId, previousId)?.Result is CommandResult.Success or CommandResult.Failure &&
                    state is AmrState.IDLE or AmrState.STOPPED && (point.Position.X != _destination.X || point.Position.Y != _destination.Y))
                {
                    // 새 세션에서 다른 대기 위치를 수신했다면 과거 완료 미션을 현재 도착으로 표시하지 않는다.
                    _currentMoveId = null; _source = new("수신 대기 위치", point.Position.X, point.Position.Y); _movementObservedAt = null;
                }
                if (_device?.State != state) AddHistory("상태", $"운행 {state}");
                if (_source is null && state is AmrState.IDLE or AmrState.STOPPED) _source = new("수신 대기 위치", point.Position.X, point.Position.Y);
                if (_currentMoveId is not null && state == AmrState.BUSY) _movementObservedAt ??= point.ReceivedAtUtc;
                _device = new(point.Id, point.Position.X, point.Position.Y, point.Speed, state, BrokerConnection, _liveness, _lastHeartbeat, point.ReceivedAtUtc, _revision);
                _connection = _connection with { TelemetryIsCurrent = true };
                return true;
            case AckMessage ack:
                var disposition = _commands.Receive(new(message.AmrId, ack.CommandId, ack.Result, ack.Reason, ack.Timestamp, ack.ReceivedAtUtc));
                if (disposition is AckDisposition.Duplicate or AckDisposition.UnknownRequest) return false;
                AddHistory("명령", $"{ack.CommandId} · {(disposition == AckDisposition.Conflict ? "상충 응답 · 기존 결과 유지" : ack.Result)} · {ack.Reason}");
                return true;
            case AlarmMessage alarm:
                if (!_alarms.ContainsKey(alarm.Code) && _alarms.Count >= 256)
                { _connection = _connection with { DataWarning = "알람 보관 한도 도달 · 새 코드 수집 중단" }; return true; }
                var previous = _alarms.GetValueOrDefault(alarm.Code);
                var current = new AlarmSnapshot(alarm.Id, alarm.Code, alarm.Message ?? alarm.Code, alarm.ReceivedAtUtc, _revision)
                { Severity = alarm.Level is "ERR" or "FATAL" ? AlarmSeverity.Error : alarm.Level == "WARN" ? AlarmSeverity.Warning : AlarmSeverity.Unknown,
                    IsActive = alarm.AlarmState == "ACTIVE", Note = $"실제 수신 · {alarm.Level} · {alarm.AlarmState}" };
                _alarms[alarm.Code] = current;
                if (previous is null || previous.IsActive != current.IsActive || previous.Message != current.Message || previous.Severity != current.Severity)
                    AddHistory("알람", $"{alarm.Code} · {alarm.AlarmState} · {current.Message}");
                return true;
            default: return false;
        }
    }

    private AmrJourney? Journey()
    {
        if (_source is null || _destination is null || _device is null) return null;
        double Distance(double x, double y) { double scale = Math.Max(Math.Abs(x), Math.Abs(y)); return scale == 0 ? 0 : scale * Math.Sqrt(Math.Pow(x / scale, 2) + Math.Pow(y / scale, 2)); }
        double total = Distance(_destination.X - _source.X, _destination.Y - _source.Y);
        double remaining = Distance(_destination.X - _device.X, _destination.Y - _device.Y);
        var command = _currentMoveId is null ? null : _commands.Find(_connection.AmrId, _currentMoveId);
        var phase = command?.Result == CommandResult.Success && _device.State == AmrState.IDLE && _device.X == _destination.X && _device.Y == _destination.Y
            ? SimulationPhase.Arrived : _device.State == AmrState.BUSY ? SimulationPhase.Moving
            : _device.State == AmrState.STOPPED ? SimulationPhase.Stopped : SimulationPhase.Ready;
        return new(_source, _destination, phase, double.IsFinite(total) ? total : null,
            _currentMoveId is null || !double.IsFinite(total) || !double.IsFinite(remaining) ? null : Math.Clamp(total - remaining, 0, total), _movementObservedAt)
            { DistanceBasis = _currentMoveId is null ? "계획 직선 거리 · 실행거리 미확인" : "명령 출발점–목적지 직선 구간 거리" };
    }

    private void AddHistory(string type, string description)
    {
        if (_history.Count >= 2048) { _connection = _connection with { HistoryAtCapacity = true }; return; }
        _history.Add(new(_connection.AmrId, type, description, DateTimeOffset.UtcNow));
    }
    private async Task MaintainAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                lock (_gate)
                {
                    if (_disposed) return;
                    if (!_commands.CheckDeadlines()) continue;
                    foreach (var command in _commands.Read().Where(x => x.ResultDeadlineExceeded && _recordedTimeouts.Add(x.CommandId)))
                        AddHistory("명령", $"{command.CommandId} · 결과 대기 시간 경과 · 실제 수행 미확인");
                    Publish();
                }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
}
