using Scheduler.Domain.Model;

namespace Scheduler.Application.Schedules;

/// <summary>值班表的生命週期狀態。契約的 <c>ScheduleStatus</c>。</summary>
public enum ScheduleStatus
{
    Draft,
    Published,
}

/// <summary>
/// 一個月的值班表標頭：狀態、修改次數、發布時間。格子本身是 <see cref="Duty"/>。
/// 這是生命週期資料而非規則，所以住在 Application 而不是 Domain。
/// </summary>
/// <param name="Revision">修改次數計數器，供 UI 顯示與快取失效，不是併發控制。</param>
/// <param name="PublishedAt">最近一次發布的時間；草稿為 null。第一次發布後不會再變回 null。</param>
public sealed record ScheduleHeader(
    YearMonth YearMonth,
    ScheduleStatus Status,
    int Revision,
    DateTimeOffset? PublishedAt)
{
    public static ScheduleHeader NewDraft(YearMonth yearMonth) =>
        new(yearMonth, ScheduleStatus.Draft, Revision: 0, PublishedAt: null);
}
