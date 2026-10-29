using System.Collections.Immutable;

namespace Amr.Core;

/// <summary>등록 장비의 통신 관측을 업무 상태·미션 보류·업무 경보로 반영한다.</summary>
/// <remarks>이번 사용자 선택: 복구는 경보를 해제하지만 미션 재개는 명시적으로 요청한다.
/// 관제 미션 보류는 차량의 물리적 정지 명령이 아니다. 저장은 현재 프로세스 메모리 범위다.</remarks>
public sealed class AmrDomainManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;
    private readonly Dictionary<(string Id, string Code), AlarmSnapshot> _alarms = [];
    private Guid _sessionId;
    private long _revision;

    public AmrDomainManager(IEnumerable<string> amrIds)
    {
        ArgumentNullException.ThrowIfNull(amrIds);
        var ids = amrIds.ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Register distinct, nonempty AMR IDs.", nameof(amrIds));
        _entries = ids.ToDictionary(id => id, _ => new Entry(), StringComparer.Ordinal);
    }

    public void BeginSession(Guid sessionId)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(sessionId));
        lock (_gate)
        {
            if (sessionId == _sessionId) return;
            _sessionId = sessionId;
            foreach (var entry in _entries.Values)
            {
                entry.Sequence = 0;
                entry.Broker = ConnectionObservation.Unknown;
                entry.Liveness = LivenessObservation.Unknown;
                entry.Communication = AmrCommunicationState.UNKNOWN;
                entry.LastHeartbeat = null;
                entry.Reason = null;
                if (entry.Mission.State == AmrMissionState.Running) entry.Mission = entry.Mission with { State = AmrMissionState.OnHold };
            }
            _revision++;
        }
    }

    public bool Observe(AmrCommunicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            if (input.SessionId != _sessionId || _sessionId == Guid.Empty || !_entries.TryGetValue(input.AmrId, out var entry)
                || input.Sequence <= entry.Sequence) return false;
            entry.Sequence = input.Sequence;
            entry.Broker = input.BrokerConnection;
            entry.Liveness = input.Monitoring && input.BrokerConnection == ConnectionObservation.Connected ? input.Liveness : LivenessObservation.Unknown;
            if (input.LastHeartbeatReceivedAt is { } received) entry.LastHeartbeat = received;
            entry.Reason = input.Reason;
            entry.Communication = input.BrokerConnection == ConnectionObservation.Disconnected ? AmrCommunicationState.BROKER_LOST
                : !input.Monitoring ? AmrCommunicationState.SESSION_STOPPED
                : entry.Liveness == LivenessObservation.Unresponsive ? AmrCommunicationState.COMM_LOST
                : entry.Liveness == LivenessObservation.Alive ? AmrCommunicationState.CONNECTED : AmrCommunicationState.UNKNOWN;
            _revision++;
            if (entry.Communication is AmrCommunicationState.COMM_LOST or AmrCommunicationState.BROKER_LOST)
            {
                string code = entry.Communication.ToString();
                string message = code == "COMM_LOST" ? "AMR Heartbeat 응답 없음" : "관제 Broker 연결 끊김";
                if (!_alarms.ContainsKey((input.AmrId, code)))
                    _alarms.Add((input.AmrId, code), new(input.AmrId, code, message, input.ObservedAt, _revision));
            }
            if (entry.Communication == AmrCommunicationState.CONNECTED)
            {
                _alarms.Remove((input.AmrId, "COMM_LOST"));
                _alarms.Remove((input.AmrId, "BROKER_LOST"));
            }
            if (entry.Communication != AmrCommunicationState.CONNECTED && entry.Mission.State == AmrMissionState.Running)
                entry.Mission = entry.Mission with { State = AmrMissionState.OnHold };
            return true;
        }
    }

    public bool ReceiveTelemetry(AmrTelemetryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!double.IsFinite(input.X) || !double.IsFinite(input.Y) || !double.IsFinite(input.SpeedMps) || input.SpeedMps < 0 || !Enum.IsDefined(input.State))
            throw new ArgumentException("Invalid Telemetry values.", nameof(input));
        lock (_gate)
        {
            if (input.SessionId != _sessionId || _sessionId == Guid.Empty || !_entries.TryGetValue(input.AmrId, out var entry)
                || entry.Broker != ConnectionObservation.Connected) return false;
            // 같은 AMR의 검사된 수신 큐 순서를 사용한다. Payload ts로 도착 순서를 재정렬하지 않는다.
            entry.Telemetry = input;
            _revision++;
            return true;
        }
    }

    public bool StartMission(string amrId, string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(amrId, out var entry) || entry.Communication != AmrCommunicationState.CONNECTED
                || entry.Mission.State is AmrMissionState.Running or AmrMissionState.OnHold) return false;
            entry.Mission = new(missionId, AmrMissionState.Running, false);
            _revision++;
            return true;
        }
    }

    public bool ResumeMission(string amrId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(amrId, out var entry) || entry.Communication != AmrCommunicationState.CONNECTED
                || entry.Mission.State != AmrMissionState.OnHold) return false;
            entry.Mission = entry.Mission with { State = entry.Mission.CompletionObserved ? AmrMissionState.Completed : AmrMissionState.Running };
            _revision++;
            return true;
        }
    }

    public bool CompleteMission(string amrId, string missionId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(amrId, out var entry) || entry.Mission.MissionId != missionId
                || entry.Mission.State is not (AmrMissionState.Running or AmrMissionState.OnHold)) return false;
            entry.Mission = entry.Mission with { CompletionObserved = true,
                State = entry.Mission.State == AmrMissionState.OnHold ? AmrMissionState.OnHold : AmrMissionState.Completed };
            _revision++;
            return true;
        }
    }

    public bool CancelMission(string amrId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(amrId, out var entry)) return false;
            entry.Mission = entry.Mission with { State = AmrMissionState.Canceled };
            _revision++;
            return true;
        }
    }

    public AmrDomainSnapshot Read()
    {
        lock (_gate)
        {
            var statuses = _entries.Select(pair => new AmrDomainStatus(pair.Key, pair.Value.Communication,
                pair.Value.Broker, pair.Value.Liveness, pair.Value.LastHeartbeat, pair.Value.Mission, pair.Value.Reason)).ToImmutableArray();
            var devices = _entries.Where(pair => pair.Value.Telemetry is not null).Select(pair => {
                var entry = pair.Value;
                var data = entry.Telemetry!;
                return new AmrSnapshot(pair.Key, data.X, data.Y, data.SpeedMps, data.State, entry.Broker,
                    entry.Liveness, entry.LastHeartbeat, data.ReceivedAt, _revision);
            }).ToImmutableArray();
            return new(_revision, devices, statuses, _alarms.Values.OrderBy(x => x.AmrId).ThenBy(x => x.Code).ToImmutableArray());
        }
    }

    private sealed class Entry
    {
        public long Sequence;
        public ConnectionObservation Broker;
        public LivenessObservation Liveness;
        public AmrCommunicationState Communication;
        public DateTimeOffset? LastHeartbeat;
        public string? Reason;
        public AmrTelemetryInput? Telemetry;
        public AmrMissionSnapshot Mission = new(null, AmrMissionState.None, false);
    }
}
