using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Amr.Communication.Mqtt;
using Amr.Core;
using MQTTnet;

namespace Amr.Infrastructure;

public sealed record MqttFleetDevice(string AmrId, MapStation? Destination = null);

/// <summary>장비별 검증된 단일 공급자를 소유하며 화면에는 단일 Revision의 전체 스냅샷을 제공한다.</summary>
public sealed class FleetMqttAmrDataProvider : IAmrDataProvider, ICommandDispatcher, IAmrMissionPlanner
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MqttAmrDataProvider> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AmrChange> _latest = new(StringComparer.Ordinal);
    private readonly HashSet<Channel<AmrChange>> _subscriptions = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly int _capacity;
    private Task[] _pumps = [];
    private Task? _stop;
    private bool _started, _disposed;
    private long _revision;

    public FleetMqttAmrDataProvider(MqttConnectionSettings settings, IEnumerable<MqttFleetDevice> devices,
        ReconnectOptions? reconnect = null, TimeSpan? commandTimeout = null, int subscriptionCapacity = 64,
        Func<string, IMqttClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(devices);
        var registered = devices.ToArray();
        if (registered.Length == 0 || registered.Any(x => x is null || string.IsNullOrWhiteSpace(x.AmrId) ||
            x.AmrId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#')) ||
            registered.Select(x => x.AmrId).Distinct(StringComparer.Ordinal).Count() != registered.Length)
            throw new ArgumentException("Fleet에는 서로 다른 유효한 AMR ID가 필요합니다.", nameof(devices));
        if (registered.Any(x => x.Destination is { } d && (!double.IsFinite(d.X) || !double.IsFinite(d.Y))))
            throw new ArgumentException("목적지 좌표는 유한해야 합니다.", nameof(devices));
        if (subscriptionCapacity < 1) throw new ArgumentOutOfRangeException(nameof(subscriptionCapacity));
        var options = reconnect ?? new();
        options.Validate();
        if (commandTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        _capacity = subscriptionCapacity;
        foreach (var device in registered)
        {
            // ClientId를 공유하면 Broker가 이전 접속을 끊는다. 각 세션에 별도 설정/ClientId를 만든다.
            var connection = new MqttConnectionSettings(settings.Host, settings.Port, (int)settings.ConnectTimeout.TotalSeconds);
            _children.Add(device.AmrId, new(connection, device.AmrId, options,
                clientFactory: clientFactory is null ? null : () => clientFactory(device.AmrId),
                commandTimeout: commandTimeout, destination: device.Destination));
            _latest.Add(device.AmrId, new(0, AmrChangeKind.InitialSnapshot, [], [], [])
            { Connection = new($"{settings.Host}:{settings.Port}", device.AmrId, DataConnectionState.Disconnected,
                0, options.MaxAttempts, options.Delay.TotalSeconds, 0, false) { RegisteredAmrIds = [device.AmrId] } });
        }
    }

    public ProviderMode Mode => ProviderMode.Actual;

    public void Start()
    {
        lock (_gate)
        {
            Check(default);
            if (_started) throw new InvalidOperationException("이미 시작한 공급자입니다.");
            _started = true;
            // Pump는 MQTT 시작 전 초기 스냅샷과 구독 경계를 확보한다.
            _pumps = _children.Select(x => PumpAsync(x.Key, x.Value)).ToArray();
            foreach (var child in _children.Values) child.Start();
        }
    }

    private async Task PumpAsync(string id, MqttAmrDataProvider child)
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    await foreach (var change in child.WatchAsync(_lifetime.Token).ConfigureAwait(false))
                        lock (_gate)
                        {
                            if (_disposed) return;
                            _latest[id] = change;
                            Publish();
                        }
                    return;
                }
                catch (AmrResyncRequiredException) { /* 이 장비의 새 전체 스냅샷으로 재동기화한다. */ }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var previous = _latest[id];
                _latest[id] = previous with
                {
                    Devices = previous.Devices.Select(x => x with { BrokerConnection = ConnectionObservation.Disconnected,
                        Liveness = LivenessObservation.Unknown }).ToImmutableArray(),
                    Connection = previous.Connection! with { State = DataConnectionState.Failed,
                        TelemetryIsCurrent = false, DataWarning = $"수신 중단: {error.GetType().Name}" }
                };
                Publish();
            }
            // 한 장비의 수신 작업 실패는 다른 장비를 취소하지 않는다.
            await child.DisposeAsync().ConfigureAwait(false);
        }
    }

    private AmrChange Capture(AmrChangeKind kind)
    {
        var connections = _latest.ToImmutableDictionary(x => x.Key, x => x.Value.Connection!, StringComparer.Ordinal);
        var all = connections.Values.ToArray();
        var first = all[0];
        // 부분 연결은 Connecting 요약 + 장비별 상세로 표시한다. 모든 연결/모든 위치 수신을 분리한다.
        var state = all.All(x => x.State == DataConnectionState.Connected) ? DataConnectionState.Connected
            : all.Select(x => x.State).Distinct().Count() == 1 ? first.State : DataConnectionState.Connecting;
        return new(_revision, kind,
            _latest.Values.SelectMany(x => x.Devices).Select(x => x with { Revision = _revision }).ToImmutableArray(),
            _latest.Values.SelectMany(x => x.Alarms).ToImmutableArray(),
            _latest.Values.SelectMany(x => x.Commands).ToImmutableArray())
        {
            DeviceConnections = connections,
            Connection = first with { AmrId = "Fleet", State = state, RegisteredAmrIds = _children.Keys.ToImmutableArray(),
                TelemetryIsCurrent = all.All(x => x.TelemetryIsCurrent), RejectedMessages = all.Sum(x => x.RejectedMessages),
                HistoryAtCapacity = all.Any(x => x.HistoryAtCapacity),
                DataWarning = string.Join(" · ", connections.Where(x => x.Value.DataWarning is not null).Select(x => $"{x.Key}: {x.Value.DataWarning}")) },
            History = _latest.Values.SelectMany(x => x.History).OrderBy(x => x.ObservedAt).ToImmutableArray()
        };
    }

    private void Publish()
    {
        ++_revision;
        var snapshot = Capture(AmrChangeKind.DeviceChanged);
        foreach (var subscriber in _subscriptions.ToArray())
        {
            if (subscriber.Writer.TryWrite(snapshot)) continue;
            while (subscriber.Reader.TryRead(out _)) { }
            subscriber.Writer.TryComplete(new AmrResyncRequiredException());
            _subscriptions.Remove(subscriber);
        }
    }

    public async IAsyncEnumerable<AmrChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<AmrChange>(new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.Wait });
        lock (_gate) { Check(cancellationToken); channel.Writer.TryWrite(Capture(AmrChangeKind.InitialSnapshot)); _subscriptions.Add(channel); }
        try { await foreach (var value in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return value; }
        finally { lock (_gate) _subscriptions.Remove(channel); }
    }

    public Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken cancellationToken = default)
    { lock (_gate) { Check(cancellationToken); return Task.FromResult<IReadOnlyList<AmrSnapshot>>(Capture(AmrChangeKind.InitialSnapshot).Devices); } }
    public Task<AmrSnapshot?> GetDeviceAsync(string amrId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId);
        lock (_gate) { Check(cancellationToken); return Task.FromResult(Capture(AmrChangeKind.InitialSnapshot).Devices.FirstOrDefault(x => x.AmrId == amrId)); }
    }
    public Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);
        lock (_gate) { Check(cancellationToken); return Task.FromResult<IReadOnlyList<AlarmSnapshot>>(Capture(AmrChangeKind.InitialSnapshot).Alarms.Where(x =>
            (query.AmrId is null || x.AmrId == query.AmrId) && (query.From is null || x.ObservedAt >= query.From) && (query.To is null || x.ObservedAt <= query.To)).ToArray()); }
    }
    public Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);
        lock (_gate) { Check(cancellationToken); return Task.FromResult(new HistoryResult(Capture(AmrChangeKind.InitialSnapshot).History.Where(x =>
            (query.AmrId is null || x.AmrId == query.AmrId) && (query.Type is null || x.Type == query.Type) &&
            (query.From is null || x.ObservedAt >= query.From) && (query.To is null || x.ObservedAt <= query.To)).ToImmutableArray())); }
    }
    public Task<CommandSnapshot?> GetCommandAsync(string amrId, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId); ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        lock (_gate) { Check(cancellationToken); return _children.TryGetValue(amrId, out var child)
            ? child.GetCommandAsync(amrId, commandId, cancellationToken) : Task.FromResult<CommandSnapshot?>(null); }
    }
    public Task<CommandSubmission> DispatchAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate) { Check(cancellationToken); return _children.TryGetValue(request.AmrId, out var child)
            ? child.DispatchAsync(request, cancellationToken)
            : Task.FromResult(new CommandSubmission(false, null, CommandRejection.UnknownDevice, "등록된 AMR이 아닙니다.")); }
    }
    public Task SetDestinationAsync(string amrId, MapStation destination, CancellationToken cancellationToken = default)
    {
        lock (_gate) { Check(cancellationToken); return _children.TryGetValue(amrId, out var child)
            ? child.SetDestinationAsync(amrId, destination, cancellationToken) : throw new ArgumentException("등록된 AMR이 아닙니다."); }
    }
    private static void Validate(AmrQuery query)
    { ArgumentNullException.ThrowIfNull(query); if (query.From > query.To) throw new ArgumentException("조회 기간이 잘못되었습니다."); }
    private void Check(CancellationToken token) { ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested(); }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { if (_stop is not null) return new(_stop); _disposed = true; return new(_stop = Task.Run(StopAsync)); }
    }
    private async Task StopAsync()
    {
        try
        {
            _lifetime.Cancel();
            await Task.WhenAll(_children.Values.Select(x => x.DisposeAsync().AsTask()).Concat(_pumps)).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) { foreach (var channel in _subscriptions) channel.Writer.TryComplete(); _subscriptions.Clear(); }
            _lifetime.Dispose();
        }
    }
}
