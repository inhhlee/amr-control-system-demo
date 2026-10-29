using System.Text.Json.Serialization;

namespace Amr.Communication.Monitoring;

[JsonConverter(typeof(JsonStringEnumConverter<HeartbeatObservationState>))]
public enum HeartbeatObservationState { AwaitingFirst, Alive, Unresponsive, Stopped }

public sealed record HeartbeatMonitorOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(Timeout));
        if (CheckInterval < TimeSpan.FromMilliseconds(1) || CheckInterval > Timeout) throw new ArgumentOutOfRangeException(nameof(CheckInterval));
    }
}

public sealed record HeartbeatObservation(string AmrId, HeartbeatObservationState State, bool Monitoring,
    DateTimeOffset? LastReceivedAtUtc, double? ElapsedMilliseconds, double TimeoutMilliseconds,
    double CheckIntervalMilliseconds, int TimeoutCount, int RecoveryCount);
public sealed record HeartbeatTransition(string Kind, HeartbeatObservation Observation);

// 한 연결 세션/한 AMR의 관측만 담당한다. 주기 실행·MQTT 수명은 호출자가 소유한다.
public sealed class SingleAmrHeartbeatMonitor
{
    private readonly object _gate = new();
    private readonly string _amrId;
    private readonly TimeProvider _clock;
    private readonly HeartbeatMonitorOptions _options;
    private HeartbeatObservationState _state = HeartbeatObservationState.Stopped;
    private bool _active;
    private long? _lastTimestamp;
    private DateTimeOffset? _lastUtc;
    private int _timeouts, _recoveries;

    public SingleAmrHeartbeatMonitor(string amrId, HeartbeatMonitorOptions? options = null, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId);
        _amrId = amrId;
        _options = options ?? new();
        _options.Validate();
        _clock = clock ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_active) throw new InvalidOperationException("감시 세션이 이미 시작되었습니다.");
            _active = true; _lastTimestamp = null; _lastUtc = null;
            _timeouts = _recoveries = 0; _state = HeartbeatObservationState.AwaitingFirst;
        }
    }

    // 검사를 통과한 Heartbeat만 호출한다. timestamp는 검사/큐 처리 전 수신 지점의 값이다.
    public HeartbeatTransition? Observe(string amrId, DateTimeOffset receivedAtUtc, long receivedTimestamp)
    {
        lock (_gate)
        {
            if (!_active || amrId != _amrId || (_lastTimestamp is long previous && receivedTimestamp <= previous)) return null;
            string? kind = _lastTimestamp is null ? "MonitoringStarted" : _state == HeartbeatObservationState.Unresponsive ? "Recovered" : null;
            if (kind == "Recovered") _recoveries++;
            _lastTimestamp = receivedTimestamp; _lastUtc = receivedAtUtc;
            _state = HeartbeatObservationState.Alive;
            return kind is null ? null : new(kind, ReadCore());
        }
    }

    public HeartbeatTransition? Check()
    {
        lock (_gate)
        {
            if (!_active || _state != HeartbeatObservationState.Alive || _lastTimestamp is not long last ||
                _clock.GetElapsedTime(last, _clock.GetTimestamp()) < _options.Timeout) return null;
            _state = HeartbeatObservationState.Unresponsive; _timeouts++;
            return new("TimedOut", ReadCore());
        }
    }
    public HeartbeatObservation Read() { lock (_gate) return ReadCore(); }
    private HeartbeatObservation ReadCore() => new(_amrId, _state, _active, _lastUtc,
        _lastTimestamp is long last ? Math.Max(0, _clock.GetElapsedTime(last, _clock.GetTimestamp()).TotalMilliseconds) : null,
        _options.Timeout.TotalMilliseconds, _options.CheckInterval.TotalMilliseconds, _timeouts, _recoveries);
    public void Stop()
    {
        lock (_gate) { _active = false; _lastTimestamp = null; _lastUtc = null; _state = HeartbeatObservationState.Stopped; }
    }
}
