namespace Scheduler.Domain.Model;

/// <summary>區域類型：決定資格與偏好的區域分組（一般病房、ICU、總值）。</summary>
public sealed record AreaType(string Code, string Name);

/// <summary>每日需要有人值班的位置。</summary>
/// <param name="RequiredPerDay">每日需求人數。目前 5 個區域皆為 1，無主責／支援之分。</param>
public sealed record Area(string Id, string Code, string Name, string AreaTypeCode, int RequiredPerDay = 1);

/// <summary>一位人員在某一日負責某一個區域。領域識別子是 <c>(AreaId, Date)</c>。</summary>
public sealed record Duty(string AreaId, DateOnly Date, string StaffId);

/// <summary>人員登記的「這天絕對不能排我」。獨立於值班表存在，是求解的輸入。</summary>
public sealed record BlockedDay(string StaffId, DateOnly Date);

/// <summary>某人在某月結束時、相對於同組其他人多出的額度點數。供下個月當起始偏移。</summary>
public sealed record CarryOverEntry(string StaffId, int Points);

/// <summary>逐月覆寫。目前唯一的用途是 R6 的額度上限。</summary>
public sealed record MonthlyOverride(YearMonth YearMonth, IReadOnlyDictionary<string, int> QuotaCapByRank)
{
    public static MonthlyOverride Empty(YearMonth yearMonth) => new(yearMonth, new Dictionary<string, int>());
}
