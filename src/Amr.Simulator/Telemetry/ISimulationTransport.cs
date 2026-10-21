namespace Amr.Simulator.Telemetry;

// 세션이 연결·발행·해제·Dispose를 소유한다. 전송 실패 후 같은 인스턴스를 재사용하지 않는다.
public interface ISimulationTransport : IDisposable
{
    // 접속된 연결의 단절만 통지한다. ConnectAsync 반환 직전에도 발생할 수 있다.
    event Action? ConnectionLost;
    Task ConnectAsync(CancellationToken token);
    Task PublishAsync(SimulationPublication message, CancellationToken token);
    Task DisconnectAsync();
}
