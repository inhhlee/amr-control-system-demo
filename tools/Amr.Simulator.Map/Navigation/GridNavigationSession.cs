using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Map.Navigation;

/// <summary>편집·이동은 같은 잠금으로, MQTT I/O는 잠금 밖에서 수행한다.</summary>
public sealed class GridNavigationSession : IAsyncDisposable
{
    public const int DefaultStepIntervalMs = 600;
    public int StepIntervalMs { get; }
    private readonly int _rows, _cols;
    public string? ConfigurationPath { get; private set; }
    public string AmrId { get; } = "AMR_GRID_" + Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private readonly GridNavigator _navigator;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TextWriter _log;
    private readonly Task _loop;
    private readonly Queue<string> _recent = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly SimulatorMqttOptions? _mqtt;
    private readonly Func<IGridMqttConnection>? _connectionFactory;
    private MqttMapProjection _received;
    private readonly List<GridCell> _receivedTrail = [];
    private Task _execution = Task.CompletedTask;
    private CancellationTokenSource? _runCancellation;
    private bool _closing, _resetRequested, _connected;
    private string? _phase, _failure;
    private string _brokerState = "NotStarted";
    private int _stopReason; // 0=active, 1=user, 2=failure, 3=received completion
    private int _published, _rejected;
    private long? _lastPublishedTimestamp;

    public GridNavigationSession(string evidenceDirectory, SimulatorMqttOptions? mqtt = null,
        Func<IGridMqttConnection>? connectionFactory = null, GridNavigator? navigator = null, string? amrId = null,
        int stepIntervalMs = DefaultStepIntervalMs)
    {
        if (stepIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepIntervalMs));
        StepIntervalMs = stepIntervalMs;
        _navigator = navigator ?? new();
        var map = _navigator.Read().Map;
        _rows = map.Length;
        _cols = map[0].Length;
        if (amrId is not null) AmrId = amrId;
        _mqtt = mqtt;
        _received = new(AmrId);
        if (mqtt is not null && (mqtt.TelemetryMs <= 0 || mqtt.HeartbeatMs <= 0))
            throw new ArgumentException("송신 주기는 양수여야 합니다.", nameof(mqtt));
        EvidencePath = Path.Combine(evidenceDirectory, "grid-navigation.jsonl");
        _log = TextWriter.Synchronized(new StreamWriter(EvidencePath, append: false) { AutoFlush = true });
        _connectionFactory = connectionFactory ?? (mqtt is null ? null : () => new GridMqttConnection(mqtt, AmrId, _log));
        Record("GridReady");
        _loop = mqtt is null ? RunLocalAsync() : Task.CompletedTask;
    }

    public string EvidencePath { get; }
    public string EvidenceDirectory => Path.GetDirectoryName(EvidencePath)!;

    public static async Task<GridNavigationSession> CreateAsync(string configPath, string evidenceRoot, SimulatorMqttOptions? mqtt,
        GridMissionSettings? settings = null)
    {
        settings ??= await GridMissionSettings.LoadAsync(configPath);
        mqtt ??= settings.Mqtt;
        string directory = Path.Combine(evidenceRoot, $"mission-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string snapshot = Path.Combine(directory, "config.json");
        await File.WriteAllBytesAsync(snapshot, settings.SourceBytes.ToArray());
        await File.WriteAllTextAsync(Path.Combine(directory, "effective-settings.json"), JsonSerializer.Serialize(new {
            configurationPath = Path.GetFullPath(configPath), settings.StepIntervalMs,
            mqtt = mqtt is null ? null : new { host = mqtt.Connection.Host, port = mqtt.Connection.Port, mqtt.TelemetryMs, mqtt.HeartbeatMs }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return new(directory, mqtt, navigator: new(settings.Start, settings.Waypoints, settings.Map),
            amrId: settings.AmrId, stepIntervalMs: settings.StepIntervalMs) { ConfigurationPath = Path.GetFullPath(configPath) };
    }

    public object Read()
    {
        lock (_gate)
        {
            var data = _received.Read();
            var navigation = _navigator.Read();
            if (_phase is not null) navigation = navigation with { Phase = _phase };
            return new { navigation, stepIntervalMs = StepIntervalMs,
                source = _mqtt is null ? "csharp-grid-navigation" : "mqtt-grid-control-receiver",
                position = _mqtt is null ? navigation.Current : ReceivedCell(data.Telemetry),
                configurationPath = ConfigurationPath,
                amrId = AmrId, cellSizeMeters = GridTelemetry.CellSizeMeters,
                receivedTelemetry = data.Telemetry, receivedHeartbeat = data.Heartbeat,
                receivedTrail = _receivedTrail.ToArray(),
                telemetryCount = data.TelemetryCount, heartbeatCount = data.HeartbeatCount,
                publishedCount = _published, rejectedCount = _rejected, lastPublishedTimestamp = _lastPublishedTimestamp,
                brokerState = _brokerState, failure = _failure,
                mqttEndpoint = _mqtt is null ? null : $"{_mqtt.Connection.Host}:{_mqtt.Connection.Port}",
                evidencePath = EvidencePath, evidenceDirectory = EvidenceDirectory, logs = _recent.ToArray() };
        }
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (_closing || !_execution.IsCompleted || !_navigator.Start()) return false;
            _received = new(AmrId);
            _receivedTrail.Clear();
            _failure = null;
            _published = _rejected = _stopReason = 0;
            _lastPublishedTimestamp = null;
            _resetRequested = _connected = false;
            if (_mqtt is not null)
            {
                _phase = "Connecting";
                _brokerState = "Connecting";
                _runCancellation?.Dispose();
                _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                _execution = Task.Run(() => ExecuteMqttAsync(_runCancellation.Token));
            }
            Record("GridStarted");
            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _navigator.Cancel();
            if (_mqtt is not null && !_execution.IsCompleted && _stopReason == 0)
            {
                _stopReason = 1;
                _phase = "Canceling";
                _runCancellation!.Cancel();
            }
            Record("GridCanceled");
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_mqtt is not null && !_execution.IsCompleted)
            {
                _resetRequested = true;
                Cancel();
                _phase = "Resetting";
            }
            else ResetCore();
        }
    }

    private void ResetCore()
    {
        _navigator.Reset();
        _received = new(AmrId);
        _receivedTrail.Clear();
        _phase = _failure = null;
        _lastPublishedTimestamp = null;
        _published = _rejected = 0;
        Record("GridReset");
    }

    public void SetObstacle(GridCell cell, bool blocked)
    {
        lock (_gate)
            if (_navigator.SetObstacle(cell, blocked)) Record("GridMapChanged");
    }

    private async Task RunLocalAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(StepIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
                lock (_gate)
                {
                    if (_closing) break;
                    if (_navigator.Step()) Record("GridMoved");
                }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private void Receive(object model)
    {
        lock (_gate)
        {
            if (model is TelemetryMessage t && (t.Id != AmrId || ReceivedCell(t) is null))
            {
                _rejected++;
                Record("GridTelemetryRejected");
                return;
            }
            if (_received.Apply(model))
            {
                if (model is TelemetryMessage telemetry && ReceivedCell(telemetry) is { } cell &&
                    (_receivedTrail.Count == 0 || _receivedTrail[^1] != cell)) _receivedTrail.Add(cell);
                Record("GridControlReceived");
            }
        }
    }

    private void Rejected()
    {
        lock (_gate) { _rejected++; Record("GridMessageRejected"); }
    }

    private void Fail(string reason)
    {
        lock (_gate)
        {
            if (_stopReason != 0) return;
            _failure = !_connected ? "ConnectionFailed:" + reason : reason;
            _stopReason = 2;
            _navigator.Cancel();
            _runCancellation!.Cancel();
            Record("GridMqttFailed");
        }
    }

    private async Task ExecuteMqttAsync(CancellationToken token)
    {
        IGridMqttConnection? connection = null;
        try
        {
            connection = _connectionFactory!();
            connection.Received += Receive;
            connection.Failed += Fail;
            connection.Rejected += Rejected;
            await connection.ConnectAsync(token);
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _connected = true;
                _brokerState = "Subscribed";
                _phase = _navigator.Read().Phase == "Completed" ? "WaitingForTelemetry" : null;
                Record("GridMqttConnected");
            }
            await Publish(false, true);
            await Publish(true, true);
            var elapsed = Stopwatch.StartNew();
            long nextStep = StepIntervalMs, nextTelemetry = _mqtt!.TelemetryMs, nextHeartbeat = _mqtt.HeartbeatMs;
            int lastMoves = 0;
            string lastPhase = "Ready";
            while (true)
            {
                token.ThrowIfCancellationRequested();
                GridSnapshot current;
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    if (elapsed.ElapsedMilliseconds >= nextStep)
                    {
                        if (_navigator.Step()) Record("GridMoved");
                        nextStep = elapsed.ElapsedMilliseconds + StepIntervalMs;
                    }
                    current = _navigator.Read();
                    // 계산 도착과 최종 좌표 수신 사이에도 외부에 완료를 노출하지 않는다.
                    if (current.Phase == "Completed") _phase = "WaitingForTelemetry";
                }
                if (current.Phase == "Completed") break;
                if (current.Moves != lastMoves || current.Phase != lastPhase || elapsed.ElapsedMilliseconds >= nextTelemetry)
                {
                    await Publish(false, false);
                    lastMoves = current.Moves;
                    lastPhase = current.Phase;
                    nextTelemetry = elapsed.ElapsedMilliseconds + _mqtt.TelemetryMs;
                }
                if (elapsed.ElapsedMilliseconds >= nextHeartbeat)
                {
                    await Publish(true, false);
                    nextHeartbeat = elapsed.ElapsedMilliseconds + _mqtt.HeartbeatMs;
                }
                await Task.Delay(20, token);
            }
            long finalStamp = await Publish(false, true);
            lock (_gate) _phase = "WaitingForTelemetry";
            var waiting = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    var final = GridTelemetry.Capture(AmrId, _navigator.Read(), true, StepIntervalMs);
                    if (_stopReason == 0 && _received.MatchesFinal(finalStamp, final.X, final.Y))
                    {
                        _stopReason = 3;
                        Record("GridFinalReceived");
                        break;
                    }
                }
                if (waiting.Elapsed >= TimeSpan.FromSeconds(3)) throw new TimeoutException("FinalTelemetryNotReceived");
                await Task.Delay(20, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Fail(exception is TimeoutException ? exception.Message : exception.GetType().Name); }
        finally
        {
            if (connection is not null)
            {
                connection.Received -= Receive;
                connection.Failed -= Fail;
                connection.Rejected -= Rejected;
                try { await connection.DisposeAsync(); }
                catch (Exception exception)
                {
                    lock (_gate) { _failure = "CleanupFailed:" + exception.GetType().Name; _stopReason = 2; }
                }
            }
            lock (_gate)
            {
                _connected = false;
                _brokerState = _failure is null ? "Closed" : "Failed";
                _phase = _stopReason == 3 ? "Completed" : _stopReason == 1 ? "Canceled" : "Failed";
                Record("GridMqttEnded");
                if (_resetRequested) { _resetRequested = false; ResetCore(); }
            }
        }

        async Task<long> Publish(bool heartbeat, bool stopped)
        {
            SimulationPublication publication;
            long stamp;
            lock (_gate)
            {
                stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), (_lastPublishedTimestamp ?? -1) + 1);
                publication = SimulationMessages.Create(GridTelemetry.Capture(AmrId, _navigator.Read(), stopped, StepIntervalMs), heartbeat, stamp);
            }
            await connection!.PublishAsync(publication, token);
            lock (_gate)
            {
                _published++;
                if (!heartbeat) _lastPublishedTimestamp = stamp;
                _log.WriteLine(JsonSerializer.Serialize(new { @event = "GridPublished", publication.Kind,
                    publication.Topic, payload = Encoding.UTF8.GetString(publication.Payload), delivery = "Unconfirmed" }, _json));
            }
            return stamp;
        }
    }

    private GridCell? ReceivedCell(TelemetryMessage? message) => GridTelemetry.ReceivedCell(message, _rows, _cols);

    private void Record(string name)
    {
        var state = _navigator.Read();
        _log.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, name, state, failure = _failure }, _json));
        _recent.Enqueue($"{name} Phase={_phase ?? state.Phase} Position=({state.Current.Row},{state.Current.Col}) Remaining={state.RemainingDistance?.ToString() ?? "none"} Moves={state.Moves} WaypointsPassed={state.NextWaypointIndex}/{state.Waypoints.Length} Replans={state.Replans} MapVersion={state.MapVersion}");
        while (_recent.Count > 80) _recent.Dequeue();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; Cancel(); }
        _shutdown.Cancel();
        try { await Task.WhenAll(_loop, _execution); }
        finally { _runCancellation?.Dispose(); _shutdown.Dispose(); _log.Dispose(); }
    }
}
