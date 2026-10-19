namespace Amr.Communication.Models;

// Timestamp는 송신자가 보낸 Int64를 그대로 보존한다. 수신 시각·생존 판정과 다르다.
public sealed record HeartbeatMessage(
    string Id,
    long Timestamp,
    string Status,
    DateTimeOffset ReceivedAtUtc);
