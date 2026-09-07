using System.Text.Json.Serialization;

namespace Scheduler.Api.Contracts;

// api-contract.yaml 的 schema，手寫（ARCHITECTURE §7：DTO 不從 yaml 生成）。
// 列舉一律已經是契約字串；Domain 型別 → 這裡的轉換在 ContractMapper。
// 契約標 [X, null] 且 required 的欄位要輸出 null；標 optional 的欄位用 WhenWritingNull 省略，
// 與 frontend/src/mocks 的輸出對齊，契約守法測試會逐一驗。

public sealed record HealthStatusDto(string Status);

public sealed record ErrorResponseDto(ErrorBodyDto Error);

public sealed record ErrorBodyDto(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object?>? Details);

// -- 值班表 -------------------------------------------------------------

public sealed record ScheduleListDto(IReadOnlyList<ScheduleSummaryDto> Months);

public sealed record ScheduleSummaryDto(string YearMonth, string Status, int Revision, DateTimeOffset? PublishedAt, int HardViolationCount);

public sealed record ScheduleDto(
    string YearMonth,
    string Status,
    int Revision,
    DateTimeOffset? PublishedAt,
    int DayCount,
    int StaffCount,
    IReadOnlyList<AreaDto> Areas,
    IReadOnlyList<DutyDto> Duties);

public sealed record DutyDto(string AreaId, DateOnly Date, string StaffId, string CellKey);

public sealed record ValidationResultDto(bool Ok, IReadOnlyList<ViolationDto> Violations, ValidationSummaryDto Summary);

public sealed record ValidationSummaryDto(int Hard, int Soft);

public sealed record ViolationListDto(IReadOnlyList<ViolationDto> Violations);

public sealed record ViolationDto(string Id, string Code, string Severity, IReadOnlyList<string> CellKeys, string Message);

// -- 檢視 ---------------------------------------------------------------

public sealed record PointBoardDto(IReadOnlyList<PointBoardGroupDto> Groups);

public sealed record PointBoardGroupDto(string GroupCode, string GroupName, IReadOnlyList<PointBoardRowDto> Rows);

public sealed record PointBoardRowDto(
    string StaffId,
    string Name,
    string RankCode,
    int QuotaPoints,
    int? QuotaCap,
    int? QuotaRemaining,
    int CarryOverApplied,
    int? FairnessPoints,
    int Duties,
    int HolidayDuties);

public sealed record DayDetailDto(DateOnly Date, bool IsHoliday, bool IsPublicHoliday, int QuotaPointValue, IReadOnlyList<DayAreaSlotDto> Areas);

public sealed record DayAreaSlotDto(string AreaId, string Code, string AreaTypeCode, bool Filled, DayAreaStaffDto? Staff);

public sealed record DayAreaStaffDto(string StaffId, string Name, string RankCode, int MonthQuotaPoints, int? MonthQuotaCap, int MonthHolidayDuties);

public sealed record VacancyReportDto(int Total, IReadOnlyList<VacancyByDateDto> ByDate);

public sealed record VacancyByDateDto(DateOnly Date, IReadOnlyList<string> AreaIds, int Count);

public sealed record CandidateListDto(IReadOnlyList<CandidateDto> Candidates);

public sealed record CandidateDto(
    string StaffId,
    string Name,
    string RankCode,
    int? QuotaRemaining,
    double AreaConsistency,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings);

// -- 不可排班日 ---------------------------------------------------------

public sealed record BlockedDayRegistrationDto(
    string YearMonth,
    int MonthlyCap,
    IReadOnlyList<BlockedDayEntryDto> Entries,
    IReadOnlyList<BlockedDayByStaffDto> ByStaff,
    IReadOnlyList<BlockedDayByDateDto> ByDate);

public sealed record BlockedDayEntryDto(string StaffId, DateOnly Date);

public sealed record BlockedDayByStaffDto(string StaffId, int Count, int Remaining);

public sealed record BlockedDayByDateDto(DateOnly Date, int Count);

public sealed record FeasibilityReportDto(
    bool Feasible,
    IReadOnlyList<FeasibilityByDateDto> ByDate,
    IReadOnlyList<FeasibilityTierDto> BySupply,
    IReadOnlyList<string> Warnings);

public sealed record FeasibilityByDateDto(DateOnly Date, IReadOnlyList<FeasibilityShortageDto> Shortages);

public sealed record FeasibilityShortageDto(string AreaTypeCode, int Required, int AvailableStaff);

public sealed record FeasibilityTierDto(IReadOnlyList<string> AreaTypeCodes, int DemandPoints, int SupplyPoints, int Headroom);

// -- 設定 ---------------------------------------------------------------

public sealed record AreaSettingsDto(IReadOnlyList<AreaTypeDto> AreaTypes, IReadOnlyList<AreaDto> Areas);

public sealed record AreaTypeDto(string Code, string Name);

public sealed record AreaDto(string Id, string Code, string Name, string AreaTypeCode, int RequiredPerDay);

public sealed record RankSettingsDto(IReadOnlyList<RankDto> Ranks, IReadOnlyList<RankGroupDto> Groups);

public sealed record RankDto(string Code, string Name, string GroupCode, int? QuotaCap, string? PointType);

public sealed record RankGroupDto(string Code, string Name);

public sealed record EligibilityMatrixDto(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Matrix);

public sealed record PointRulesDto(QuotaPointRuleDto Quota, FairnessPointRuleDto Fairness);

public sealed record QuotaPointRuleDto(int Weekday, int Holiday);

public sealed record FairnessPointRuleDto(
    IReadOnlyDictionary<string, IReadOnlyList<FairnessTableEntryDto>> Tables,
    ConsecutiveSaturdayBonusDto ConsecutiveSaturdayBonus);

public sealed record FairnessTableEntryDto(string Today, string Tomorrow, int Points);

public sealed record ConsecutiveSaturdayBonusDto(int Points, int WindowDays);

public sealed record ConstraintSettingsDto(IReadOnlyList<HardConstraintDto> Hard, IReadOnlyList<SoftConstraintDto> Soft);

public sealed record HardConstraintDto(
    string Code,
    string Name,
    string Primitive,
    bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstraintScopeDto? Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Metric,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstraintParamsDto? Params);

public sealed record SoftConstraintDto(
    string Code,
    string Name,
    string Primitive,
    int Weight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstraintScopeDto? Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Metric,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstraintParamsDto? Params);

public sealed record ConstraintScopeDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RankCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ExemptRankCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? AreaTypeCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? DayKinds);

public sealed record ConstraintParamsDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Days,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Cap,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Direction);

public sealed record MonthlyOverrideDto(string YearMonth, IReadOnlyDictionary<string, int> QuotaCapByRank);

// -- 行事曆 -------------------------------------------------------------

public sealed record CalendarDto(int Year, IReadOnlyList<CalendarDayDto> Days);

public sealed record CalendarDayDto(
    DateOnly Date,
    int Weekday,
    bool IsHoliday,
    bool IsPublicHoliday,
    bool IsMakeUpWorkday,
    string? HolidayName,
    int QuotaPointValue,
    bool Overridden);

// -- 人員 ---------------------------------------------------------------

public sealed record StaffListDto(IReadOnlyList<StaffDto> Items, StaffCountsDto Counts);

public sealed record StaffCountsDto(int Active, int Inactive);

public sealed record StaffDto(string Id, string EmployeeNo, string Name, string RankCode, string Status, IReadOnlyList<string> EligibleAreaTypes);
