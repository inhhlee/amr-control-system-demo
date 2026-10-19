namespace Amr.Communication.Models;

/// <summary>검사된 모델과 Topic의 전달 대상을 함께 보존한다. ACK의 생략된 Id는 채우지 않는다.</summary>
public sealed class ReceivedMessage
{
    internal ReceivedMessage(string amrId, string kind, object model, DateTimeOffset receivedAtUtc)
    {
        AmrId = amrId;
        Kind = kind;
        Model = model;
        ReceivedAtUtc = receivedAtUtc;
    }

    public string AmrId { get; }
    public string Kind { get; }
    // Kind에 해당하는 HeartbeatMessage / TelemetryMessage / AlarmMessage / AckMessage다.
    public object Model { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
}
