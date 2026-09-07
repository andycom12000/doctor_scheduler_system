using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Tests;

/// <summary>
/// 不依賴真實名單的 fixture。區域、身分、資格、點數規則一律用出廠值
/// （<c>docs/constraint-defaults.md</c>），人員與值班由各測試自己放。
///
/// 預設月份 2026-09：9/1 是週二，週六落在 5、12、19、26。行事曆只有週末、沒有國定假日，
/// 要測國定假日的測試自己用 <see cref="WithCalendar"/> 覆寫。
/// </summary>
public sealed class ContextBuilder
{
    public static readonly YearMonth DefaultMonth = new(2026, 9);

    private readonly YearMonth _month;
    private Calendar _calendar;
    private readonly List<Staff> _staff = new();
    private readonly List<Duty> _duties = new();
    private readonly List<Duty> _previous = new();
    private readonly List<BlockedDay> _blocked = new();
    private readonly List<CarryOverEntry> _carryOver = new();
    private readonly Dictionary<string, int> _quotaCapOverride = new();

    public ContextBuilder(YearMonth? month = null)
    {
        _month = month ?? DefaultMonth;
        // 從上月月初涵蓋到次月第一天：跨月 tail 與「隔日」查表都用得到
        _calendar = Calendar.Plain(_month.Previous.FirstDay, _month.Next.FirstDay);
    }

    public YearMonth Month => _month;

    public DateOnly Day(int day) => new(_month.Year, _month.Month, day);

    public DateOnly PreviousMonthDay(int day) => new(_month.Previous.Year, _month.Previous.Month, day);

    /// <summary>加一位人員。id 與員編都用 rankCode 加流水號，測試裡好認。</summary>
    public ContextBuilder WithStaff(string id, string rankCode, string? name = null)
    {
        _staff.Add(new Staff(id, EmployeeNo: id, name ?? id, rankCode));
        return this;
    }

    public ContextBuilder WithInactiveStaff(string id, string rankCode)
    {
        _staff.Add(new Staff(id, EmployeeNo: id, id, rankCode, StaffStatus.Inactive));
        return this;
    }

    public ContextBuilder WithDuty(string staffId, int day, string areaId = "area-a")
    {
        _duties.Add(new Duty(areaId, Day(day), staffId));
        return this;
    }

    public ContextBuilder WithDuties(string staffId, string areaId, params int[] days)
    {
        foreach (var d in days)
        {
            WithDuty(staffId, d, areaId);
        }

        return this;
    }

    /// <summary>上月月尾的值班（跨月固定輸入）。</summary>
    public ContextBuilder WithPreviousMonthDuty(string staffId, int day, string areaId = "area-a")
    {
        _previous.Add(new Duty(areaId, PreviousMonthDay(day), staffId));
        return this;
    }

    public ContextBuilder WithBlockedDay(string staffId, int day)
    {
        _blocked.Add(new BlockedDay(staffId, Day(day)));
        return this;
    }

    public ContextBuilder WithCarryOver(string staffId, int points)
    {
        _carryOver.Add(new CarryOverEntry(staffId, points));
        return this;
    }

    public ContextBuilder WithQuotaCapOverride(string rankCode, int cap)
    {
        _quotaCapOverride[rankCode] = cap;
        return this;
    }

    public ContextBuilder WithCalendar(Func<Calendar, Calendar> change)
    {
        _calendar = change(_calendar);
        return this;
    }

    public SchedulingContext Build() => new(
        _month,
        _calendar,
        DefaultAreas.Areas,
        _staff.ToArray(),
        DefaultRanks.Ranks,
        DefaultRanks.Eligibility,
        DefaultPointRules.Rules,
        new MonthlyOverride(_month, _quotaCapOverride),
        _duties.ToArray(),
        _previous.ToArray(),
        _blocked.ToArray(),
        _carryOver.ToArray());
}
