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
    string? CommandId = null);

public sealed class AmrResyncRequiredException : Exception
{
    public AmrResyncRequiredException() : base("변경 통지 버퍼가 가득 찼습니다. 새 구독의 초기 스냅샷으로 동기화하세요.") { }
}
