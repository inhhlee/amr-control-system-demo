using System.Collections.Immutable;

namespace Amr.Core;

// 기간 경계·정렬·페이지 정책은 실제 이력 기능에서 결정한다. 첫 Mock은 유효한 빈 결과를 반환한다.
public sealed record AmrQuery(string? AmrId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Type = null);
public sealed record AlarmSnapshot(string AmrId, string Code, string Message, DateTimeOffset ObservedAt, long Revision);
public sealed record HistoryEntry(string AmrId, string Type, string Description, DateTimeOffset ObservedAt);
public sealed record HistoryResult(ImmutableArray<HistoryEntry> Entries);
