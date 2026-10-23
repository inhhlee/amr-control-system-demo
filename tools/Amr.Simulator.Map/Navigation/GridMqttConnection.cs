using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Map.Navigation;

public interface IGridMqttConnection : IAsyncDisposable
{
    event Action<object>? Received;
    event Action<string>? Failed;
    event Action? Rejected;
    Task ConnectAsync(CancellationToken token);
    Task PublishAsync(SimulationPublication publication, CancellationToken token);
}

/// <summary>별도 송신 Client와 기존 관제 구독·검사·AMR별 큐의 수명을 소유한다.</summary>
public sealed class GridMqttConnection : IGridMqttConnection
{
    private readonly MqttMapReceiver _receiver;
    private readonly MqttSimulationTransport _sender;

    public GridMqttConnection(SimulatorMqttOptions options, string amrId, TextWriter log)
    {
        _receiver = new(options.Connection.Host, options.Connection.Port, amrId,
            message => Received?.Invoke(message.Model), () => Rejected?.Invoke(),
            _ => { }, reason => Failed?.Invoke(reason), log);
        // 구독자와 발행자는 다른 Client ID를 갖는다.
        _sender = new(new(options.Connection.Host, options.Connection.Port));
        _sender.ConnectionLost += SenderLost;
    }

    public event Action<object>? Received;
    public event Action<string>? Failed;
    public event Action? Rejected;
    private void SenderLost() => Failed?.Invoke("SenderDisconnected");

    public async Task ConnectAsync(CancellationToken token)
    {
        await _receiver.StartAsync(token); // SUBACK 뒤에만 발행한다.
        await _sender.ConnectAsync(token);
        token.ThrowIfCancellationRequested();
    }

    public Task PublishAsync(SimulationPublication publication, CancellationToken token) => _sender.PublishAsync(publication, token);

    public async ValueTask DisposeAsync()
    {
        _sender.ConnectionLost -= SenderLost;
        try { await _sender.DisconnectAsync(); }
        finally
        {
            _sender.Dispose();
            await _receiver.DisposeAsync();
        }
    }
}
