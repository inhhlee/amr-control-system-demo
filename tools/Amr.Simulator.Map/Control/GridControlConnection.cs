using Amr.Simulator.Telemetry;
using Amr.Communication.Monitoring;

namespace Amr.Simulator.Map.Control;

public interface IGridControlConnection : IAsyncDisposable
{
    event Action<object>? Received;
    event Action<string>? Failed;
    event Action? Rejected;
    Task ConnectAsync(CancellationToken token);
    HeartbeatObservation? Heartbeat => null;
}

/// <summary>관제 통신 세션의 결과를 지도에 전달한다. 발행 Client는 Simulator가 별도로 소유한다.</summary>
public sealed class GridControlConnection : IGridControlConnection
{
    private readonly MqttMapReceiver _receiver;

    public GridControlConnection(SimulatorMqttOptions options, string amrId, TextWriter log, HeartbeatMonitorOptions? heartbeatOptions = null)
    {
        _receiver = new(options.Connection.Host, options.Connection.Port, amrId,
            message => Received?.Invoke(message.Model), () => Rejected?.Invoke(),
            _ => { }, reason => Failed?.Invoke(reason), log, heartbeatOptions);
    }

    public event Action<object>? Received;
    public event Action<string>? Failed;
    public event Action? Rejected;
    public Task ConnectAsync(CancellationToken token) => _receiver.StartAsync(token);
    public HeartbeatObservation? Heartbeat => _receiver.Heartbeat;
    public ValueTask DisposeAsync() => _receiver.DisposeAsync();
}
