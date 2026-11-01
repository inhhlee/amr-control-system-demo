using Amr.Communication.Command;
using MQTTnet;

namespace Amr.Communication.Mqtt;

/// <summary>끝난 세션의 정리를 기다린 뒤 새 Client/구독을 생성하는 단일 재접속 소유자.</summary>
/// <remarks>명령 대기열/재전송/업무 ACK 추적은 없다. 관찰 콜백은 짧게 반환해야 한다.</remarks>
public sealed class ReconnectingCommunicationSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly MqttConnectionSettings _settings;
    private readonly string _amrId;
    private readonly ReconnectOptions _options;
    private readonly Action<CommunicationSessionEvent>? _report;
    private readonly Func<IMqttClient> _clientFactory;
    private readonly TimeProvider _clock;
    private readonly bool _showPayload;
    private ControlCommunicationSession? _current;
    private Task<int>? _run;
    private Task? _stop;
    private bool _ready, _stopping;
    private long _generation;
    private long _lastReadyGeneration;
    private int _connections;
    private int _observerFailed;
    private string _state = "Disconnected";

    public ReconnectingCommunicationSession(MqttConnectionSettings settings, string amrId,
        ReconnectOptions? options = null, Action<CommunicationSessionEvent>? report = null,
        Func<IMqttClient>? clientFactory = null, TimeProvider? clock = null, bool showPayload = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(amrId);
        if (amrId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
            throw new ArgumentException("AMR ID must be one topic segment.", nameof(amrId));
        _settings = settings; _amrId = amrId; _options = options ?? new(); _options.Validate();
        _report = report; _clientFactory = clientFactory ?? (() => new MqttClientFactory().CreateMqttClient());
        _clock = clock ?? TimeProvider.System; _showPayload = showPayload;
    }

    public bool IsReady { get { lock (_gate) return _ready && !_stopping; } }
    public string State { get { lock (_gate) return _state; } }

    public Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_run is not null || _stopping) throw new InvalidOperationException("Use a new supervisor.");
            // 실행/취소 콜백을 이 잠금 안에서 호출하지 않는다.
            return _run = Task.Run(() => RunCoreAsync(cancellationToken));
        }
    }

    public Task<CommandPublication> PublishCommandAsync(ReadOnlyMemory<byte> payload, CancellationToken token = default)
    {
        ControlCommunicationSession? session;
        lock (_gate) session = _ready && !_stopping ? _current : null;
        // 다음 연결로 넘기거나 저장하지 않는다. 현재 세션도 자신의 종료 경계에서 다시 검사한다.
        return session is null
            ? Task.FromException<CommandPublication>(new CommandPublishException(CommandPublishFailure.NotConnected))
            : session.PublishCommandAsync(payload, token);
    }

    private async Task<int> RunCoreAsync(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = lifetime.Token;
        int attempts = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                long generation;
                lock (_gate) { generation = ++_generation; _state = "Connecting"; }
                var settings = new MqttConnectionSettings(_settings.Host, _settings.Port, (int)_settings.ConnectTimeout.TotalSeconds);
                ControlCommunicationSession? session = null;
                string reason = "StartupFailed";
                bool connected = false, cleanupFailed = false;
                try
                {
                    session = new(settings, _amrId, (_, _) => Task.CompletedTask,
                        entry => Observe(generation, entry), client: _clientFactory(), clock: _clock, showPayload: _showPayload);
                    lock (_gate) _current = session;
                    await session.StartAsync(token);
                    await session.Completion;
                    reason = session.FailureReason ?? "SessionClosed";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    reason = session?.FailureReason ?? (error is MqttConnectionException mqtt
                        ? "Connect:" + mqtt.Reason : "Start:" + error.GetType().Name);
                    Emit("AttemptFailed", reason);
                }
                finally
                {
                    lock (_gate) { connected = _lastReadyGeneration == generation; _ready = false; _current = null; }
                    if (session is not null)
                        try { await session.DisposeAsync(); }
                        catch (Exception) { cleanupFailed = true; Emit("CleanupFailed", "PreviousSession"); }
                }
                if (token.IsCancellationRequested) break;
                if (cleanupFailed) return 1;
                if (connected) attempts = 0;
                if (connected && reason != "BrokerDisconnected") { Emit("FatalSessionFailure", reason); return 1; }
                if (attempts >= _options.MaxAttempts) { Emit("RetriesExhausted", reason); return 1; }
                var notice = new ReconnectNotice(++attempts, _options.MaxAttempts, _options.Delay.TotalSeconds, reason);
                lock (_gate) _state = "ReconnectWaiting";
                Emit("ReconnectWaiting", notice);
                await Task.Delay(_options.Delay, _clock, token);
                token.ThrowIfCancellationRequested();
                Emit("ReconnectAttempt", notice);
            }
            return Volatile.Read(ref _observerFailed) == 0 ? 0 : 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Volatile.Read(ref _observerFailed) == 0 ? 0 : 1; }
        finally
        {
            lock (_gate) { _ready = false; _current = null; _state = "Stopped"; }
            Emit("SupervisorStopped");
        }
    }

    private void Observe(long generation, CommunicationSessionEvent entry)
    {
        string? readyEvent = null;
        lock (_gate)
        {
            if (_generation != generation || _stopping) return;
            if (entry.Name == "Subscribed")
            {
                _ready = true; _state = "Connected"; _lastReadyGeneration = generation;
                readyEvent = _connections++ == 0 ? "Ready" : "Reconnected";
            }
            if (entry.Name == "Failed") { _ready = false; _state = "Disconnected"; }
        }
        Emit(entry.Name, entry.Value);
        // Subscribed/Failed는 같은 하위 세션 잠금에서 통지된다. 준비 알림도 그 경계에서 내보내
        // 단절 통지가 먼저 표시된 뒤 늦은 CONNECTED가 표시되는 역전을 막는다.
        if (readyEvent is not null && IsReady) Emit(readyEvent, entry.Value);
    }

    private void Emit(string name, object? value = null)
    {
        if (Volatile.Read(ref _observerFailed) != 0) return;
        try { _report?.Invoke(new(name, value)); }
        catch (Exception)
        {
            Interlocked.Exchange(ref _observerFailed, 1);
            _shutdown.Cancel();
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true; _ready = false;
            return _stop ??= Task.Run(async () =>
            {
                _shutdown.Cancel();
                Task<int>? running;
                lock (_gate) running = _run;
                if (running is not null) await running;
                _shutdown.Dispose();
            });
        }
    }
    public ValueTask DisposeAsync() => new(StopAsync());
}
