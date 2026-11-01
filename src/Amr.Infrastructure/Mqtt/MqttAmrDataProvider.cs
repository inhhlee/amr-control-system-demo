using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;
using Amr.Core;
using MQTTnet;

namespace Amr.Infrastructure;

/// <summary>trial과 같은 세션을 조회/변경 스트림 계약에 연결한다. 한 인스턴스는 지정한 AMR 한 대를 감시한다.</summary>
public sealed partial class MqttAmrDataProvider : IAmrDataProvider, ICommandDispatcher, IAmrMissionPlanner
{
    private readonly object _gate = new();
    private readonly ReconnectingCommunicationSession _session;
    private readonly HashSet<Channel<AmrChange>> _subscriptions = [];
    private readonly int _capacity;
    private readonly Action<CommunicationSessionEvent>? _report;
    private DataConnectionSnapshot _connection;
    private AmrSnapshot? _device;
    private LivenessObservation _liveness;
    private DateTimeOffset? _lastHeartbeat;
    private long _revision;
    private Task<int>? _run;
    private Task? _stop;
    private bool _disposed;
    private readonly CommandTracker _commands;
    private readonly Dictionary<string, AlarmSnapshot> _alarms = new(StringComparer.Ordinal);
    private readonly List<HistoryEntry> _history = [];
    private readonly HashSet<Task> _publications = [];
    private Task _publicationTail = Task.CompletedTask;
    private readonly HashSet<string> _recordedTimeouts = [];
    private readonly CancellationTokenSource _lifetime = new();
    private Task _maintenance = Task.CompletedTask;
    private MapStation? _source, _destination;
    private string? _currentMoveId;
    private DateTimeOffset? _movementObservedAt;

    public MqttAmrDataProvider(MqttConnectionSettings settings, string amrId = "AMR_001",
        ReconnectOptions? reconnect = null, int subscriptionCapacity = 64,
        Action<CommunicationSessionEvent>? report = null, Func<IMqttClient>? clientFactory = null,
        TimeSpan? commandTimeout = null, MapStation? destination = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (subscriptionCapacity < 1) throw new ArgumentOutOfRangeException(nameof(subscriptionCapacity));
        var options = reconnect ?? new();
        options.Validate();
        _capacity = subscriptionCapacity;
        _report = report;
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        _commands = new(timeout);
        if (destination is not null && (!double.IsFinite(destination.X) || !double.IsFinite(destination.Y)))
            throw new ArgumentException("목적지 좌표는 유한해야 합니다.", nameof(destination));
        _destination = destination;
        _connection = new($"{settings.Host}:{settings.Port}", amrId, DataConnectionState.Disconnected,
            0, options.MaxAttempts, options.Delay.TotalSeconds, 0, false) { RegisteredAmrIds = [amrId] };
        _session = new(settings, amrId, options, Observe, clientFactory);
    }

    public ProviderMode Mode => ProviderMode.Actual;

    // 시작/전체 종료는 Program 책임이다. Watch 종료는 자기 구독만 해제한다.
    public void Start()
    {
        lock (_gate)
        {
            CheckAvailable(default);
            if (_run is not null) throw new InvalidOperationException("이미 시작한 공급자입니다.");
            _run = ObserveCompletionAsync(_session.RunAsync());
            _maintenance = MaintainAsync();
        }
    }

    private async Task<int> ObserveCompletionAsync(Task<int> running)
    {
        int result = await running.ConfigureAwait(false);
        lock (_gate)
        {
            if (!_disposed && result != 0)
            {
                _connection = _connection with { State = DataConnectionState.Failed, TelemetryIsCurrent = false };
                Publish();
            }
        }
        return result;
    }

    private void Observe(CommunicationSessionEvent entry)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var previousConnection = _connection.State;
            var previousLiveness = _liveness;
            bool changed = true;
            switch (entry.Name)
            {
                case "Connecting":
                    _liveness = LivenessObservation.Unknown;
                    _connection = _connection with { State = DataConnectionState.Connecting, TelemetryIsCurrent = false };
                    break;
                case "Ready":
                case "Reconnected":
                    _connection = _connection with { State = DataConnectionState.Connected, RetryAttempt = 0, Reason = null };
                    break;
                case "Failed":
                case "AttemptFailed":
                    _liveness = LivenessObservation.Unknown;
                    _connection = _connection with { State = DataConnectionState.Disconnected, TelemetryIsCurrent = false, Reason = entry.Value as string };
                    break;
                case "ReconnectWaiting" when entry.Value is ReconnectNotice wait:
                    _connection = _connection with { State = DataConnectionState.ReconnectWaiting, RetryAttempt = wait.Attempt, Reason = wait.Reason };
                    break;
                case "ReconnectAttempt":
                    _connection = _connection with { State = DataConnectionState.Reconnecting };
                    break;
                case "RetriesExhausted":
                case "FatalSessionFailure":
                case "CleanupFailed":
                    _connection = _connection with { State = DataConnectionState.Failed, TelemetryIsCurrent = false, Reason = entry.Value as string };
                    break;
                case "SupervisorStopped":
                    if (_connection.State != DataConnectionState.Failed)
                        _connection = _connection with { State = DataConnectionState.Stopped, TelemetryIsCurrent = false };
                    _liveness = LivenessObservation.Unknown;
                    break;
                case "Rejected":
                    _connection = _connection with { RejectedMessages = _connection.RejectedMessages + 1 };
                    break;
                case "Received" when entry.Value is ReceivedMessage message && AcceptingData:
                    changed = Receive(message);
                    break;
                default:
                    if (entry.Value is AmrCommunicationObservation sessionObservation && AcceptingData)
                    {
                        var observation = sessionObservation.Heartbeat;
                        _liveness = observation.State switch
                        {
                            HeartbeatObservationState.Alive => LivenessObservation.Alive,
                            HeartbeatObservationState.Unresponsive => LivenessObservation.Unresponsive,
                            _ => LivenessObservation.Unknown
                        };
                        _lastHeartbeat = observation.LastReceivedAtUtc;
                        if (!sessionObservation.BrokerConnected)
                            _connection = _connection with { TelemetryIsCurrent = false };
                    }
                    else changed = false;
                    break;
            }
            if (changed)
            {
                if (previousConnection != _connection.State) AddHistory("통신", $"서버 상태 {_connection.State}");
                if (previousLiveness != _liveness) AddHistory("통신", $"AMR 응답 {_liveness}");
                Publish();
            }
        }
        // 외부 로그 처리와 MQTT 세션의 종료 호출은 공급자 잠금 밖에서 수행한다.
        _report?.Invoke(entry);
    }

    private bool AcceptingData => _connection.State is DataConnectionState.Connecting or DataConnectionState.Connected;
    private ConnectionObservation BrokerConnection => _connection.State == DataConnectionState.Connected
        ? ConnectionObservation.Connected : _connection.State is DataConnectionState.Failed or DataConnectionState.Disconnected
            or DataConnectionState.ReconnectWaiting or DataConnectionState.Reconnecting
            ? ConnectionObservation.Disconnected : ConnectionObservation.Unknown;

    private void Publish()
    {
        ++_revision;
        if (_device is not null) _device = _device with { BrokerConnection = BrokerConnection,
            Liveness = _liveness, LastHeartbeatReceivedAt = _lastHeartbeat, Revision = _revision,
            Journey = Journey() };
        var snapshot = Capture(AmrChangeKind.DeviceChanged);
        foreach (var subscription in _subscriptions.ToArray())
        {
            if (subscription.Writer.TryWrite(snapshot)) continue;
            while (subscription.Reader.TryRead(out _)) { }
            subscription.Writer.TryComplete(new AmrResyncRequiredException());
            _subscriptions.Remove(subscription);
        }
    }

    private AmrChange Capture(AmrChangeKind kind) => new(_revision, kind,
        _device is null ? [] : [_device], _alarms.Values.ToImmutableArray(), _commands.Read())
        { Connection = _connection, History = _history.ToImmutableArray() };

    public async IAsyncEnumerable<AmrChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<AmrChange>(new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.Wait });
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            // 초기 스냅샷과 구독 등록을 같은 경계에 둬 그 사이의 변경 누락을 막는다.
            channel.Writer.TryWrite(Capture(AmrChangeKind.InitialSnapshot));
            _subscriptions.Add(channel);
        }
        try { await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return change; }
        finally { lock (_gate) _subscriptions.Remove(channel); }
    }

    public Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult<IReadOnlyList<AmrSnapshot>>(_device is null ? [] : [_device]); }
    }
    public Task<AmrSnapshot?> GetDeviceAsync(string amrId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId);
        lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult(_device?.AmrId == amrId ? _device : null); }
    }
    public Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query, cancellationToken);
        lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult<IReadOnlyList<AlarmSnapshot>>(_alarms.Values.Where(x =>
            (query.AmrId is null || x.AmrId == query.AmrId) && (query.From is null || x.ObservedAt >= query.From) && (query.To is null || x.ObservedAt <= query.To)).ToArray()); }
    }
    public Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query, cancellationToken);
        lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult(new HistoryResult(_history.Where(x =>
            (query.AmrId is null || x.AmrId == query.AmrId) && (query.Type is null || x.Type == query.Type) &&
            (query.From is null || x.ObservedAt >= query.From) && (query.To is null || x.ObservedAt <= query.To)).ToImmutableArray())); }
    }
    public Task<CommandSnapshot?> GetCommandAsync(string amrId, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId); ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult(_commands.Find(amrId, commandId)); }
    }
    private void ValidateQuery(AmrQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To) throw new ArgumentException("조회 기간이 잘못되었습니다.", nameof(query));
        lock (_gate) CheckAvailable(token);
    }
    private void CheckAvailable(CancellationToken token) { ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested(); }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_stop is not null) return new(_stop);
            _disposed = true;
            // 세션 취소가 콜백을 실행할 수 있어 전체 종료는 잠금 밖의 작업에서 수행한다.
            return new(_stop = Task.Run(StopCoreAsync));
        }
    }
    private async Task StopCoreAsync()
    {
        try
        {
            _lifetime.Cancel();
            await _session.DisposeAsync().ConfigureAwait(false);
            if (_run is not null) await _run.ConfigureAwait(false);
            Task[] publishing;
            lock (_gate) publishing = _publications.ToArray();
            await Task.WhenAll(publishing.Append(_maintenance)).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                foreach (var subscription in _subscriptions) subscription.Writer.TryComplete();
                _subscriptions.Clear();
            }
            _lifetime.Dispose();
        }
    }
}
