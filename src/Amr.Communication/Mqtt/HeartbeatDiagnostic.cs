namespace Amr.Communication.Mqtt;

// 검사 전 관찰 결과이며 정상 Heartbeat 모델·생존 판정이 아니다.
public sealed record HeartbeatDiagnostic(
    DateTimeOffset ReceivedAtUtc,
    string Topic,
    long PayloadBytes,
    string? PayloadPreview,
    bool PayloadTruncated);
