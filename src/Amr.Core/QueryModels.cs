using System.Collections.Immutable;

namespace Amr.Core;

// 실제 DB의 기간 경계·정렬·페이지 정책과 Mock 세션의 조회 규칙은 구분한다.
public sealed record AmrQuery(string? AmrId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Type = null);
public enum AlarmSeverity { Unknown, Error, Warning }
public sealed record AlarmSnapshot(string AmrId, string Code, string Message, DateTimeOffset ObservedAt, long Revision)
{
    // 등급은 오류/경고 구분, IsActive는 현재 발생 중 여부, Note는 화면의 '비고'에 대응한다.
    // Code는 기존 계약을 보존하는 데이터이며, 이번 Figma 화면에서는 표시하지 않는다.
    public AlarmSeverity Severity { get; init; } = AlarmSeverity.Unknown;
    public bool IsActive { get; init; } = true;
    public string Note { get; init; } = "";
}
public sealed record HistoryEntry(string AmrId, string Type, string Description, DateTimeOffset ObservedAt);
public sealed record HistoryResult(ImmutableArray<HistoryEntry> Entries);
