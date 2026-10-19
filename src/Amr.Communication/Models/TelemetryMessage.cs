namespace Amr.Communication.Models;

// 개발 계약의 단위는 m이며 좌표의 음수 값을 허용한다.
public sealed record TelemetryPosition(double X, double Y);

// Timestamp는 원본 Int64, Speed는 m/s, State는 수신한 허용 문자열을 보존한다.
// 모델 반환은 실제 Core 상태 변경을 뜻하지 않는다.
public sealed record TelemetryMessage(
    string Id,
    long Timestamp,
    TelemetryPosition Position,
    double Speed,
    string State,
    DateTimeOffset ReceivedAtUtc);
