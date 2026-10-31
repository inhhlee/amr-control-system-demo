namespace Amr.Communication.Mqtt;

/// <summary>시험용 기본값. 최대 횟수는 최초 시도 이후의 추가 시도 수이며 정상 구독 후 초기화된다.</summary>
public sealed record ReconnectOptions
{
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxAttempts { get; init; } = 5;

    public void Validate()
    {
        if (Delay < TimeSpan.FromMilliseconds(1) || Delay > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(Delay));
        if (MaxAttempts is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
    }
}

public sealed record ReconnectNotice(int Attempt, int Maximum, double DelaySeconds, string Reason);
