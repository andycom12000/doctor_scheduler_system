using Scheduler.Application.Errors;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Api.Contracts;

/// <summary>
/// 列舉在契約上的字串。Persistence 有一份長得一樣的（資料庫字串刻意與契約一致），
/// 但那是 internal 且屬於另一層，這裡不借用。兩邊若漂移，契約守法測試會抓到。
/// 查詢參數的解析（字串 → 列舉）也在這裡，不認得的值回 null，由端點回 422。
/// </summary>
internal static class ContractNames
{
    public static string Of(ScheduleStatus v) => v switch
    {
        ScheduleStatus.Draft => "draft",
        ScheduleStatus.Published => "published",
        _ => throw Unknown(v),
    };

    public static string Of(StaffStatus v) => v switch
    {
        StaffStatus.Active => "active",
        StaffStatus.Inactive => "inactive",
        _ => throw Unknown(v),
    };

    public static StaffStatus? ToStaffStatus(string s) => s switch
    {
        "active" => StaffStatus.Active,
        "inactive" => StaffStatus.Inactive,
        _ => null,
    };

    public static string Of(Severity v) => v switch
    {
        Severity.Hard => "hard",
        Severity.Soft => "soft",
        _ => throw Unknown(v),
    };

    public static Severity? ToSeverity(string s) => s switch
    {
        "hard" => Severity.Hard,
        "soft" => Severity.Soft,
        _ => null,
    };

    public static string? Of(PointType? v) => v switch
    {
        null => null,
        PointType.A => "A",
        PointType.B => "B",
        _ => throw Unknown(v.Value),
    };

    public static string Of(DayKind v) => v switch
    {
        DayKind.Weekday => "weekday",
        DayKind.Holiday => "holiday",
        DayKind.PublicHoliday => "publicHoliday",
        _ => throw Unknown(v),
    };

    public static string Of(Primitive v) => Enum.IsDefined(v) ? v.ToString() : throw Unknown(v);

    public static string? Of(Metric? v) => v switch
    {
        null => null,
        Metric.QuotaPoint => "quota_point",
        Metric.FairnessPoint => "fairness_point",
        Metric.DutyDay => "duty_day",
        _ => throw Unknown(v.Value),
    };

    public static string? Of(PreferenceDirection? v) => v switch
    {
        null => null,
        PreferenceDirection.Prefer => "prefer",
        PreferenceDirection.Avoid => "avoid",
        _ => throw Unknown(v.Value),
    };

    public static string Of(ErrorCode v) => v switch
    {
        ErrorCode.NotFound => "NOT_FOUND",
        ErrorCode.InvalidRequest => "INVALID_REQUEST",
        ErrorCode.BlockedDayCapExceeded => "BLOCKED_DAY_CAP_EXCEEDED",
        ErrorCode.HardViolationsPresent => "HARD_VIOLATIONS_PRESENT",
        ErrorCode.ScheduleAlreadyPublished => "SCHEDULE_ALREADY_PUBLISHED",
        ErrorCode.StaffAlreadyOnDuty => "STAFF_ALREADY_ON_DUTY",
        ErrorCode.AreaInUse => "AREA_IN_USE",
        ErrorCode.AreaTypeInUse => "AREA_TYPE_IN_USE",
        ErrorCode.RankInUse => "RANK_IN_USE",
        ErrorCode.EmployeeNoTaken => "EMPLOYEE_NO_TAKEN",
        ErrorCode.StaffHasDuties => "STAFF_HAS_DUTIES",
        ErrorCode.SolverBusy => "SOLVER_BUSY",
        ErrorCode.SolverFailed => "SOLVER_FAILED",
        _ => throw Unknown(v),
    };

    // ---- 寫入本體的反向解析：不認得的值回 null，由 RequestMapper 回 422 ----

    public static PointType? ToPointType(string s) => s switch
    {
        "A" => PointType.A,
        "B" => PointType.B,
        _ => null,
    };

    public static DayKind? ToDayKind(string s) => s switch
    {
        "weekday" => DayKind.Weekday,
        "holiday" => DayKind.Holiday,
        "publicHoliday" => DayKind.PublicHoliday,
        _ => null,
    };

    public static Primitive? ToPrimitive(string s) =>
        Enum.TryParse<Primitive>(s, ignoreCase: false, out var v) && Enum.IsDefined(v) ? v : null;

    public static Metric? ToMetric(string s) => s switch
    {
        "quota_point" => Metric.QuotaPoint,
        "fairness_point" => Metric.FairnessPoint,
        "duty_day" => Metric.DutyDay,
        _ => null,
    };

    public static PreferenceDirection? ToPreferenceDirection(string s) => s switch
    {
        "prefer" => PreferenceDirection.Prefer,
        "avoid" => PreferenceDirection.Avoid,
        _ => null,
    };

    /// <summary>契約沒有任何 5xx；SolverFailed 沒有契約上的狀態碼，只能是 500。</summary>
    public static int StatusOf(ErrorCode v) => v switch
    {
        ErrorCode.NotFound => StatusCodes.Status404NotFound,
        ErrorCode.InvalidRequest => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.BlockedDayCapExceeded => StatusCodes.Status409Conflict,
        ErrorCode.HardViolationsPresent => StatusCodes.Status409Conflict,
        ErrorCode.ScheduleAlreadyPublished => StatusCodes.Status409Conflict,
        ErrorCode.StaffAlreadyOnDuty => StatusCodes.Status409Conflict,
        ErrorCode.AreaInUse => StatusCodes.Status409Conflict,
        ErrorCode.AreaTypeInUse => StatusCodes.Status409Conflict,
        ErrorCode.RankInUse => StatusCodes.Status409Conflict,
        ErrorCode.EmployeeNoTaken => StatusCodes.Status409Conflict,
        ErrorCode.StaffHasDuties => StatusCodes.Status409Conflict,
        ErrorCode.SolverBusy => StatusCodes.Status409Conflict,
        ErrorCode.SolverFailed => StatusCodes.Status500InternalServerError,
        _ => throw Unknown(v),
    };

    private static ArgumentOutOfRangeException Unknown<T>(T v) where T : struct, Enum =>
        new(nameof(v), v, $"{typeof(T).Name} 沒有對應的契約字串");
}
