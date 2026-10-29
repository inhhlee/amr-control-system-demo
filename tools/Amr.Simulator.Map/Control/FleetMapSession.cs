using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Core;
using Amr.Simulator.Navigation;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Map.Control;

/// <summary>두 Simulator와 정식 통신/Core를 조립한다. 생존 판정·미션 정책은 각 모듈에 위임한다.</summary>
public sealed class FleetMapSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, Vehicle> _vehicles;
    private readonly ControlCommunicationSession _receiver;
    private readonly AmrDomainManager _domain;
    private readonly FleetCommunicationBridge _bridge;
    private readonly TextWriter _log;
    private readonly Queue<string> _recent = new();
    private readonly Func<ReceivedMessage, CancellationToken, Task>? _beforeDelivery;
    private Task? _run;
    private Task? _dispose;
    private string _broker = "NotStarted";
    private string? _failure;

    public FleetMapSession(FleetMissionSettings settings, string evidenceDirectory,
        Func<string, ISimulationTransport>? transportFactory = null,
        Func<ReceivedMessage, CancellationToken, Task>? beforeDelivery = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
        Directory.CreateDirectory(EvidenceDirectory);
        _log = TextWriter.Synchronized(new StreamWriter(Path.Combine(EvidenceDirectory, "fleet-events.jsonl"), append: false) { AutoFlush = true });
        _beforeDelivery = beforeDelivery;
        _vehicles = settings.Missions.ToDictionary(x => x.AmrId, settings => new Vehicle(settings,
            new GridSimulation(settings.AmrId, new GridNavigator(settings.Start, settings.Waypoints, settings.Map),
                settings.StepIntervalMs, transportFactory is null ? null : () => transportFactory(settings.AmrId))), StringComparer.Ordinal);
        _domain = new(_vehicles.Keys);
        _bridge = new(_domain);
        // 각 Client는 서로 다른 MQTT ClientId를 소유한다. A1 송신 설정 객체를 공유하면 Broker가 수신 연결을 교체한다.
        var receiverSettings = settings.Missions[0].Mqtt!.Connection;
        _receiver = new(new MqttConnectionSettings(receiverSettings.Host, receiverSettings.Port, (int)receiverSettings.ConnectTimeout.TotalSeconds), _vehicles.Keys.ToArray(), ReceiveAsync,
            entry => Record("Communication", entry));
        _domain.BeginSession(_receiver.SessionId);
        _receiver.ObservationChanged += Observe;
        foreach (var vehicle in _vehicles.Values)
        {
            File.WriteAllBytes(Path.Combine(EvidenceDirectory, vehicle.Settings.AmrId + ".config.json"), vehicle.Settings.SourceBytes.ToArray());
            vehicle.Simulation.Published += (publication, stamp) => Record("Published", new { vehicle.Settings.AmrId, publication.Kind, publication.Topic, stamp });
            vehicle.Simulation.MissionCompleted += completion => {
                lock (_gate)
                {
                    if (completion.Generation != vehicle.Simulation.MissionGeneration) return;
                    vehicle.Final = completion;
                    CheckFinal(vehicle);
                }
            };
        }
        File.WriteAllBytes(Path.Combine(EvidenceDirectory, "fleet.config.json"), settings.SourceBytes);
    }

    public string EvidenceDirectory { get; }
    public Guid SessionId => _receiver.SessionId;
    public AmrDomainSnapshot Domain => _domain.Read();

    public void Connect()
    {
        lock (_gate)
        {
            if (_run is not null || _dispose is not null) throw new InvalidOperationException("Fleet session is single use.");
            _run = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        var senders = new List<Task>();
        try
        {
            await _receiver.StartAsync(_shutdown.Token);
            lock (_gate) { _shutdown.Token.ThrowIfCancellationRequested(); _broker = "Subscribed"; }
            foreach (var vehicle in _vehicles.Values) senders.Add(Task.Run(() => RunSenderAsync(vehicle)));
            await Task.Delay(Timeout.Infinite, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error) { lock (_gate) { _failure = _receiver.FailureReason ?? error.GetType().Name; _broker = "Failed"; } }
        finally
        {
            _shutdown.Cancel();
            await Task.WhenAll(senders);
            try { await _receiver.StopAsync(); }
            catch (Exception error) { lock (_gate) { _failure = "CleanupFailed:" + error.GetType().Name; _broker = "Failed"; } }
            lock (_gate) if (_broker != "Failed") _broker = "Closed";
        }
    }

    private async Task RunSenderAsync(Vehicle vehicle)
    {
        try { await vehicle.Simulation.RunSessionAsync(vehicle.Settings.Mqtt!, _shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate) vehicle.Failure = "AmrSender:" + error.GetType().Name;
            Record("SenderEnded", new { vehicle.Settings.AmrId, vehicle.Failure });
            // 해당 송신만 종료. 다른 AMR과 관제 수신/Heartbeat 감시는 유지한다.
        }
    }

    private void Observe(AmrCommunicationObservation observation)
    {
        _bridge.Observe(observation);
        Record("DomainObserved", new { observation, domain = _domain.Read() });
        if (observation.FailureReason is not null)
        {
            lock (_gate) { _failure = observation.FailureReason; _broker = "Failed"; }
            _shutdown.Cancel();
        }
    }

    private async Task ReceiveAsync(ReceivedMessage message, CancellationToken token)
    {
        if (_beforeDelivery is not null) await _beforeDelivery(message, token);
        token.ThrowIfCancellationRequested();
        var vehicle = _vehicles[message.AmrId];
        if (message.Model is TelemetryMessage telemetry)
        {
            if (GridMapCoordinates.ReceivedCell(telemetry, vehicle.Settings.Map.GetLength(0), vehicle.Settings.Map.GetLength(1)) is null)
            { Record("GridTelemetryRejected", message.AmrId); return; }
        }
        lock (_gate)
        {
            if (message.Model is TelemetryMessage pointData && !_bridge.Receive(_receiver.SessionId, pointData)) return;
            if (vehicle.Received.Apply(message.Model))
            {
                if (message.Model is TelemetryMessage point && GridMapCoordinates.ReceivedCell(point, vehicle.Settings.Map.GetLength(0), vehicle.Settings.Map.GetLength(1)) is { } cell
                    && (vehicle.Trail.Count == 0 || vehicle.Trail[^1] != cell)) vehicle.Trail.Add(cell);
                CheckFinal(vehicle);
            }
        }
        Record("Delivered", new { message.AmrId, message.Kind, message.Model });
    }

    public bool Start(string amrId)
    {
        lock (_gate)
        {
            if (_dispose is not null || _broker != "Subscribed" || !_vehicles.TryGetValue(amrId, out var vehicle)) return false;
            var status = _domain.Read().Statuses.Single(x => x.AmrId == amrId);
            if (status.Mission.State == AmrMissionState.OnHold) return _domain.ResumeMission(amrId);
            if (status.Communication != AmrCommunicationState.CONNECTED || !vehicle.Simulation.Start()) return false;
            vehicle.MissionId = $"{amrId}:{vehicle.Simulation.MissionGeneration}";
            vehicle.Final = null;
            vehicle.FinalConfirmed = false;
            vehicle.Trail.Clear();
            if (!_domain.StartMission(amrId, vehicle.MissionId)) { vehicle.Simulation.Cancel(); return false; }
            Record("MissionStarted", new { amrId, vehicle.MissionId });
            return true;
        }
    }

    public bool Cancel(string amrId)
    {
        lock (_gate)
        {
            if (_dispose is not null || _broker != "Subscribed" || !_vehicles.TryGetValue(amrId, out var vehicle)) return false;
            vehicle.Simulation.Cancel();
            _domain.CancelMission(amrId);
            vehicle.Final = null;
            return true;
        }
    }

    public bool Reset(string amrId)
    {
        lock (_gate)
        {
            if (_dispose is not null || _broker != "Subscribed" || !_vehicles.TryGetValue(amrId, out var vehicle)) return false;
            vehicle.Simulation.Reset();
            _domain.CancelMission(amrId);
            vehicle.Final = null;
            vehicle.FinalConfirmed = false;
            vehicle.Trail.Clear();
            return true;
        }
    }

    public bool SetObstacle(string amrId, GridCell cell, bool blocked)
    {
        lock (_gate) return _dispose is null && _broker == "Subscribed" && _vehicles.TryGetValue(amrId, out var vehicle) && vehicle.Simulation.SetObstacle(cell, blocked);
    }

    private void CheckFinal(Vehicle vehicle)
    {
        if (!vehicle.FinalConfirmed && vehicle.Final is { } final && vehicle.MissionId is { } mission &&
            vehicle.Received.MatchesFinal(final.Timestamp, final.X, final.Y))
        {
            vehicle.FinalConfirmed = true;
            _domain.CompleteMission(vehicle.Settings.AmrId, mission);
        }
    }

    public object Read()
    {
        lock (_gate)
        {
            var domain = _domain.Read();
            object View(Vehicle vehicle)
            {
                var data = vehicle.Received.Read();
                return new { navigation = vehicle.Simulation.Read(), source = "mqtt-grid-control-receiver",
                    position = GridMapCoordinates.ReceivedCell(data.Telemetry, vehicle.Settings.Map.GetLength(0), vehicle.Settings.Map.GetLength(1)),
                    amrId = vehicle.Settings.AmrId, receivedTelemetry = data.Telemetry, receivedHeartbeat = data.Heartbeat,
                    receivedTrail = vehicle.Trail.ToArray(), telemetryCount = data.TelemetryCount, heartbeatCount = data.HeartbeatCount,
                    rejectedCount = 0, lastPublishedTimestamp = (long?)null, brokerState = _broker, failure = vehicle.Failure ?? _failure,
                    mqttEndpoint = $"{vehicle.Settings.Mqtt!.Connection.Host}:{vehicle.Settings.Mqtt.Connection.Port}",
                    logs = _recent.ToArray(), heartbeatMonitor = _receiver.Heartbeats.Single(x => x.AmrId == vehicle.Settings.AmrId),
                    missionFinalReceived = vehicle.FinalConfirmed };
            }
            object[] fleet = _vehicles.Values.Select(View).ToArray();
            // 기존 단일 화면 계약을 최상위에 유지하고 두 AMR/업무 조회를 확장 필드로 제공한다.
            var root = JsonSerializer.SerializeToElement(fleet[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var result = root.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value);
            result["fleet"] = fleet;
            result["domain"] = domain;
            result["evidenceDirectory"] = EvidenceDirectory;
            return result;
        }
    }

    private void Record(string name, object? value)
    {
        _log.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, name, value }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        lock (_gate) { if (_recent.Count == 5) _recent.Dequeue(); _recent.Enqueue(name); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= Task.Run(DisposeCoreAsync));
    }
    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();
        try { if (_run is not null) await _run; else await _receiver.StopAsync(); }
        finally
        {
            _receiver.ObservationChanged -= Observe;
            _log.Dispose();
            _shutdown.Dispose();
        }
    }

    private sealed class Vehicle(GridMissionSettings settings, GridSimulation simulation)
    {
        public GridMissionSettings Settings = settings;
        public GridSimulation Simulation = simulation;
        public MqttMapProjection Received = new(settings.AmrId);
        public List<GridCell> Trail = [];
        public GridSimulationCompletion? Final;
        public bool FinalConfirmed;
        public string? MissionId, Failure;
    }
}
