namespace Amr.Communication.Models;

// Result는 명령의 수신 결과를 그대로 보존한다. FAILURE도 정상적인 ACK 데이터다.
// Id·Reason의 null은 필드 생략을 뜻하며 Topic ID로 Id를 자동 보충하지 않는다.
public sealed record AckMessage(
    string CommandId,
    string Result,
    long Timestamp,
    string? Id,
    string? Reason,
    DateTimeOffset ReceivedAtUtc);
