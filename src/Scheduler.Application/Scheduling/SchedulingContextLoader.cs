using Scheduler.Application.Calendars;
using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Scheduling;

/// <summary>
/// 組某月 <see cref="SchedulingContext"/> 的結果，連同 handler 還會用到、但 context 本身不帶的東西：
/// 標頭（可能不存在）、約束設定、身分組、區域類型、非阻斷的提醒。
/// </summary>
public sealed record LoadedContext(
    SchedulingContext Context,
    ScheduleHeader? Header,
    ConstraintSettings Constraints,
    IReadOnlyList<RankGroup> RankGroups,
    IReadOnlyList<AreaType> AreaTypes,
    IReadOnlyList<string> Warnings)
{
    public bool ScheduleExists => Header is not null;
}

/// <summary>
/// 組一份月份 context 要撈十幾樣東西，跨月與跨年的撈取規則只住在這裡（ARCHITECTURE §5）：
/// <list type="bullet">
/// <item>行事曆從「上月尾巴的起點」涵蓋到「次月第一天 + 連值週六加分的視窗」，
/// 公平性點數查「隔日」與往後找國定假日才不會缺日</item>
/// <item>上月月尾只撈最後 <c>max(minGap, maxConsecutive)</c> 天，天數讀約束設定不寫死（§4.4）</item>
/// <item>月結轉依 ADR-0004 分支：本月已發布讀凍結的那份；草稿即時讀上月發布時結算的那份；
/// 上月未發布就是空的，走 warnings 不擋</item>
/// <item>人員撈全部（含已停用），值班表上引用到的人才找得到；只列在職的是 handler 的事</item>
/// </list>
/// </summary>
public sealed class SchedulingContextLoader
{
    public const string PreviousMonthNotPublishedWarning = "上月尚未發布，月結轉為空";
    public const string PreviousMonthIsDraftWarning = "上月值班表仍是草稿，跨月間隔以草稿計";

    private readonly IScheduleRepository _schedules;
    private readonly IBlockedDayRepository _blockedDays;
    private readonly IStaffRepository _staff;
    private readonly ISettingsRepository _settings;
    private readonly CalendarBuilder _calendar;

    public SchedulingContextLoader(
        IScheduleRepository schedules,
        IBlockedDayRepository blockedDays,
        IStaffRepository staff,
        ISettingsRepository settings,
        ICalendarRepository calendar)
    {
        _schedules = schedules;
        _blockedDays = blockedDays;
        _staff = staff;
        _settings = settings;
        _calendar = new CalendarBuilder(calendar);
    }

    /// <summary>
    /// 該月尚無值班表時仍會組出一份（值班為空）——不可排班日與可行性預警發生在值班表存在之前。
    /// 要 404 的 handler 自己看 <see cref="LoadedContext.ScheduleExists"/>。
    /// </summary>
    public async Task<LoadedContext> LoadAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var constraints = await _settings.GetConstraintsAsync(cancellationToken);
        var pointRules = await _settings.GetPointRulesAsync(cancellationToken);
        var areas = await _settings.GetAreasAsync(cancellationToken);
        var ranks = await _settings.GetRanksAsync(cancellationToken);
        var eligibility = await _settings.GetEligibilityAsync(cancellationToken);
        var monthlyOverride = await _settings.GetMonthlyOverrideAsync(month, cancellationToken);
        var staff = await _staff.ListAsync(cancellationToken);

        var tailDays = TailDaysOf(constraints);
        var tailStart = month.FirstDay.AddDays(-tailDays);
        var calendarEnd = month.LastDay.AddDays(Math.Max(1, pointRules.Fairness.ConsecutiveSaturdayBonus.WindowDays));
        var calendar = await _calendar.BuildAsync(tailStart, calendarEnd, cancellationToken);

        var header = await _schedules.FindAsync(month, cancellationToken);
        var duties = header is null
            ? Array.Empty<Duty>()
            : await _schedules.GetDutiesAsync(month, cancellationToken);
        var previousDuties = tailDays == 0
            ? Array.Empty<Duty>()
            : await _schedules.GetDutiesInRangeAsync(tailStart, month.Previous.LastDay, cancellationToken);
        var blockedDays = await _blockedDays.ListAsync(month, cancellationToken);

        var warnings = new List<string>();
        var previousHeader = await _schedules.FindAsync(month.Previous, cancellationToken);
        var previousPublished = previousHeader?.Status == ScheduleStatus.Published;
        if (previousHeader is not null && !previousPublished && previousDuties.Count > 0)
        {
            warnings.Add(PreviousMonthIsDraftWarning);
        }

        IReadOnlyList<CarryOverEntry> carryOver;
        // 「已凍結」看 PublishedAt 不看 Status（IScheduleRepository 的約定）：發布過就有凍結的那份
        if (header?.PublishedAt is not null)
        {
            carryOver = await _schedules.GetCarryOverAppliedAsync(month, cancellationToken);
        }
        else if (previousPublished)
        {
            carryOver = await _schedules.GetCarryOverAsync(month.Previous, cancellationToken);
        }
        else
        {
            carryOver = Array.Empty<CarryOverEntry>();
            warnings.Add(PreviousMonthNotPublishedWarning);
        }

        var context = new SchedulingContext(
            month,
            calendar,
            areas.Areas,
            staff,
            ranks.Ranks,
            eligibility,
            pointRules,
            monthlyOverride,
            duties,
            previousDuties,
            blockedDays,
            carryOver);
        context.EnsureConsistent();

        return new LoadedContext(context, header, constraints, ranks.Groups, areas.AreaTypes, warnings);
    }

    /// <summary>上月月尾要撈幾天：所有 MinGap／MaxConsecutive 定義的最大天數（含停用的，多撈無害）。</summary>
    internal static int TailDaysOf(ConstraintSettings constraints) =>
        constraints.All
            .Where(c => c.Primitive is Primitive.MinGap or Primitive.MaxConsecutive)
            .Select(c => c.Params.Days ?? 0)
            .DefaultIfEmpty(0)
            .Max();
}
