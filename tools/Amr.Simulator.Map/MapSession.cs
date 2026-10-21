using System.Globalization;
using System.Text;
using System.Text.Json;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using Amr.Communication.Models;

namespace Amr.Simulator.Map;

/// <summary>실제 Simulator 실행과 출력 수집을 소유한다. UI는 복사한 상태만 읽는다.</summary>
public sealed class MapSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _snapshotPath;
    private readonly SimulatorSettings _settings;
    private readonly Queue<string> _lines = new();
    private CancellationTokenSource? _cancellation;
    private Task _execution = Task.CompletedTask;
    private bool _closing;
    private string _phase = "Ready";
    private string? _runId;
    private Position2D _position;
    private string _amrState = "IDLE";
    private double _speed;
    private double _elapsed;
    private int _waypointsPassed;
    private long _sequence;
    private int? _exitCode;
    private string? _lastSnapshotLine;
    private string? _terminalEvent;
    private DateTimeOffset? _updatedAt;
    private readonly SimulatorMqttOptions? _mqtt;
    private MqttMapProjection _received;
    private string _brokerState = "NotStarted";
    private string? _receiveFailure;
    private int _rejectedCount;
    private long? _lastPublishedTimestamp;
    private int _stopReason; // 0=pending, 1=user cancel, 2=receive failure, 3=complete

    private MapSession(string snapshotPath, string evidenceDirectory, SimulatorSettings settings, SimulatorMqttOptions? mqtt)
    {
        _snapshotPath = snapshotPath;
        EvidenceDirectory = evidenceDirectory;
        _settings = settings;
        _position = settings.InitialPosition;
        _mqtt = mqtt;
        _received = new(settings.AmrId);
    }

    public string EvidenceDirectory { get; }

    public static async Task<MapSession> CreateAsync(string sourceConfig, string evidenceRoot, SimulatorMqttOptions? mqtt = null)
    {
        // 파일이 시연 중 변경돼도 지도 경로와 실행 입력은 같은 복사본을 사용한다.
        string directory = Path.Combine(evidenceRoot, $"map-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string snapshot = Path.Combine(directory, "config.json");
        await File.WriteAllBytesAsync(snapshot, await File.ReadAllBytesAsync(sourceConfig));
        var settings = await SimulatorSettingsLoader.LoadAsync(snapshot);
        return new MapSession(snapshot, directory, settings, mqtt);
    }

    public object Read()
    {
        lock (_gate)
        {
            var received = _received.Read();
            var telemetry = received.Telemetry;
            return new
            {
            phase = _phase, runId = _runId, sequence = _sequence,
            position = _mqtt is null ? _position : telemetry is null ? (Position2D?)null : new Position2D(telemetry.Position.X, telemetry.Position.Y),
            amrState = _mqtt is null ? _amrState : telemetry?.State,
            speed = _mqtt is null ? _speed : telemetry?.Speed,
            elapsedSeconds = _elapsed, waypointsPassed = _waypointsPassed,
            exitCode = _exitCode, updatedAt = _updatedAt,
            lastSnapshotLine = _lastSnapshotLine, logs = _lines.ToArray(),
            settings = _settings, evidenceDirectory = EvidenceDirectory,
            source = _mqtt is null ? "simulator-output" : "mqtt-control-receiver",
            brokerState = _brokerState, receiveFailure = _receiveFailure, rejectedCount = _rejectedCount,
            receivedTelemetry = telemetry, receivedHeartbeat = received.Heartbeat,
            telemetryCount = received.TelemetryCount, heartbeatCount = received.HeartbeatCount,
            ignoredCount = received.IgnoredCount, lastPublishedTimestamp = _lastPublishedTimestamp,
            mqttEndpoint = _mqtt is null ? null : $"{_mqtt.Connection.Host}:{_mqtt.Connection.Port}"
            };
        };
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (_closing || !_execution.IsCompleted) return false;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _runId = $"run-{DateTimeOffset.Now:HHmmss}-{Guid.NewGuid():N}";
            string runId = _runId;
            _phase = "Running";
            _position = _settings.InitialPosition;
            _amrState = "IDLE";
            _speed = _elapsed = 0;
            _waypointsPassed = 0;
            _exitCode = null;
            _lastSnapshotLine = _terminalEvent = null;
            _received = new(_settings.AmrId);
            _brokerState = "NotStarted";
            _receiveFailure = null;
            _rejectedCount = _stopReason = 0;
            _lastPublishedTimestamp = null;
            _lines.Clear();
            _sequence++;
            _updatedAt = DateTimeOffset.UtcNow;
            _execution = Task.Run(() => ExecuteAsync(runId, token));
            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_execution.IsCompleted) return;
            if (_stopReason != 0) return;
            _stopReason = 1;
            _phase = "Canceling";
            _cancellation!.Cancel();
        }
    }

    private async Task ExecuteAsync(string runId, CancellationToken token)
    {
        int code = 1;
        try
        {
            using var stdout = new StreamWriter(Path.Combine(EvidenceDirectory, runId + ".stdout.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
            using var stderr = new StreamWriter(Path.Combine(EvidenceDirectory, runId + ".stderr.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
            using var output = new CaptureWriter(line => { stdout.WriteLine(line); Observe(line); });
            using var error = new CaptureWriter(line => { stderr.WriteLine(line); Observe(line); });
            // 콘솔과 같은 구현·설정·실제 시간의 시계를 사용한다.
            code = _mqtt is null
                ? await SimulatorApplication.RunAsync(["--config", _snapshotPath], output, error, token)
                : await ExecuteMqttAsync(runId, output, error, token);
        }
        catch (Exception exception)
        {
            Observe($"MapExecutionFailed Reason={exception.GetType().Name}");
        }
        lock (_gate)
        {
            _exitCode = code;
            _phase = _mqtt is not null ? (_stopReason == 1 && code == 0 ? "Canceled" : _stopReason == 3 && code == 0 ? "Completed" : "Failed")
                : code == 0 && _terminalEvent == "SimulationCompleted" ? "Completed"
                : code == 0 && _terminalEvent is "SimulationCanceled" or "InitializationCanceled" ? "Canceled" : "Failed";
            _sequence++;
            _updatedAt = DateTimeOffset.UtcNow;
        }
        try
        {
            await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory, runId + ".result.json"),
                JsonSerializer.Serialize(Read(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Observe($"EvidenceSaveFailed Reason={exception.GetType().Name}");
            lock (_gate) _phase = "Failed";
        }
    }

    private void Observe(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            if (_lines.Count > 80) _lines.Dequeue();
            _sequence++;
            _updatedAt = DateTimeOffset.UtcNow;
            string name = line.Split(' ', 2)[0];
            if (name is "SimulationCompleted" or "SimulationCanceled" or "InitializationCanceled") _terminalEvent = name;
            if (_mqtt is not null)
            {
                if (name == "MqttSimulationFailed" && line.Contains("Reason=ConnectionLost", StringComparison.Ordinal))
                    FailReceiver("BrokerDisconnected");
                if (name == "MqttSimulationFailed" && line.Contains("Reason=ConnectionLost", StringComparison.Ordinal))
                    FailReceiver("BrokerDisconnected");
                if (name == "MqttSimulationCompleted") _terminalEvent = name;
                if (name == "TelemetryPublished")
                {
                    int start = line.IndexOf("Payload=", StringComparison.Ordinal) + 8;
                    int end = line.LastIndexOf(" Delivery=", StringComparison.Ordinal);
                    using var payload = JsonDocument.Parse(line[start..end]);
                    _lastPublishedTimestamp = payload.RootElement.GetProperty("ts").GetInt64();
                }
                // stdout는 실행 경과·종료 근거만 제공한다. 지도 위치·속도·State는 절대 갱신하지 않는다.
                if (name is "SimulationStarted" or "AmrMoved" or "SimulationCompleted" or "SimulationCanceled")
                {
                    string elapsed = line.Split("ElapsedSeconds=", 2)[1];
                    _elapsed = double.Parse(elapsed, CultureInfo.InvariantCulture);
                }
                return;
            }
            var fields = line.Split(' ').Skip(1).Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]);
            if (name == "WaypointReached") _waypointsPassed = int.Parse(fields["Index"], CultureInfo.InvariantCulture) + 1;
            if (name is not ("AmrInitialized" or "SimulationStarted" or "AmrMoved" or "SimulationCompleted" or "SimulationCanceled")) return;
            _position = new Position2D(Number(fields, "X"), Number(fields, "Y"));
            _speed = Number(fields, "CurrentSpeedMps");
            _amrState = fields["State"];
            _elapsed = fields.ContainsKey("ElapsedSeconds") ? Number(fields, "ElapsedSeconds") : 0;
            _lastSnapshotLine = line;
            if (name == "SimulationCompleted") _waypointsPassed = _settings.Waypoints.Count;
        }
    }

    private static double Number(Dictionary<string, string> fields, string name) => double.Parse(fields[name], CultureInfo.InvariantCulture);

    private async Task<int> ExecuteMqttAsync(string runId, TextWriter output, TextWriter error, CancellationToken token)
    {
        using var receiveLog = new StreamWriter(Path.Combine(EvidenceDirectory, runId + ".received.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
        await using var receiver = new MqttMapReceiver(_mqtt!.Connection.Host, _mqtt.Connection.Port, _settings.AmrId,
            message =>
            {
                lock (_gate)
                {
                    if (_received.Apply(message.Model))
                    {
                        _lastSnapshotLine = "Received " + JsonSerializer.Serialize(message);
                        _lines.Enqueue(_lastSnapshotLine);
                        if (_lines.Count > 80) _lines.Dequeue();
                        _sequence++;
                        _updatedAt = message.ReceivedAtUtc;
                    }
                }
            }, () => { lock (_gate) _rejectedCount++; },
            state => { lock (_gate) _brokerState = state; },
            FailReceiver, receiveLog);
        try
        {
            await receiver.StartAsync(token);
            int code = await SimulatorApplication.RunAsync(["--config", _snapshotPath, "--mqtt",
                "--host", _mqtt.Connection.Host, "--port", _mqtt.Connection.Port.ToString(CultureInfo.InvariantCulture),
                "--telemetry-ms", _mqtt.TelemetryMs.ToString(CultureInfo.InvariantCulture),
                "--heartbeat-ms", _mqtt.HeartbeatMs.ToString(CultureInfo.InvariantCulture)], output, error, token);
            if (code != 0) return code;
            if (token.IsCancellationRequested) return _stopReason == 1 ? 0 : 1;
            lock (_gate) _phase = "WaitingForTelemetry";
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(3))
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    var target = _settings.Waypoints[^1];
                    if (_stopReason == 0 && _terminalEvent == "MqttSimulationCompleted" && _lastPublishedTimestamp is long stamp &&
                        _received.MatchesFinal(stamp, target.X, target.Y))
                    {
                        _stopReason = 3;
                        _waypointsPassed = _settings.Waypoints.Count;
                        return 0;
                    }
                }
                await Task.Delay(20, token);
            }
            FailReceiver("FinalTelemetryNotReceived");
            return 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return _stopReason == 1 ? 0 : 1; }
        catch (Exception exception)
        {
            FailReceiver("ReceiverOrExecution" + exception.GetType().Name);
            throw;
        }
    }

    private void FailReceiver(string reason)
    {
        lock (_gate)
        {
            if (_stopReason != 0) return;
            _stopReason = 2;
            _receiveFailure = reason;
            _cancellation!.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task execution;
        lock (_gate)
        {
            _closing = true;
            if (_stopReason == 0) _stopReason = 1;
            _cancellation?.Cancel();
            execution = _execution;
        }
        await execution;
        _cancellation?.Dispose();
    }

    private sealed class CaptureWriter(Action<string> capture) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => capture(value ?? "");
        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }
    }
}
