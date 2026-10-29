using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;
using Amr.Core;

namespace Amr.Simulator.Map.Control;

// 조립 계층의 형식 변환만 담당한다. 통신에는 미션을, Core에는 MQTT를 참조시키지 않는다.
internal sealed class FleetCommunicationBridge(AmrDomainManager domain)
{
    public void Observe(AmrCommunicationObservation observation) => domain.Observe(new(
        observation.SessionId, observation.Sequence, observation.AmrId,
        observation.FailureReason == "BrokerDisconnected" ? ConnectionObservation.Disconnected :
            observation.BrokerConnected ? ConnectionObservation.Connected : ConnectionObservation.Unknown,
        observation.Heartbeat.State switch {
            HeartbeatObservationState.Alive => LivenessObservation.Alive,
            HeartbeatObservationState.Unresponsive => LivenessObservation.Unresponsive,
            _ => LivenessObservation.Unknown
        }, observation.Heartbeat.Monitoring, observation.Heartbeat.LastReceivedAtUtc,
        observation.ObservedAtUtc, observation.FailureReason));

    public bool Receive(Guid sessionId, TelemetryMessage message) => domain.ReceiveTelemetry(new(sessionId,
        message.Id, message.Position.X, message.Position.Y, message.Speed, Enum.Parse<AmrState>(message.State), message.ReceivedAtUtc));
}
