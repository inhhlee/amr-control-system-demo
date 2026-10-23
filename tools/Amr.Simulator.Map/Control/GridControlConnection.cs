using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Map.Control;

public interface IGridControlConnection : IAsyncDisposable
{
    event Action<object>? Received;
    event Action<string>? Failed;
    event Action? Rejected;
    Task ConnectAsync(CancellationToken token);
}

/// <summary>관제 구독·검사·AMR별 큐만 소유한다. 발행 Client는 Simulator가 소유한다.</summary>
public sealed class GridControlConnection : IGridControlConnection
{
    private readonly MqttMapReceiver _receiver;

    public GridControlConnection(SimulatorMqttOptions options, string amrId, TextWriter log)
    {
        _receiver = new(options.Connection.Host, options.Connection.Port, amrId,
            message => Received?.Invoke(message.Model), () => Rejected?.Invoke(),
            _ => { }, reason => Failed?.Invoke(reason), log);
    }

    public event Action<object>? Received;
    public event Action<string>? Failed;
    public event Action? Rejected;
    public Task ConnectAsync(CancellationToken token) => _receiver.StartAsync(token);
    public ValueTask DisposeAsync() => _receiver.DisposeAsync();
}
