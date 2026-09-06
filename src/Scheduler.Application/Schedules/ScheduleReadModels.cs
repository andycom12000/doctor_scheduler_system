using Scheduler.Domain.Model;

namespace Scheduler.Application.Schedules;

// 讀取類回應的形狀，對齊 api-contract.yaml 的 schema，列舉維持 Domain 型別，字串化是 Api 的事。

/// <summary><c>GET /schedules</c> 的一列。</summary>
public sealed record ScheduleSummary(
    YearMonth YearMonth,
    ScheduleStatus Status,
    int Revision,
    DateTimeOffset? PublishedAt,
    int HardViolationCount);

/// <summary><c>GET /schedules/{ym}</c>。</summary>
public sealed record ScheduleView(
    YearMonth YearMonth,
    ScheduleStatus Status,
    int Revision,
    DateTimeOffset? PublishedAt,
    int DayCount,
    int StaffCount,
    IReadOnlyList<Area> Areas,
    IReadOnlyList<DutyView> Duties);

public sealed record DutyView(string AreaId, DateOnly Date, string StaffId, string CellKey);

/// <summary><c>GET /schedules/{ym}/point-board</c>。</summary>
public sealed record PointBoard(IReadOnlyList<PointBoardGroup> Groups);

public sealed record PointBoardGroup(string GroupCode, string GroupName, IReadOnlyList<PointBoardRow> Rows);

public sealed record PointBoardRow(
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

/// <summary><c>GET /schedules/{ym}/days/{date}</c>。</summary>
public sealed record DayDetail(
    DateOnly Date,
    bool IsHoliday,
    bool IsPublicHoliday,
    int QuotaPointValue,
    IReadOnlyList<DayAreaSlot> Areas);

public sealed record DayAreaSlot(string AreaId, string Code, string AreaTypeCode, bool Filled, DayAreaStaff? Staff);

public sealed record DayAreaStaff(
    string StaffId,
    string Name,
    string RankCode,
    int MonthQuotaPoints,
    int? MonthQuotaCap,
    int MonthHolidayDuties);

/// <summary><c>GET /schedules/{ym}/vacancies</c>。</summary>
public sealed record VacancyReport(int Total, IReadOnlyList<VacancyByDate> ByDate);

public sealed record VacancyByDate(DateOnly Date, IReadOnlyList<string> AreaIds, int Count);

/// <summary><c>GET /schedules/{ym}/candidates</c> 的一列。</summary>
public sealed record Candidate(
    string StaffId,
    string Name,
    string RankCode,
    int? QuotaRemaining,
    double AreaConsistency,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings);
