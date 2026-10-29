using Amr.Communication.Monitoring;

namespace Amr.Communication.Mqtt;

// 통신 관측만 전달한다. 미션/업무 경보/GUI는 통신 계층의 책임이 아니다.
public sealed record AmrCommunicationObservation(Guid SessionId, long Sequence, string AmrId,
    HeartbeatObservation Heartbeat, bool BrokerConnected, string? FailureReason, DateTimeOffset ObservedAtUtc);
