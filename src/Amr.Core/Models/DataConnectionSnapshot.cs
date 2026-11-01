namespace Amr.Core;

// 공급자 연결의 관측값이다. AMR의 운행 상태·업무 알람과 구분한다.
public enum DataConnectionState { Disconnected, Connecting, Connected, ReconnectWaiting, Reconnecting, Failed, Stopped }

public sealed record DataConnectionSnapshot(string Endpoint, string AmrId, DataConnectionState State,
    int RetryAttempt, int MaxAttempts, double RetryDelaySeconds, int RejectedMessages,
    bool TelemetryIsCurrent, string? Reason = null)
{
    public System.Collections.Immutable.ImmutableArray<string> RegisteredAmrIds { get; init; } = [];
    public bool HistoryAtCapacity { get; init; }
    public string? DataWarning { get; init; }
}
