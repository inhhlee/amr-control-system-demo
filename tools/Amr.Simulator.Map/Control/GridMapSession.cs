using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Simulator.Telemetry;
using Amr.Simulator.Navigation;

namespace Amr.Simulator.Map.Control;

// 호스트가 관제/AMR 통신 세션을 소유한다. 출발/도착/이동 중단은 세션 종료가 아니다.
public sealed class GridMapSession : IAsyncDisposable
{
    public const int DefaultStepIntervalMs = GridSimulation.DefaultStepIntervalMs;
    private readonly object _gate = new();
    private readonly GridSimulation _simulation;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TextWriter _log;
    private readonly Queue<string> _recent = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly SimulatorMqttOptions? _mqtt;
    private readonly Func<IGridControlConnection>? _connectionFactory;
    private readonly int _rows, _cols;
    private MqttMapProjection _received;
    private readonly List<GridCell> _receivedTrail = [];
    private Task _execution = Task.CompletedTask;
    private CancellationTokenSource? _runCancellation;
    private IGridControlConnection? _control;
    private HeartbeatObservation? _lastHeartbeat;
    private bool _closing, _connected, _pendingStart, _senderAvailable, _finalConfirmed;
    private string? _phase, _failure, _lastHeartbeatState;
    private string _brokerState = "NotStarted";
    private int _published, _rejected, _stopReason;
    private long? _lastPublishedTimestamp;
    private GridSimulationCompletion? _missionFinal;
    private long _finalWaitingStarted;
    private Task? _disposeTask;
    private bool _localResetPending;

    public GridMapSession(string evidenceDirectory, SimulatorMqttOptions? mqtt = null,
        Func<IGridControlConnection>? connectionFactory = null, GridNavigator? navigator = null, string? amrId = null,
        int stepIntervalMs = DefaultStepIntervalMs, Func<ISimulationTransport>? transportFactory = null,
        HeartbeatMonitorOptions? heartbeatOptions = null)
    {
        if (stepIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepIntervalMs));
        StepIntervalMs = stepIntervalMs;
        AmrId = amrId ?? "AMR_GRID_" + Guid.NewGuid().ToString("N");
        _simulation = new(AmrId, navigator, stepIntervalMs, transportFactory);
        _simulation.Moved += OnMoved; _simulation.Connected += OnConnected;
        _simulation.Published += OnPublished; _simulation.MissionCompleted += OnMissionCompleted;
        var map = _simulation.Read().Map; _rows = map.Length; _cols = map[0].Length;
        _mqtt = mqtt; _received = new(AmrId);
        if (mqtt is not null && (mqtt.TelemetryMs <= 0 || mqtt.HeartbeatMs <= 0)) throw new ArgumentException("송신 주기는 양수여야 합니다.", nameof(mqtt));
        EvidencePath = Path.Combine(evidenceDirectory, "grid-navigation.jsonl");
        _log = TextWriter.Synchronized(new StreamWriter(EvidencePath, false) { AutoFlush = true });
        _connectionFactory = connectionFactory ?? (mqtt is null ? null : () => new GridControlConnection(mqtt, AmrId, _log, heartbeatOptions));
        Record("GridReady");
    }
    public int StepIntervalMs { get; }
    public string AmrId { get; }
    public string? ConfigurationPath { get; private set; }
    public string EvidencePath { get; }
    public string EvidenceDirectory => Path.GetDirectoryName(EvidencePath)!;

    public static async Task<GridMapSession> CreateAsync(string configPath, string evidenceRoot, SimulatorMqttOptions? mqtt, GridMissionSettings? settings = null)
    {
        settings ??= await GridMissionSettings.LoadAsync(configPath); mqtt ??= settings.Mqtt;
        string directory = Path.Combine(evidenceRoot, $"mission-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "config.json"), settings.SourceBytes.ToArray());
        await File.WriteAllTextAsync(Path.Combine(directory, "effective-settings.json"), JsonSerializer.Serialize(new {
            configurationPath = Path.GetFullPath(configPath), settings.StepIntervalMs,
            mqtt = mqtt is null ? null : new { host=mqtt.Connection.Host,port=mqtt.Connection.Port,mqtt.TelemetryMs,mqtt.HeartbeatMs },
            heartbeatMonitor = new {timeoutMs=5000,checkIntervalMs=500}
        },new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
        var session = new GridMapSession(directory,mqtt,navigator:new(settings.Start,settings.Waypoints,settings.Map),
            amrId:settings.AmrId,stepIntervalMs:settings.StepIntervalMs){ConfigurationPath=Path.GetFullPath(configPath)};
        session.Connect();
        return session;
    }

    public object Read()
    {
        lock (_gate)
        {
            var data = _received.Read(); var navigation = _simulation.Read();
            if (_phase is not null) navigation = navigation with {Phase=_phase};
            else if (_mqtt is not null && navigation.Phase == "Completed") navigation = navigation with {Phase="WaitingForTelemetry"};
            return new { navigation,stepIntervalMs=StepIntervalMs,source=_mqtt is null?"csharp-grid-navigation":"mqtt-grid-control-receiver",
                position=_mqtt is null?navigation.Current:ReceivedCell(data.Telemetry),configurationPath=ConfigurationPath,amrId=AmrId,cellSizeMeters=GridTelemetry.CellSizeMeters,
                receivedTelemetry=data.Telemetry,receivedHeartbeat=data.Heartbeat,receivedTrail=_receivedTrail.ToArray(),
                telemetryCount=data.TelemetryCount,heartbeatCount=data.HeartbeatCount,publishedCount=_published,rejectedCount=_rejected,
                lastPublishedTimestamp=_lastPublishedTimestamp,missionFinalTelemetry=_missionFinal,missionFinalReceived=_finalConfirmed,
                heartbeatMonitor=_control?.Heartbeat??_lastHeartbeat,brokerState=_brokerState,failure=_failure,
                mqttEndpoint=_mqtt is null?null:$"{_mqtt.Connection.Host}:{_mqtt.Connection.Port}",evidencePath=EvidencePath,evidenceDirectory=EvidenceDirectory,logs=_recent.ToArray() };
        }
    }
    public void Connect()
    {
        lock (_gate)
        {
            if (_mqtt is null || _closing || !_execution.IsCompleted) return;
            _runCancellation?.Dispose(); _runCancellation=CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _stopReason=0;_failure=null;_connected=_senderAvailable=false;_phase="Connecting";_brokerState="Connecting";
            _execution=Task.Run(()=>ExecuteAsync(_runCancellation.Token));
        }
    }
    public bool Start()
    {
        lock (_gate)
        {
            if (_closing) return false;
            if (_mqtt is null)
            {
                if (!_execution.IsCompleted || !_simulation.Start()) return false;
                _phase=null; _execution=Task.Run(RunLocalAsync);
                return true;
            }
            if (_execution.IsCompleted) { _pendingStart=true; Connect(); Record("GridStartRequested"); return true; }
            if (!_senderAvailable || _phase is not ("Ready" or "Completed" or "Canceled") || !_simulation.Start()) return false;
            _missionFinal=null;_finalConfirmed=false;_phase=null;_failure=null;_receivedTrail.Clear();
            // Ready에서 이미 받은 출발 위치를 남긴다. 계산 좌표로 궤적을 채우지 않는다.
            if(ReceivedCell(_received.Read().Telemetry) is {} cell && cell==_simulation.Read().Start)_receivedTrail.Add(cell);
            Record("GridStarted"); return true;
        }
    }
    public void Cancel()
    {
        lock (_gate)
        {
            if (_pendingStart || _simulation.Read().Phase is "Running" or "Waiting")
            {
                _pendingStart=false;_simulation.Cancel();_missionFinal=null;_finalConfirmed=false;_phase="Canceled";
                Record("GridCanceled");
            }
        }
    }
    public void Reset()
    {
        lock (_gate)
        {
            if (_mqtt is null && !_execution.IsCompleted)
            {
                _localResetPending=true;_simulation.Cancel();_phase="Resetting";return;
            }
            _pendingStart=false;_simulation.Reset();_missionFinal=null;_finalConfirmed=false;_receivedTrail.Clear();
            _phase=_senderAvailable||_mqtt is null?"Ready":_phase;
            // 마지막 수신값을 설정값으로 덮지 않는다. 다음 실제 Telemetry가 초기 위치를 갱신한다.
            Record("GridReset");
        }
    }
    public void SetObstacle(GridCell cell,bool blocked) { lock(_gate) if(_simulation.SetObstacle(cell,blocked))Record("GridMapChanged"); }
    private void Receive(object model)
    {
        lock(_gate)
        {
            if(model is TelemetryMessage t &&(t.Id!=AmrId||ReceivedCell(t)is null)){_rejected++;Record("GridTelemetryRejected");return;}
            if(_received.Apply(model))
            {
                if(model is TelemetryMessage telemetry && ReceivedCell(telemetry)is {} cell &&(_receivedTrail.Count==0||_receivedTrail[^1]!=cell))_receivedTrail.Add(cell);
                CheckFinal(); Record("GridControlReceived");
            }
        }
    }
    private void CheckFinal()
    {
        if(!_finalConfirmed&&_missionFinal is {} final&&_received.MatchesFinal(final.Timestamp,final.X,final.Y))
        {_finalConfirmed=true;_phase="Completed";Record("GridFinalReceived");}
    }
    private void Rejected(){lock(_gate){_rejected++;Record("GridMessageRejected");}}
    private void Fail(string reason)
    {
        lock(_gate)
        {
            if(_stopReason!=0)return;
            _failure=!_connected?"ConnectionFailed:"+reason:reason;_stopReason=2;
            _brokerState=reason=="BrokerDisconnected"||!_connected?"Failed":"ReceiveFailed";
            _simulation.Cancel();_runCancellation!.Cancel();Record("GridMqttFailed");
        }
    }
    private void OnMoved(){lock(_gate)Record("GridMoved");}
    private void OnConnected()
    {
        lock(_gate)
        {
            _senderAvailable=true;
            if(_pendingStart){_pendingStart=false;_simulation.Start();_missionFinal=null;_finalConfirmed=false;_phase=null;}
            else if(_phase!="Canceled")_phase="Ready";
            Record("GridMqttConnected");
        }
    }
    private void OnMissionCompleted(GridSimulationCompletion completion)
    {
        lock(_gate)
        {
            // 발행 대기 중 초기화/새 임무가 시작됐다면 이전 완료 알림을 채택하지 않는다.
            if(completion.Generation!=_simulation.MissionGeneration || _simulation.Read().Phase!="Completed")return;
            _missionFinal=completion;_finalConfirmed=false;_finalWaitingStarted=Stopwatch.GetTimestamp();_phase="WaitingForTelemetry";CheckFinal();
        }
    }
    private async Task RunLocalAsync()
    {
        try
        {
            Task work;
            lock(_gate)
                work=_shutdown.IsCancellationRequested || _simulation.Read().Phase is not ("Running" or "Waiting" or "Completed")
                    ? Task.CompletedTask : _simulation.RunAsync(null,_shutdown.Token);
            await work;
        }
        catch(OperationCanceledException)when(_shutdown.IsCancellationRequested){}
        finally
        {
            lock(_gate)
            {
                if(_localResetPending){_localResetPending=false;_simulation.Reset();_phase="Ready";Record("GridReset");}
            }
        }
    }
    private void OnPublished(SimulationPublication publication,long stamp)
    {
        lock(_gate){_published++;if(publication.Kind=="Telemetry")_lastPublishedTimestamp=stamp;
            _log.WriteLine(JsonSerializer.Serialize(new{@event="GridPublished",publication.Kind,publication.Topic,payload=Encoding.UTF8.GetString(publication.Payload),delivery="Unconfirmed"},_json));}
    }
    private async Task ExecuteAsync(CancellationToken token)
    {
        IGridControlConnection? connection=null;Task? sender=null;bool senderObserved=false;
        try
        {
            connection=_connectionFactory!();
            lock(_gate)_control=connection;
            connection.Received+=Receive;connection.Failed+=Fail;connection.Rejected+=Rejected;
            await connection.ConnectAsync(token);token.ThrowIfCancellationRequested();
            lock(_gate){_connected=true;_brokerState="Subscribed";}
            sender=_simulation.RunSessionAsync(_mqtt!,token);
            using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while(await timer.WaitForNextTickAsync(token))
            {
                if(sender.IsCompleted&&!senderObserved)
                {
                    senderObserved=true;
                    try{await sender;}
                    catch(Exception error)when(!token.IsCancellationRequested)
                    {
                        // 송신 AMR의 실패만으로 관제 Broker 연결을 끊거나 감시를 중지하지 않는다.
                        lock(_gate){_senderAvailable=false;_phase="AmrUnavailable";_failure="AmrSender:"+error.Message;_missionFinal=null;Record("GridAmrSenderEnded");}
                    }
                }
                lock(_gate)
                {
                    CheckFinal();
                    if(_missionFinal is not null&&!_finalConfirmed&&Stopwatch.GetElapsedTime(_finalWaitingStarted)>=TimeSpan.FromSeconds(3))Fail("FinalTelemetryNotReceived");
                    string? state=connection.Heartbeat?.State.ToString();
                    if(state!=_lastHeartbeatState){_lastHeartbeatState=state;Record("GridHeartbeatChanged");}
                }
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){Fail(error is IOException?error.Message:error.GetType().Name);}
        finally
        {
            _runCancellation!.Cancel();
            if(sender is not null)try{await sender;}catch(Exception error){lock(_gate)Record("GridSenderStopped:"+error.GetType().Name);}
            if(connection is not null)
            {
                connection.Received-=Receive;connection.Failed-=Fail;connection.Rejected-=Rejected;
                try{await connection.DisposeAsync();}
                catch(Exception error){lock(_gate){_failure="CleanupFailed:"+error.GetType().Name;_stopReason=2;}}
            }
            lock(_gate)
            {
                _lastHeartbeat=connection?.Heartbeat;_control=null;_senderAvailable=false;_connected=false;
                if(_stopReason!=2)_brokerState="Closed";
                _phase=_stopReason==2?"Failed":"Closed";Record("GridMqttEnded");
            }
        }
    }
    private GridCell? ReceivedCell(TelemetryMessage? message)=>GridMapCoordinates.ReceivedCell(message,_rows,_cols);
    private void Record(string name)
    {
        var state=_simulation.Read();
        _log.WriteLine(JsonSerializer.Serialize(new{timestamp=DateTimeOffset.UtcNow,name,state,failure=_failure,heartbeat=_control?.Heartbeat??_lastHeartbeat},_json));
        _recent.Enqueue($"{name} Phase={_phase??state.Phase} Position=({state.Current.Row},{state.Current.Col}) Moves={state.Moves} WaypointsPassed={state.NextWaypointIndex}/{state.Waypoints.Length}");
        while(_recent.Count>80)_recent.Dequeue();
    }
    public ValueTask DisposeAsync() { lock(_gate)return new(_disposeTask??=DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        lock(_gate){_closing=true;_simulation.Cancel();}
        _shutdown.Cancel();
        try{await _execution;}
        finally
        {
            _simulation.Moved-=OnMoved;_simulation.Connected-=OnConnected;_simulation.Published-=OnPublished;_simulation.MissionCompleted-=OnMissionCompleted;
            _runCancellation?.Dispose();_shutdown.Dispose();_log.Dispose();
        }
    }
}

