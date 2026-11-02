using System.Collections.Immutable;

namespace Amr.Core;

public enum AmrChangeKind { InitialSnapshot, DeviceChanged, CommandChanged }

// 첫 실행본은 작은 전체 스냅샷을 전달한다. Revision은 공급자 인스턴스 내에서 증가한다.
public sealed record AmrChange(
    long Revision,
    AmrChangeKind Kind,
    ImmutableArray<AmrSnapshot> Devices,
    ImmutableArray<AlarmSnapshot> Alarms,
    ImmutableArray<CommandSnapshot> Commands,
    string? AmrId = null,
    string? CommandId = null)
{
    // 작은 Mock 세션의 전체 이력. 실제 DB의 페이지/보존 계약과 구분한다.
    public ImmutableArray<HistoryEntry> History { get; init; } = [];
    // 위치 수신 전에도 연결 상태를 통지한다. 기존 Mock/격자 공급자는 null을 유지한다.
    public DataConnectionSnapshot? Connection { get; init; }
    // Fleet의 장비별 수신 세션. Connection은 전체 요약이며 제어 허용은 대상 세션으로 판단한다.
    public ImmutableDictionary<string, DataConnectionSnapshot> DeviceConnections { get; init; }
        = ImmutableDictionary<string, DataConnectionSnapshot>.Empty;
}

public sealed class AmrResyncRequiredException : Exception
{
    public AmrResyncRequiredException() : base("변경 통지 버퍼가 가득 찼습니다. 새 구독의 초기 스냅샷으로 동기화하세요.") { }
}
