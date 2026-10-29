using System.Diagnostics;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Navigation;

public sealed record GridSimulationCompletion(long Timestamp, double X, double Y, long Generation = 0);

/// <summary>
/// 격자 AMR의 상태·이동 타이머·송신 Client를 소유한다. RunSessionAsync는 임무 사이에도
/// 통신을 유지하며, 호스트는 세션 취소·실패 뒤 종료를 기다린다.
/// </summary>
public sealed class GridSimulation
{
    public const int DefaultStepIntervalMs = 600;
    private readonly object _gate = new();
    private readonly GridNavigator _navigator;
    private readonly Func<ISimulationTransport>? _transportFactory;
    private bool _executing;
    private bool _persistent;
    private long _missionGeneration;
    private long _lastTimestamp;

    public GridSimulation(string amrId, GridNavigator? navigator = null,
        int stepIntervalMs = DefaultStepIntervalMs, Func<ISimulationTransport>? transportFactory = null)
    {
        if (stepIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepIntervalMs));
        AmrId = amrId;
        StepIntervalMs = stepIntervalMs;
        _navigator = navigator ?? new();
        _transportFactory = transportFactory;
    }

    public string AmrId { get; }
    public int StepIntervalMs { get; }
    public long MissionGeneration { get { lock (_gate) return _missionGeneration; } }
    // 알림은 상태 잠금 밖에서 호출해 관제 잠금과 역순으로 기다리지 않는다.
    public event Action? Connected;
    public event Action? Moved;
    public event Action<SimulationPublication, long>? Published;
    public event Action<GridSimulationCompletion>? MissionCompleted;

    public GridSnapshot Read() { lock (_gate) return _navigator.Read(); }
    public bool Start()
    {
        lock (_gate)
        {
            if ((_executing && !_persistent) || !_navigator.Start()) return false;
            _missionGeneration++;
            return true;
        }
    }
    public void Cancel() { lock (_gate) _navigator.Cancel(); }
    public void Reset()
    {
        lock (_gate)
        {
            if (_executing && !_persistent) throw new InvalidOperationException("실행 종료 후 초기화해야 합니다.");
            _navigator.Reset();
            _missionGeneration++;
        }
    }
    public bool SetObstacle(GridCell cell, bool blocked)
    {
        lock (_gate) return _navigator.SetObstacle(cell, blocked);
    }

    public async Task<GridSimulationCompletion?> RunAsync(SimulatorMqttOptions? mqtt, CancellationToken token)
        => await RunCoreAsync(mqtt, token, false);

    // AMR 통신 수명은 임무 수명보다 길다. Ready/Waiting/Completed/Canceled에서도 송신한다.
    public async Task RunSessionAsync(SimulatorMqttOptions mqtt, CancellationToken token)
        => await RunCoreAsync(mqtt, token, true);

    private async Task<GridSimulationCompletion?> RunCoreAsync(SimulatorMqttOptions? mqtt, CancellationToken token, bool persistent)
    {
        if (mqtt is not null && (mqtt.TelemetryMs <= 0 || mqtt.HeartbeatMs <= 0))
            throw new ArgumentException("송신 주기는 양수여야 합니다.", nameof(mqtt));
        lock (_gate)
        {
            if (_executing) throw new InvalidOperationException("이미 실행 중입니다.");
            if (!persistent && _navigator.Read().Phase is not ("Running" or "Waiting" or "Completed"))
                throw new InvalidOperationException("Start 후 실행해야 합니다.");
            _executing = true; _persistent = persistent;
        }
        try
        {
            return mqtt is null ? await RunLocalAsync(token) : await RunMqttAsync(mqtt, token, persistent);
        }
        finally
        {
            lock (_gate) { _navigator.Cancel(); _executing = _persistent = false; }
        }
    }

    private bool Step(CancellationToken token)
    {
        bool moved;
        lock (_gate) { token.ThrowIfCancellationRequested(); moved = _navigator.Step(); }
        if (moved) Moved?.Invoke();
        return moved;
    }

    private async Task<GridSimulationCompletion?> RunLocalAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(StepIntervalMs));
        while (Read().Phase is "Running" or "Waiting")
        {
            if (!await timer.WaitForNextTickAsync(token)) break;
            Step(token);
        }
        return null;
    }

    private async Task<GridSimulationCompletion?> RunMqttAsync(SimulatorMqttOptions mqtt, CancellationToken token, bool persistent)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var sender = _transportFactory?.Invoke() ?? new MqttSimulationTransport(mqtt.Connection);
        int disconnected = 0;
        void Lost() { Interlocked.Exchange(ref disconnected, 1); lifetime.Cancel(); }
        sender.ConnectionLost += Lost;
        var runToken = lifetime.Token;
        try
        {
            await sender.ConnectAsync(runToken);
            runToken.ThrowIfCancellationRequested();
            Connected?.Invoke();
            await Publish(false, true);
            await Publish(true, true);
            var elapsed = Stopwatch.StartNew();
            long nextStep = StepIntervalMs, nextTelemetry = mqtt.TelemetryMs, nextHeartbeat = mqtt.HeartbeatMs;
            int lastMoves = 0;
            string lastPhase = "Ready";
            long completedGeneration = -1;
            while (true)
            {
                runToken.ThrowIfCancellationRequested();
                if (elapsed.ElapsedMilliseconds >= nextStep)
                {
                    Step(runToken);
                    nextStep = elapsed.ElapsedMilliseconds + StepIntervalMs;
                }
                GridSnapshot current;
                long generation;
                lock (_gate) { current = _navigator.Read(); generation = _missionGeneration; }
                if (current.Phase == "Completed" && (!persistent || completedGeneration != generation))
                {
                    long timestamp = await Publish(false, true, current);
                    runToken.ThrowIfCancellationRequested();
                    var final = GridTelemetry.Capture(AmrId, current, true, StepIntervalMs);
                    var completion = new GridSimulationCompletion(timestamp, final.X, final.Y, generation);
                    completedGeneration = generation;
                    MissionCompleted?.Invoke(completion);
                    if (!persistent) return completion;
                }
                if (current.Phase == "Canceled" && !persistent) throw new OperationCanceledException(runToken);
                if (current.Moves != lastMoves || current.Phase != lastPhase || elapsed.ElapsedMilliseconds >= nextTelemetry)
                {
                    await Publish(false, false);
                    lastMoves = current.Moves;
                    lastPhase = current.Phase;
                    nextTelemetry = elapsed.ElapsedMilliseconds + mqtt.TelemetryMs;
                }
                if (elapsed.ElapsedMilliseconds >= nextHeartbeat)
                {
                    await Publish(true, false);
                    nextHeartbeat = elapsed.ElapsedMilliseconds + mqtt.HeartbeatMs;
                }
                await Task.Delay(20, runToken);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && Volatile.Read(ref disconnected) != 0)
        {
            throw new IOException("SenderDisconnected");
        }
        finally
        {
            sender.ConnectionLost -= Lost;
            await sender.DisconnectAsync();
        }

        async Task<long> Publish(bool heartbeat, bool stopped, GridSnapshot? snapshot = null)
        {
            SimulationPublication publication;
            long stamp;
            lock (_gate)
            {
                stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastTimestamp + 1);
                _lastTimestamp = stamp;
                publication = SimulationMessages.Create(GridTelemetry.Capture(AmrId, snapshot ?? _navigator.Read(), stopped, StepIntervalMs), heartbeat, stamp);
            }
            await sender.PublishAsync(publication, runToken);
            Published?.Invoke(publication, stamp);
            return stamp;
        }
    }
}
