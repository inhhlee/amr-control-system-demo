namespace Amr.Communication.Mqtt;

/// <summary>입력 검사와 구분하는 처리 기록. Payload와 예외 Message를 포함하지 않는다.</summary>
public sealed record QueueProcessingEvent(string Code, string? AmrId = null, string? Kind = null,
    int Count = 0, string? ExceptionType = null);
