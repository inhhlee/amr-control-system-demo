namespace Amr.Communication.Mqtt;

// JSON 검사 결과가 아니라, 검사된 메시지를 대기 줄에 넣었는지를 뜻한다.
public enum QueueAdmission
{
    Accepted,
    QueueFull,
    AmrLimitReached,
    NotAccepting
}
