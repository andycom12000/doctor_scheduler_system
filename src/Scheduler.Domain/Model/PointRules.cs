namespace Scheduler.Domain.Model;

/// <summary>
/// 點數規則。系統裡有兩套並存、互不相關的點數：
/// <see cref="Quota"/> 是額度點數（有硬上限、排班者日常在看的那一套），
/// <see cref="Fairness"/> 是公平性點數（實驗性、沒有上限、預設停用）。
/// </summary>
public sealed record PointRules(QuotaPointRule Quota, FairnessPointRule Fairness);

/// <summary>額度點數：平日 1、假日 2。補班日視為平日。</summary>
public sealed record QuotaPointRule(int Weekday, int Holiday)
{
    public int ValueOf(CalendarDay day) => day.IsHoliday ? Holiday : Weekday;
}

/// <summary>
/// 公平性點數。每一格由「當日」與「隔日」是否為假日查表，兩者都是行事曆常數；
/// 唯一決策相依的是連值週六加分。
/// </summary>
public sealed record FairnessPointRule(
    IReadOnlyDictionary<PointType, IReadOnlyList<FairnessTableEntry>> Tables,
    ConsecutiveSaturdayBonus ConsecutiveSaturdayBonus)
{
    /// <summary>查表：某點數類型在「當日 today、隔日 tomorrow」的點數。</summary>
    public int Lookup(PointType pointType, CalendarDay today, CalendarDay tomorrow)
    {
        if (!Tables.TryGetValue(pointType, out var table))
        {
            throw new KeyNotFoundException($"公平性點數沒有 Type {pointType} 的查表");
        }

        var todayKind = today.IsHoliday ? DayKind.Holiday : DayKind.Weekday;
        var tomorrowKind = tomorrow.IsHoliday ? DayKind.Holiday : DayKind.Weekday;
        var entry = table.FirstOrDefault(e => e.Today == todayKind && e.Tomorrow == tomorrowKind)
            ?? throw new KeyNotFoundException($"公平性點數 Type {pointType} 查表缺少 {todayKind}/{tomorrowKind} 那一列");
        return entry.Points;
    }
}

/// <summary>查表的一列。<see cref="Today"/> 與 <see cref="Tomorrow"/> 只會是 Weekday 或 Holiday。</summary>
public sealed record FairnessTableEntry(DayKind Today, DayKind Tomorrow, int Points);

/// <summary>
/// 連值兩個週六且中間沒有國定假日可以喘息時的加分。
/// 這裡的「假日」特指國定假日，不含一般週末——若含週末，條件永遠為假。
/// </summary>
/// <param name="WindowDays">從值班的週六當日起算、含當日的天數。預設 10。</param>
public sealed record ConsecutiveSaturdayBonus(int Points, int WindowDays);
