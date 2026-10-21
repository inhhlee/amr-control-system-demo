namespace Amr.Simulator.Telemetry;

// 세션이 연결·발행·해제·Dispose를 소유한다. 전송 실패 후 같은 인스턴스를 재사용하지 않는다.
public interface ISimulationTransport : IDisposable
{
    event Action? ConnectionLost;
    Task ConnectAsync(CancellationToken token);
    Task PublishAsync(SimulationPublication message, CancellationToken token);
    Task DisconnectAsync();
}
