namespace Amr.Communication.Mqtt;

// 수신 진단이며 Core 전달 모델·생존 판정이 아니다. 거부 입력의 원문은 숨긴다.
public sealed record HeartbeatDiagnostic(
    DateTimeOffset ReceivedAtUtc,
    string Topic,
    long PayloadBytes,
    string? PayloadPreview,
    bool PayloadTruncated,
    bool IsValid);
