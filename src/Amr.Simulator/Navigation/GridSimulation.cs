using System.Diagnostics;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Navigation;

public sealed record GridSimulationCompletion(long Timestamp, double X, double Y);

/// <summary>
/// 격자 AMR의 상태·이동 타이머·송신 Client를 소유한다. 호스트는 Start 후 RunAsync를
/// 한 번 호출하고 취소·실패 뒤에도 종료를 기다린다. 관제 수신이나 웹 화면에 의존하지 않는다.
/// </summary>
public sealed class GridSimulation
{
    public const int DefaultStepIntervalMs = 600;
    private readonly object _gate = new();
    private readonly GridNavigator _navigator;
    private readonly Func<ISimulationTransport>? _transportFactory;
    private bool _executing;
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
    // 알림은 상태 잠금 밖에서 호출해 관제 잠금과 역순으로 기다리지 않는다.
    public event Action? Connected;
    public event Action? Moved;
    public event Action<SimulationPublication, long>? Published;

    public GridSnapshot Read() { lock (_gate) return _navigator.Read(); }
    public bool Start()
    {
        lock (_gate)
        {
            if (_executing || !_navigator.Start()) return false;
            return true;
        }
    }
    public void Cancel() { lock (_gate) _navigator.Cancel(); }
    public void Reset()
    {
        lock (_gate)
        {
            if (_executing) throw new InvalidOperationException("실행 종료 후 초기화해야 합니다.");
            _navigator.Reset();
        }
    }
    public bool SetObstacle(GridCell cell, bool blocked)
    {
        lock (_gate) return _navigator.SetObstacle(cell, blocked);
    }

    public async Task<GridSimulationCompletion?> RunAsync(SimulatorMqttOptions? mqtt, CancellationToken token)
    {
        if (mqtt is not null && (mqtt.TelemetryMs <= 0 || mqtt.HeartbeatMs <= 0))
            throw new ArgumentException("송신 주기는 양수여야 합니다.", nameof(mqtt));
        lock (_gate)
        {
            if (_executing) throw new InvalidOperationException("이미 실행 중입니다.");
            if (_navigator.Read().Phase is not ("Running" or "Waiting" or "Completed"))
                throw new InvalidOperationException("Start 후 실행해야 합니다.");
            _executing = true;
        }
        try
        {
            return mqtt is null ? await RunLocalAsync(token) : await RunMqttAsync(mqtt, token);
        }
        finally
        {
            lock (_gate) { _navigator.Cancel(); _executing = false; }
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

    private async Task<GridSimulationCompletion> RunMqttAsync(SimulatorMqttOptions mqtt, CancellationToken token)
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
            while (true)
            {
                runToken.ThrowIfCancellationRequested();
                if (elapsed.ElapsedMilliseconds >= nextStep)
                {
                    Step(runToken);
                    nextStep = elapsed.ElapsedMilliseconds + StepIntervalMs;
                }
                var current = Read();
                if (current.Phase == "Completed") break;
                if (current.Phase == "Canceled") throw new OperationCanceledException(runToken);
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
            long timestamp = await Publish(false, true);
            runToken.ThrowIfCancellationRequested();
            var final = GridTelemetry.Capture(AmrId, Read(), true, StepIntervalMs);
            return new(timestamp, final.X, final.Y);
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

        async Task<long> Publish(bool heartbeat, bool stopped)
        {
            SimulationPublication publication;
            long stamp;
            lock (_gate)
            {
                stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastTimestamp + 1);
                _lastTimestamp = stamp;
                publication = SimulationMessages.Create(GridTelemetry.Capture(AmrId, _navigator.Read(), stopped, StepIntervalMs), heartbeat, stamp);
            }
            await sender.PublishAsync(publication, runToken);
            Published?.Invoke(publication, stamp);
            return stamp;
        }
    }
}
