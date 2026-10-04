using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.Errors;
using Scheduler.Application.People;
using Scheduler.Application.Schedules;
using Scheduler.Application.Settings;
using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Validation;

namespace Scheduler.Api.Contracts;

/// <summary>
/// Application 的讀取模型 → 契約 DTO。純形狀轉換，沒有任何業務判斷（硬性規則 #2）。
/// </summary>
internal static class ContractMapper
{
    public static ErrorResponseDto ToContract(this SchedulerException e) =>
        new(new ErrorBodyDto(ContractNames.Of(e.Code), e.Message, e.Details));

    // -- 值班表 -------------------------------------------------------------

    public static ScheduleListDto ToContract(this IReadOnlyList<ScheduleSummary> months) =>
        new(months.Select(m => new ScheduleSummaryDto(m.YearMonth.ToString(), ContractNames.Of(m.Status), m.Revision, m.PublishedVersion, m.EditedSincePublish, m.PublishedAt, m.HardViolationCount)).ToArray());

    public static ScheduleDto ToContract(this ScheduleView s) =>
        new(
            s.YearMonth.ToString(),
            ContractNames.Of(s.Status),
            s.Revision,
            s.PublishedVersion,
            s.EditedSincePublish,
            s.PublishedAt,
            s.DayCount,
            s.StaffCount,
            s.Areas.Select(ToContract).ToArray(),
            s.Duties.Select(d => new DutyDto(d.AreaId, d.Date, d.StaffId, d.CellKey)).ToArray());

    public static ValidationResultDto ToContract(this ValidationResult r) =>
        new(r.Ok, r.Violations.Select(ToContract).ToArray(), new ValidationSummaryDto(r.HardCount, r.SoftCount));

    public static ViolationListDto ToContract(this IReadOnlyList<Violation> violations) =>
        new(violations.Select(ToContract).ToArray());

    public static ViolationDto ToContract(this Violation v) =>
        new(v.Id, v.Code, ContractNames.Of(v.Severity), v.CellKeys, v.Message);

    public static MutationResultDto ToContract(this MutationResult r) =>
        new(
            r.Revision,
            r.Cells.Select(c => new MutatedCellDto(c.AreaId, c.Date, c.StaffId, c.CellKey)).ToArray(),
            r.Violations.Select(ToContract).ToArray());

    public static PublishResultDto ToContract(this PublishResult r) =>
        new(ContractNames.Of(r.Status), r.PublishedAt, r.Revision, r.PublishedVersion, r.CarryOver.Select(e => new CarryOverEntryDto(e.StaffId, e.Points)).ToArray());

    // -- 檢視 ---------------------------------------------------------------

    public static PointBoardDto ToContract(this PointBoard b) =>
        new(b.Groups.Select(g => new PointBoardGroupDto(
            g.GroupCode,
            g.GroupName,
            g.Rows.Select(r => new PointBoardRowDto(
                r.StaffId, r.Name, r.RankCode, r.QuotaPoints, r.QuotaCap, r.QuotaRemaining,
                r.CarryOverApplied, r.FairnessPoints, r.Duties, r.HolidayDuties)).ToArray())).ToArray());

    public static DayDetailDto ToContract(this DayDetail d) =>
        new(
            d.Date,
            d.IsHoliday,
            d.IsPublicHoliday,
            d.QuotaPointValue,
            d.Areas.Select(a => new DayAreaSlotDto(
                a.AreaId,
                a.Code,
                a.AreaTypeCode,
                a.Filled,
                a.Staff is null
                    ? null
                    : new DayAreaStaffDto(a.Staff.StaffId, a.Staff.Name, a.Staff.RankCode, a.Staff.MonthQuotaPoints, a.Staff.MonthQuotaCap, a.Staff.MonthHolidayDuties))).ToArray());

    public static VacancyReportDto ToContract(this VacancyReport r) =>
        new(r.Total, r.ByDate.Select(v => new VacancyByDateDto(v.Date, v.AreaIds, v.Count)).ToArray());

    public static CandidateListDto ToContract(this IReadOnlyList<Candidate> candidates) =>
        new(candidates.Select(c => new CandidateDto(
            c.StaffId, c.Name, c.RankCode, c.QuotaRemaining, c.AreaConsistency, c.BlockingReasons, c.Warnings)).ToArray());

    // -- 不可排班日 ---------------------------------------------------------

    public static BlockedDayRegistrationDto ToContract(this BlockedDayRegistration r) =>
        new(
            r.YearMonth.ToString(),
            r.MonthlyCap,
            r.Entries.Select(e => new BlockedDayEntryDto(e.StaffId, e.Date)).ToArray(),
            r.ByStaff.Select(s => new BlockedDayByStaffDto(s.StaffId, s.Count, s.Remaining)).ToArray(),
            r.ByDate.Select(d => new BlockedDayByDateDto(d.Date, d.Count)).ToArray());

    public static BlockedDayMutationResultDto ToContract(this BlockedDayMutation m) =>
        new(new BlockedDayStaffTotalsDto(m.StaffCount, m.StaffRemaining), new BlockedDayDateTotalsDto(m.DateCount));

    public static FeasibilityReportDto ToContract(this FeasibilityReport r) =>
        new(
            r.Feasible,
            r.ByDate.Select(d => new FeasibilityByDateDto(
                d.Date,
                d.Shortages.Select(s => new FeasibilityShortageDto(s.AreaTypeCode, s.Required, s.AvailableStaff)).ToArray())).ToArray(),
            r.BySupply.Select(t => new FeasibilityTierDto(t.AreaTypeCodes, t.DemandPoints, t.SupplyPoints, t.Headroom)).ToArray(),
            r.Warnings);

    // -- 設定 ---------------------------------------------------------------

    public static AreaSettingsDto ToContract(this AreaSettings s) =>
        new(s.AreaTypes.Select(t => new AreaTypeDto(t.Code, t.Name)).ToArray(), s.Areas.Select(ToContract).ToArray());

    public static AreaDto ToContract(this Area a) => new(a.Id, a.Code, a.Name, a.AreaTypeCode, a.RequiredPerDay);

    public static RankSettingsDto ToContract(this RankSettings s) =>
        new(
            s.Ranks.Select(r => new RankDto(r.Code, r.Name, r.GroupCode, r.QuotaCap, ContractNames.Of(r.PointType))).ToArray(),
            s.Groups.Select(g => new RankGroupDto(g.Code, g.Name)).ToArray());

    public static EligibilityMatrixDto ToContract(this EligibilityMatrix m) => new(m.Matrix);

    public static PointRulesDto ToContract(this PointRules p) =>
        new(
            new QuotaPointRuleDto(p.Quota.Weekday, p.Quota.Holiday),
            new FairnessPointRuleDto(
                p.Fairness.Tables.ToDictionary(
                    kv => ContractNames.Of(kv.Key)!,
                    kv => (IReadOnlyList<FairnessTableEntryDto>)kv.Value
                        .Select(e => new FairnessTableEntryDto(ContractNames.Of(e.Today), ContractNames.Of(e.Tomorrow), e.Points))
                        .ToArray()),
                new ConsecutiveSaturdayBonusDto(p.Fairness.ConsecutiveSaturdayBonus.Points, p.Fairness.ConsecutiveSaturdayBonus.WindowDays)));

    public static ConstraintSettingsDto ToContract(this ConstraintSettings s) =>
        new(
            s.Hard.Select(c => new HardConstraintDto(c.Code, c.Name, ContractNames.Of(c.Primitive), c.Enabled, ScopeOf(c.Scope), ContractNames.Of(c.Metric), ParamsOf(c.Params))).ToArray(),
            s.Soft.Select(c => new SoftConstraintDto(c.Code, c.Name, ContractNames.Of(c.Primitive), c.Weight, ScopeOf(c.Scope), ContractNames.Of(c.Metric), ParamsOf(c.Params))).ToArray());

    /// <summary>四個維度都不限時整個 <c>scope</c> 省略，與 mock fixture 一致。</summary>
    private static ConstraintScopeDto? ScopeOf(ConstraintScope scope)
    {
        var dto = new ConstraintScopeDto(
            Sorted(scope.RankCodes),
            Sorted(scope.ExemptRankCodes),
            Sorted(scope.AreaTypeCodes),
            scope.DayKinds is { Count: > 0 } kinds ? kinds.Select(ContractNames.Of).OrderBy(k => k, StringComparer.Ordinal).ToArray() : null);
        return dto.RankCodes is null && dto.ExemptRankCodes is null && dto.AreaTypeCodes is null && dto.DayKinds is null ? null : dto;
    }

    /// <summary>空集合與 null 同義（不限），都省略；Persistence 本來就拒收空集合，這裡只是多一道保險。</summary>
    private static IReadOnlyList<string>? Sorted(IReadOnlySet<string>? set) =>
        set is { Count: > 0 } ? set.OrderBy(s => s, StringComparer.Ordinal).ToArray() : null;

    private static ConstraintParamsDto? ParamsOf(ConstraintParams p) =>
        p.Days is null && p.Cap is null && p.Direction is null
            ? null
            : new ConstraintParamsDto(p.Days, p.Cap, ContractNames.Of(p.Direction));

    public static MonthlyOverrideDto ToContract(this MonthlyOverride o) => new(o.YearMonth.ToString(), o.QuotaCapByRank);

    // -- 行事曆 -------------------------------------------------------------

    public static CalendarDto ToContract(this CalendarYear c) =>
        new(c.Year, c.Days.Select(ToContract).ToArray());

    public static CalendarDayDto ToContract(this CalendarDayView d) =>
        new(
            d.Day.Date,
            (int)d.Day.Date.DayOfWeek,
            d.Day.IsHoliday,
            d.Day.IsPublicHoliday,
            d.Day.IsMakeUpWorkday,
            d.Day.HolidayName,
            d.QuotaPointValue,
            d.Overridden);

    // -- 人員 ---------------------------------------------------------------

    public static StaffListDto ToContract(this StaffList l) =>
        new(l.Items.Select(ToContract).ToArray(), new StaffCountsDto(l.Counts.Active, l.Counts.Inactive));

    public static StaffDto ToContract(this StaffView s) =>
        new(s.Id, s.EmployeeNo, s.Name, s.RankCode, ContractNames.Of(s.Status), s.EligibleAreaTypes);

    // -- 求解 -----------------------------------------------------------------

    public static SolverJobDto ToContract(this SolverJobView v)
    {
        var r = v.Record;
        return new SolverJobDto(
            r.JobId,
            r.YearMonth.ToString(),
            ContractNames.Of(r.Status),
            r.VariantCount,
            r.ElapsedSec ?? 0,
            r.Scale is null ? null : new SolverScaleDto(r.Scale.Staff, r.Scale.Areas, r.Scale.Days, r.Scale.Variables),
            r.ConstraintCount is null ? null : new ConstraintCountDto(r.ConstraintCount.Hard, r.ConstraintCount.Soft),
            v.Progress.ToContract(),
            r.Warnings,
            r.FailureReason);
    }

    public static SolverProgressDto ToContract(this SolverProgressSnapshot p) =>
        new(p.JobId, ContractNames.Of(p.Status), p.VariantIndex, p.VariantCount, p.ElapsedSec, p.TimeLimitSec, p.SolutionCount, p.BestObjective, p.BestBound, p.Gap);

    public static VariantListDto ToContract(this IReadOnlyList<VariantRecord> variants) =>
        new(variants.Select(ToContract).ToArray());

    public static VariantDto ToContract(this VariantRecord v) =>
        new(
            v.Id,
            v.Label,
            VariantProfiles.All.FirstOrDefault(p => p.Id == v.Id)?.Description ?? "",
            v.WeightProfile,
            new VariantMetricsDto(v.Metrics.Vacancies, v.Metrics.QuotaFairness, v.Metrics.AreaConsistency, v.Metrics.RankPreference, v.Metrics.FairnessPoint),
            v.HardViolationCount,
            v.SoftScore,
            v.Duties.Select(d => new DutyDto(d.AreaId, d.Date, d.StaffId, Domain.Scheduling.CellKey.Area(d))).ToArray());
}
