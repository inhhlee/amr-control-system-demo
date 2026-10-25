namespace Amr.Core;

public enum ProviderMode { Mock, Actual }

// 조회 결과는 값 사본이다. UI 스레드 전환과 구독 취소는 소비자가 담당한다.
public interface IAmrDataProvider : IAsyncDisposable
{
    ProviderMode Mode { get; }
    Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<AmrSnapshot?> GetDeviceAsync(string amrId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken cancellationToken = default);
    Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken cancellationToken = default);
    Task<CommandSnapshot?> GetCommandAsync(string amrId, string commandId, CancellationToken cancellationToken = default);

    // 첫 항목은 전체 스냅샷. 버퍼 초과 예외를 받으면 새 구독의 첫 스냅샷부터 다시 적용한다.
    IAsyncEnumerable<AmrChange> WatchAsync(CancellationToken cancellationToken = default);
}
