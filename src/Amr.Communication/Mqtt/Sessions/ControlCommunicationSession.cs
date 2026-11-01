using Amr.Communication.Models;
using Amr.Communication.Command;
using Amr.Communication.Monitoring;
using MQTTnet;

namespace Amr.Communication.Mqtt;

public sealed record CommunicationSessionEvent(string Name, object? Value = null);

/// <summary>등록한 AMR들의 관제 수신·독립 큐·Heartbeat 감시와 연결 종료를 함께 소유한다.</summary>
/// <remarks>
/// 연결마다 새 인스턴스를 만든다. 전달한 Client도 이 객체가 정리한다.
/// 처리자는 취소에 협조하고, 기록 콜백은 짧게 반환하며 자기 세션의 종료를 동기 대기하지 않는다.
/// StartAsync의 취소는 실행 중인 세션에도 적용된다. Stop/Dispose는 같은 완료를 기다린다.
/// </remarks>
public sealed class ControlCommunicationSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IMqttClient _client;
    private readonly MqttConnection _connection;
    private readonly MqttConnectionSettings _settings;
    private readonly AmrReceiveQueue _queue;
    private readonly MessageSubscription _subscription;
    private readonly SingleAmrHeartbeatMonitor _heartbeat;
    private readonly Dictionary<string, SingleAmrHeartbeatMonitor> _heartbeats;
    private long _observationSequence;
    private readonly HeartbeatMonitorOptions _heartbeatOptions;
    private readonly TimeProvider _clock;
    private readonly Action<CommunicationSessionEvent>? _report;
    private CancellationTokenRegistration _ownerCancellation;
    private Task _monitorTask = Task.CompletedTask;
    private Task? _stopWorker;
    private readonly HashSet<Task> _publications = [];
    private bool _stopping, _approved, _ready;
    private int _startClaimed;
    private string? _failureReason;

    public ControlCommunicationSession(MqttConnectionSettings settings, string amrId,
        Func<ReceivedMessage, CancellationToken, Task> received,
        Action<CommunicationSessionEvent>? report = null, HeartbeatMonitorOptions? heartbeatOptions = null,
        IMqttClient? client = null, TimeProvider? clock = null, bool showPayload = false)
        : this(settings, new[] { amrId }, received, report, heartbeatOptions, client, clock, showPayload) { }

    public ControlCommunicationSession(MqttConnectionSettings settings, IReadOnlyCollection<string> amrIds,
        Func<ReceivedMessage, CancellationToken, Task> received,
        Action<CommunicationSessionEvent>? report = null, HeartbeatMonitorOptions? heartbeatOptions = null,
        IMqttClient? client = null, TimeProvider? clock = null, bool showPayload = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(amrIds);
        string[] ids = amrIds.ToArray();
        if (ids.Length is < 1 or > 64 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Specify 1..64 distinct AMR IDs.", nameof(amrIds));
        foreach (string id in ids)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
                throw new ArgumentException("AMR ID must be one valid topic segment.", nameof(amrIds));
        }
        _settings = settings;
        AmrId = ids[0];
        _report = report;
        _clock = clock ?? TimeProvider.System;
        _heartbeatOptions = heartbeatOptions ?? new();
        _heartbeatOptions.Validate();
        _heartbeats = ids.ToDictionary(id => id, id => new SingleAmrHeartbeatMonitor(id, _heartbeatOptions, _clock), StringComparer.Ordinal);
        _heartbeat = _heartbeats[AmrId];
        _client = client ?? new MqttClientFactory().CreateMqttClient();
        _connection = new(_client);
        _queue = new(async (message, token) => {
            Emit("Received", message);
            token.ThrowIfCancellationRequested();
            await received(message, token);
        }, entry => {
            Emit("QueueEvent", entry);
            if (entry.ExceptionType is not null) Fail("ReceiverHandlerFailed");
        });
        _subscription = new(_client, message => {
            if (!_heartbeats.ContainsKey(message.AmrId)) { Emit("IgnoredAmr", new { message.AmrId, message.Kind }); return; }
            var result = _queue.TryEnqueue(message);
            if (result != QueueAdmission.Accepted) Fail("ReceiveQueue" + result);
        }, error => Emit("Rejected", error),
            error => { Emit("CallbackFailed", error); Fail("ReceiverCallbackFailed"); },
            diagnostic: showPayload ? diagnostic => Emit("MessageDiagnostic", diagnostic) : null,
            showPayload: showPayload,
            normalHeartbeat: (message, stamp) => {
                lock (_gate)
                {
                    if (_stopping || !_heartbeats.TryGetValue(message.Id, out var monitor)) return;
                    var transition = monitor.Observe(message.Id, message.ReceivedAtUtc, stamp);
                    if (transition is not null) Emit("Heartbeat" + transition.Kind, transition.Observation);
                    PublishObservation(message.Id);
                }
            }, clock: _clock);
        _client.DisconnectedAsync += OnDisconnected;
    }

    public string AmrId { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public IReadOnlyList<HeartbeatObservation> Heartbeats => _heartbeats.Values.Select(monitor => monitor.Read()).ToArray();
    /// <remarks>짧은 동기 관측 수신자다. 세션 종료를 동기 대기하지 않는다.</remarks>
    public event Action<AmrCommunicationObservation>? ObservationChanged;
    public HeartbeatObservation Heartbeat => _heartbeat.Read();
    public bool IsConnected { get { lock (_gate) return _ready && !_stopping && _connection.IsConnected; } }

    /// <summary>준비된 현재 연결에서 한 번 발행한다. 종료는 진행 중 발행의 취소·반환까지 기다린다.</summary>
    public async Task<CommandPublication> PublishCommandAsync(ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource lifetime;
        Task<CommandPublication> pending;
        lock (_gate)
        {
            if (!_ready || _stopping) throw new CommandPublishException(CommandPublishFailure.NotConnected);
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            pending = new CommandPublisher(_client).PublishAsync(AmrId, payload, lifetime.Token);
            _publications.Add(pending);
        }
        try { return await pending.ConfigureAwait(false); }
        finally
        {
            lock (_gate) _publications.Remove(pending);
            lifetime.Dispose();
        }
    }
    public string? FailureReason => Volatile.Read(ref _failureReason);
    public Task Completion => _completion.Task;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _startClaimed, 1) != 0)
            throw new InvalidOperationException("Use a new session for each connection.");
        try
        {
            await _lifecycle.WaitAsync();
            try
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_stopping, this);
                    _ownerCancellation = cancellationToken.Register(() => { _ = StopAsync(); });
                }
                Emit("Connecting");
                await _connection.ConnectAsync(_settings, _shutdown.Token);
                lock (_gate)
                {
                    _shutdown.Token.ThrowIfCancellationRequested();
                    if (!_connection.IsConnected) throw new IOException("ReceiverDisconnectedBeforeSubscription");
                    _approved = true;
                    foreach (var monitor in _heartbeats.Values) monitor.Start();
                }
                await _subscription.SubscribeAsync(_shutdown.Token);
                lock (_gate)
                {
                    _shutdown.Token.ThrowIfCancellationRequested();
                    if (!_connection.IsConnected) throw new IOException("ReceiverDisconnectedDuringSubscription");
                    _monitorTask = MonitorAsync();
                    _shutdown.Token.ThrowIfCancellationRequested();
                    _ready = true;
                    Emit("Subscribed", new { _settings.ClientId, _settings.Host, _settings.Port });
                    foreach (string id in _heartbeats.Keys) PublishObservation(id);
                }
                _shutdown.Token.ThrowIfCancellationRequested();
            }
            finally { _lifecycle.Release(); }
        }
        catch (Exception exception)
        {
            if (!_shutdown.IsCancellationRequested)
                Fail(exception is MqttConnectionException mqtt ? "Connect:" + mqtt.Reason : "Start:" + exception.GetType().Name);
            try { await StopAsync(); }
            catch (Exception cleanup) { Emit("CleanupFailed", cleanup.GetType().Name); }
            throw;
        }
    }

    private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
    {
        lock (_gate)
            if (_approved && !_stopping) Fail("BrokerDisconnected");
        return Task.CompletedTask;
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(_heartbeatOptions.CheckInterval, _clock);
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
            {
                lock (_gate)
                {
                    if (_stopping) break;
                    foreach (var pair in _heartbeats)
                    {
                        var transition = pair.Value.Check();
                        if (transition is not null)
                        {
                            Emit("Heartbeat" + transition.Kind, transition.Observation);
                            PublishObservation(pair.Key);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception) { Fail("HeartbeatMonitorFailed"); }
    }

    private void Emit(string name, object? value = null)
    {
        try { _report?.Invoke(new(name, value)); }
        catch (Exception) { if (name is not ("Failed" or "Closed" or "CleanupFailed")) Fail("ObserverCallbackFailed"); }
    }

    private void Fail(string reason) => BeginStop(reason);
    private void PublishObservation(string id, bool terminal = false)
    {
        lock (_gate)
        {
            if (_stopping && !terminal) return;
            try { ObservationChanged?.Invoke(new(SessionId, ++_observationSequence, id, _heartbeats[id].Read(),
                !_stopping && _connection.IsConnected, _failureReason, _clock.GetUtcNow())); }
            catch (Exception) { if (!terminal) Fail("ObservationCallbackFailed"); }
        }
    }
    public Task StopAsync() => BeginStop(null);
    private Task BeginStop(string? failure)
    {
        lock (_gate)
        {
            if (_stopping) return Completion;
            _stopping = true;
            _ready = false;
            _failureReason = failure;
            foreach (var monitor in _heartbeats.Values) monitor.Stop();
            _ = _queue.RequestStop();
            foreach (string id in _heartbeats.Keys) PublishObservation(id, terminal: true);
            Exception? cancellationError = null;
            try { _shutdown.Cancel(); } catch (Exception error) { cancellationError = error; }
            if (failure is not null) Emit("Failed", failure);
            // 콜백 안에서 자기 종료를 기다리지 않고, 소유한 Completion으로 전체 종료를 관찰한다.
            _stopWorker = Task.Run(async () => {
                try { await StopCoreAsync(cancellationError); _completion.TrySetResult(); }
                catch (Exception exception) { _completion.TrySetException(exception); }
            });
            return Completion;
        }
    }

    private async Task StopCoreAsync(Exception? cancellationError)
    {
        await _lifecycle.WaitAsync();
        var errors = new List<Exception>();
        if (cancellationError is not null) errors.Add(cancellationError);
        try
        {
            try { await _monitorTask; } catch (Exception error) { errors.Add(error); }
            Task[] publications;
            lock (_gate) publications = _publications.ToArray();
            // 실패/취소의 발행 결과는 호출자가 받는다. 정리는 모든 발행 대기가 끝난 뒤 수행한다.
            try { await Task.WhenAll(publications); } catch (Exception) { }
            try { await _queue.StopAsync(_settings.ConnectTimeout); } catch (Exception error) { errors.Add(error); }
            try { await _subscription.StopAsync(); } catch (Exception error) { errors.Add(error); }
            _client.DisconnectedAsync -= OnDisconnected;
            try { await _connection.DisconnectAsync(); } catch (Exception error) { errors.Add(error); }
        }
        finally
        {
            TryDispose(_subscription.Dispose);
            _client.DisconnectedAsync -= OnDisconnected;
            TryDispose(_connection.Dispose);
            TryDispose(_ownerCancellation.Dispose);
            _shutdown.Dispose();
            _lifecycle.Release();
            Emit("Closed");
        }
        if (errors.Count != 0) throw new AggregateException("Communication session cleanup failed.", errors);
        void TryDispose(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
