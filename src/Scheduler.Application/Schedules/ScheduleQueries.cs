using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Validation;

namespace Scheduler.Application.Schedules;

/// <summary>
/// 值班表的讀取路徑：值班表本體、驗證、違規、點數看板、單日、空缺、候選人。
/// 全部先叫 <see cref="SchedulingContextLoader"/> 組 context，再用 Domain 的檢查器與點數計算器算，
/// 這裡不重寫任何約束語義（ADR-0002）。
/// </summary>
public sealed class ScheduleQueries
{
    private readonly IScheduleRepository _schedules;
    private readonly SchedulingContextLoader _loader;

    public ScheduleQueries(IScheduleRepository schedules, SchedulingContextLoader loader)
    {
        _schedules = schedules;
        _loader = loader;
    }

    public async Task<IReadOnlyList<ScheduleSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var headers = await _schedules.ListAsync(cancellationToken);
        var result = new List<ScheduleSummary>(headers.Count);
        foreach (var header in headers.OrderBy(h => h.YearMonth))
        {
            var loaded = await _loader.LoadAsync(header.YearMonth, cancellationToken);
            var validation = new ViolationChecker(loaded.Context).Check(loaded.Constraints);
            result.Add(new ScheduleSummary(header.YearMonth, header.Status, header.Revision, header.PublishedAt, validation.HardCount));
        }

        return result;
    }

    public async Task<ScheduleView> GetAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var loaded = await RequireAsync(month, cancellationToken);
        var header = loaded.Header!;
        var ctx = loaded.Context;
        return new ScheduleView(
            month,
            header.Status,
            header.Revision,
            header.PublishedAt,
            month.DayCount,
            ctx.Staff.Count(s => s.Status == StaffStatus.Active),
            ctx.Areas,
            ctx.Duties
                .OrderBy(d => d.Date).ThenBy(d => d.AreaId, StringComparer.Ordinal)
                .Select(d => new DutyView(d.AreaId, d.Date, d.StaffId, CellKey.Area(d)))
                .ToArray());
    }

    public async Task<ValidationResult> ValidateAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var loaded = await RequireAsync(month, cancellationToken);
        return new ViolationChecker(loaded.Context).Check(loaded.Constraints);
    }

    /// <summary><c>GET /schedules/{ym}/violations</c>：可依嚴重度與日期篩。日期篩的是 cellKey 尾巴的日期。</summary>
    public async Task<IReadOnlyList<Violation>> ListViolationsAsync(
        YearMonth month, Severity? severity, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var result = await ValidateAsync(month, cancellationToken);
        IEnumerable<Violation> violations = result.Violations;
        if (severity is not null)
        {
            violations = violations.Where(v => v.Severity == severity);
        }

        if (date is not null)
        {
            var suffix = ":" + date.Value.ToString("yyyy-MM-dd");
            violations = violations.Where(v => v.CellKeys.Any(k => k.EndsWith(suffix, StringComparison.Ordinal)));
        }

        return violations.ToArray();
    }

    public async Task<PointBoard> GetPointBoardAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var loaded = await RequireAsync(month, cancellationToken);
        var ctx = loaded.Context;
        var metrics = new MetricEvaluator(ctx);

        var rowsByGroup = ctx.Staff
            .Where(s => s.Status == StaffStatus.Active)
            .Select(s => (Group: ctx.RankOf(s.RankCode).GroupCode, Row: RowOf(ctx, metrics, s)))
            .ToLookup(x => x.Group, x => x.Row);

        var groups = loaded.RankGroups
            .Where(g => rowsByGroup.Contains(g.Code))
            .Select(g => new PointBoardGroup(g.Code, g.Name, rowsByGroup[g.Code].ToArray()))
            .ToArray();
        return new PointBoard(groups);
    }

    public async Task<DayDetail> GetDayDetailAsync(YearMonth month, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!month.Contains(date))
        {
            throw SchedulerException.NotFound($"{date:yyyy-MM-dd} 不在 {month} 裡");
        }

        var loaded = await RequireAsync(month, cancellationToken);
        var ctx = loaded.Context;
        var metrics = new MetricEvaluator(ctx);
        var day = ctx.Calendar[date];
        var byArea = ctx.Duties.Where(d => d.Date == date).ToDictionary(d => d.AreaId);

        var slots = ctx.Areas.Select(area =>
        {
            var staff = byArea.TryGetValue(area.Id, out var duty) ? ctx.StaffOf(duty.StaffId) : null;
            return new DayAreaSlot(
                area.Id,
                area.Code,
                area.AreaTypeCode,
                Filled: staff is not null,
                staff is null
                    ? null
                    : new DayAreaStaff(
                        staff.Id,
                        staff.Name,
                        staff.RankCode,
                        metrics.QuotaPoints(staff.Id),
                        ctx.QuotaCapOf(ctx.RankOf(staff.RankCode)),
                        HolidayDuties(ctx, staff.Id)));
        }).ToArray();

        return new DayDetail(date, day.IsHoliday, day.IsPublicHoliday, ctx.PointRules.Quota.ValueOf(day), slots);
    }

    public async Task<VacancyReport> ListVacanciesAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var loaded = await RequireAsync(month, cancellationToken);
        var ctx = loaded.Context;
        var filled = ctx.Duties.GroupBy(d => (d.AreaId, d.Date)).ToDictionary(g => g.Key, g => g.Count());

        // 缺額看 RequiredPerDay（出廠值全是 1），與 H1 覆蓋違規的算法一致
        var byDate = new List<VacancyByDate>();
        foreach (var date in month.Days())
        {
            var shortfalls = ctx.Areas
                .Select(a => (a.Id, Missing: a.RequiredPerDay - filled.GetValueOrDefault((a.Id, date))))
                .Where(x => x.Missing > 0)
                .ToArray();
            if (shortfalls.Length > 0)
            {
                byDate.Add(new VacancyByDate(date, shortfalls.Select(x => x.Id).ToArray(), shortfalls.Sum(x => x.Missing)));
            }
        }

        return new VacancyReport(byDate.Sum(v => v.Count), byDate);
    }

    /// <summary>
    /// 某一格的候選人：在職且有資格的人全列，阻擋理由來自「把他放進這格後多出來的硬違規」，
    /// 警示來自多出來的軟違規。不重寫 H3／H4／H6 的判斷，直接跑 <see cref="ViolationChecker"/>。
    /// 同日已排在其他區域是結構不變式，不在約束裡，要先擋，否則 context 組不起來。
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> ListCandidatesAsync(
        YearMonth month, string areaId, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!month.Contains(date))
        {
            throw SchedulerException.NotFound($"{date:yyyy-MM-dd} 不在 {month} 裡");
        }

        var loaded = await RequireAsync(month, cancellationToken);
        var ctx = loaded.Context;
        var area = ctx.Areas.FirstOrDefault(a => a.Id == areaId)
            ?? throw SchedulerException.NotFound($"找不到區域 {areaId}");

        // 基準：這一格先清空。候選人是「換成他」的意思，現任者也照樣評估。
        var baseline = WithDuties(ctx, ctx.Duties.Where(d => !(d.AreaId == areaId && d.Date == date)));
        var baselineIds = new ViolationChecker(baseline).Check(loaded.Constraints).Violations.Select(v => v.Id).ToHashSet();
        var onDutyElsewhere = baseline.Duties.Where(d => d.Date == date).Select(d => d.StaffId).ToHashSet();
        var metrics = new MetricEvaluator(ctx);

        var candidates = new List<Candidate>();
        foreach (var staff in ctx.Staff.Where(s => s.Status == StaffStatus.Active))
        {
            if (!ctx.Eligibility.IsEligible(staff.RankCode, area.AreaTypeCode))
            {
                continue;
            }

            var blocking = new List<string>();
            var warnings = new List<string>();
            if (onDutyElsewhere.Contains(staff.Id))
            {
                blocking.Add("當日已排在其他區域");
            }
            else
            {
                var hypothetical = WithDuties(baseline, baseline.Duties.Append(new Duty(areaId, date, staff.Id)));
                var added = new ViolationChecker(hypothetical).Check(loaded.Constraints).Violations
                    .Where(v => !baselineIds.Contains(v.Id));
                foreach (var v in added)
                {
                    (v.Severity == Severity.Hard ? blocking : warnings).Add(v.Message);
                }
            }

            var own = ctx.DutiesOf(staff.Id).ToArray();
            var cap = ctx.QuotaCapOf(ctx.RankOf(staff.RankCode));
            candidates.Add(new Candidate(
                staff.Id,
                staff.Name,
                staff.RankCode,
                cap is null ? null : cap - metrics.QuotaPoints(staff.Id),
                own.Length == 0 ? 0 : (double)own.Count(d => d.AreaId == areaId) / own.Length,
                blocking,
                warnings));
        }

        return candidates
            .OrderBy(c => c.BlockingReasons.Count)
            .ThenByDescending(c => c.QuotaRemaining ?? -1)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- helpers ----

    private async Task<LoadedContext> RequireAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        return loaded.ScheduleExists ? loaded : throw SchedulerException.ScheduleNotFound(month);
    }

    // 看板與候選人的「剩餘額度」是 cap − 已排點數（契約 PointBoardRow.quotaRemaining）。
    // MetricEvaluator.QuotaRemaining 另外扣了月結轉，那是公平性比較用的，不是這裡要顯示的數。
    private static PointBoardRow RowOf(SchedulingContext ctx, MetricEvaluator metrics, Staff staff)
    {
        var quotaPoints = metrics.QuotaPoints(staff.Id);
        var cap = ctx.QuotaCapOf(ctx.RankOf(staff.RankCode));
        return new PointBoardRow(
            staff.Id,
            staff.Name,
            staff.RankCode,
            quotaPoints,
            cap,
            cap is null ? null : cap - quotaPoints,
            ctx.CarryOverOf(staff.Id),
            metrics.FairnessPoints(staff.Id),
            ctx.DutiesOf(staff.Id).Count(),
            HolidayDuties(ctx, staff.Id));
    }

    private static int HolidayDuties(SchedulingContext ctx, string staffId) =>
        ctx.DutiesOf(staffId).Count(d => ctx.Calendar[d.Date].IsHoliday);

    /// <summary>
    /// 換一份值班清單的 context。不能用 <c>with</c>：record 複製會把 lazy 快取一起帶走，
    /// 舊清單的索引就黏在新 context 上。
    /// </summary>
    private static SchedulingContext WithDuties(SchedulingContext ctx, IEnumerable<Duty> duties) => new(
        ctx.Month,
        ctx.Calendar,
        ctx.Areas,
        ctx.Staff,
        ctx.Ranks,
        ctx.Eligibility,
        ctx.PointRules,
        ctx.Override,
        duties.ToArray(),
        ctx.PreviousMonthDuties,
        ctx.BlockedDays,
        ctx.CarryOver);
}
