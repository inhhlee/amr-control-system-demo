namespace Amr.Communication.Mqtt;

// 진단은 정상 모델 전달과 구분한다. 거부된 Payload의 미리보기는 항상 null이다.
public sealed record MessageDiagnostic(
    DateTimeOffset ReceivedAtUtc,
    string Topic,
    long PayloadBytes,
    string? PayloadPreview,
    bool PayloadTruncated,
    bool IsValid);

// Stage는 Received / Rejected / Diagnostic이며 예외 메시지·원문은 포함하지 않는다.
public sealed record CallbackFailure(string Stage, string ExceptionType);
