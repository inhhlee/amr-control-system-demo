using System.Buffers;

namespace Amr.Simulator.Telemetry;

// Payload의 수명은 콜백 반환까지다. 소비자는 동기 검사 후 불변 명령 모델만 큐에 보관한다.
public delegate void DeviceCommandReceived(string topic, ReadOnlySequence<byte> payload, bool retained);

/// <summary>장비 한 대의 전송 경계. 컨텍스트가 접속·구독·정리의 전체 수명을 소유한다.</summary>
public interface IAmrDeviceTransport : ISimulationTransport
{
    event DeviceCommandReceived? CommandReceived;
    Task SubscribeAsync(CancellationToken token);
    Task UnsubscribeAsync(CancellationToken token);
}
