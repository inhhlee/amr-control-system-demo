using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Simulator.Telemetry;
using Amr.Simulator.Navigation;

namespace Amr.Simulator.Map.Control;

/// <summary>지도 명령을 Simulator에 전달하고 관제 수신·표시·최종 수신 확인을 소유한다.</summary>
public sealed class GridMapSession : IAsyncDisposable
{
    public const int DefaultStepIntervalMs = GridSimulation.DefaultStepIntervalMs;
    public int StepIntervalMs { get; }
    private readonly int _rows, _cols;
    public string? ConfigurationPath { get; private set; }
    public string AmrId { get; } = "AMR_GRID_" + Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private readonly GridSimulation _simulation;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TextWriter _log;
    private readonly Queue<string> _recent = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly SimulatorMqttOptions? _mqtt;
    private readonly Func<IGridControlConnection>? _connectionFactory;
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

    public GridMapSession(string evidenceDirectory, SimulatorMqttOptions? mqtt = null,
        Func<IGridControlConnection>? connectionFactory = null, GridNavigator? navigator = null, string? amrId = null,
        int stepIntervalMs = DefaultStepIntervalMs, Func<ISimulationTransport>? transportFactory = null)
    {
        if (stepIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepIntervalMs));
        StepIntervalMs = stepIntervalMs;
        if (amrId is not null) AmrId = amrId;
        _simulation = new(AmrId, navigator, stepIntervalMs, transportFactory);
        _simulation.Moved += OnMoved;
        _simulation.Connected += OnConnected;
        _simulation.Published += OnPublished;
        var map = _simulation.Read().Map;
        _rows = map.Length;
        _cols = map[0].Length;
        _mqtt = mqtt;
        _received = new(AmrId);
        if (mqtt is not null && (mqtt.TelemetryMs <= 0 || mqtt.HeartbeatMs <= 0))
            throw new ArgumentException("송신 주기는 양수여야 합니다.", nameof(mqtt));
        EvidencePath = Path.Combine(evidenceDirectory, "grid-navigation.jsonl");
        _log = TextWriter.Synchronized(new StreamWriter(EvidencePath, append: false) { AutoFlush = true });
        _connectionFactory = connectionFactory ?? (mqtt is null ? null : () => new GridControlConnection(mqtt, AmrId, _log));
        Record("GridReady");
    }

    public string EvidencePath { get; }
    public string EvidenceDirectory => Path.GetDirectoryName(EvidencePath)!;

    public static async Task<GridMapSession> CreateAsync(string configPath, string evidenceRoot, SimulatorMqttOptions? mqtt,
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
            var navigation = _simulation.Read();
            if (_phase is not null) navigation = navigation with { Phase = _phase };
            else if (_mqtt is not null && !_execution.IsCompleted && navigation.Phase == "Completed")
                navigation = navigation with { Phase = "WaitingForTelemetry" };
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
            if (_closing || !_execution.IsCompleted || !_simulation.Start()) return false;
            _received = new(AmrId);
            _receivedTrail.Clear();
            _failure = null;
            _published = _rejected = _stopReason = 0;
            _lastPublishedTimestamp = null;
            _resetRequested = _connected = false;
            _phase = _mqtt is null ? null : "Connecting";
            _brokerState = _mqtt is null ? "NotStarted" : "Connecting";
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _execution = Task.Run(() => ExecuteAsync(_runCancellation.Token));
            Record("GridStarted");
            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _simulation.Cancel();
            if (!_execution.IsCompleted && _stopReason == 0)
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
            if (!_execution.IsCompleted)
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
        _simulation.Reset();
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
            if (_simulation.SetObstacle(cell, blocked)) Record("GridMapChanged");
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
            _simulation.Cancel();
            _runCancellation!.Cancel();
            Record("GridMqttFailed");
        }
    }

    private void OnMoved() { lock (_gate) Record("GridMoved"); }

    private void OnConnected()
    {
        lock (_gate)
        {
            _connected = true;
            _brokerState = "Subscribed";
            _phase = null;
            Record("GridMqttConnected");
        }
    }

    private void OnPublished(SimulationPublication publication, long stamp)
    {
        lock (_gate)
        {
            _published++;
            if (publication.Kind == "Telemetry") _lastPublishedTimestamp = stamp;
            _log.WriteLine(JsonSerializer.Serialize(new { @event = "GridPublished", publication.Kind,
                publication.Topic, payload = Encoding.UTF8.GetString(publication.Payload), delivery = "Unconfirmed" }, _json));
        }
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        IGridControlConnection? connection = null;
        try
        {
            if (_mqtt is not null)
            {
                connection = _connectionFactory!();
                connection.Received += Receive;
                connection.Failed += Fail;
                connection.Rejected += Rejected;
                // 관제 SUBACK 뒤에 Simulator의 송신 Client와 이동을 시작한다.
                await connection.ConnectAsync(token);
                token.ThrowIfCancellationRequested();
            }
            var final = await _simulation.RunAsync(_mqtt, token);
            if (final is not null)
            {
                lock (_gate) _phase = "WaitingForTelemetry";
                var waiting = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_stopReason == 0 && _received.MatchesFinal(final.Timestamp, final.X, final.Y))
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
            else lock (_gate) { if (_stopReason == 0) _stopReason = 3; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Fail(exception is TimeoutException || exception.Message == "SenderDisconnected"
                ? exception.Message : exception.GetType().Name);
        }
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
                _brokerState = _mqtt is null ? "NotStarted" : _failure is null ? "Closed" : "Failed";
                _phase = _stopReason == 3 ? "Completed" : _stopReason == 1 ? "Canceled" : "Failed";
                Record("GridMqttEnded");
                if (_resetRequested) { _resetRequested = false; ResetCore(); }
            }
        }
    }
    private GridCell? ReceivedCell(TelemetryMessage? message) => GridMapCoordinates.ReceivedCell(message, _rows, _cols);

    private void Record(string name)
    {
        var state = _simulation.Read();
        _log.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, name, state, failure = _failure }, _json));
        _recent.Enqueue($"{name} Phase={_phase ?? state.Phase} Position=({state.Current.Row},{state.Current.Col}) Remaining={state.RemainingDistance?.ToString() ?? "none"} Moves={state.Moves} WaypointsPassed={state.NextWaypointIndex}/{state.Waypoints.Length} Replans={state.Replans} MapVersion={state.MapVersion}");
        while (_recent.Count > 80) _recent.Dequeue();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; Cancel(); }
        _shutdown.Cancel();
        try { await _execution; }
        finally
        {
            _simulation.Moved -= OnMoved;
            _simulation.Connected -= OnConnected;
            _simulation.Published -= OnPublished;
            _runCancellation?.Dispose(); _shutdown.Dispose(); _log.Dispose();
        }
    }
}
