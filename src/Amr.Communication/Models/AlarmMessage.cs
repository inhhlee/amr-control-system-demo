namespace Amr.Communication.Models;

// Timestamp와 문자열은 수신 값을 보존한다. Message의 null은 필드 생략을 뜻한다.
// Level·AlarmState는 알람 정보이며 이 모델 생성으로 실제 AMR 주행 상태를 변경하지 않는다.
public sealed record AlarmMessage(
    string Id,
    long Timestamp,
    string Code,
    string Level,
    string AlarmState,
    string? Message,
    DateTimeOffset ReceivedAtUtc);
