using Scheduler.Application.Schedules;
using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Mapping;

/// <summary>
/// 列舉在資料庫裡的字串形式。**與契約的字串一致**（<c>draft</c>、<c>quota_point</c>、<c>ExactCount</c>……），
/// 資料庫拿出來就能對外，也不綁死在 .NET 的列舉名稱。
/// 讀到不認得的字串直接擲例外——寧可失敗，不要悄悄變成預設值。
/// </summary>
internal static class EnumNames
{
    public static string Of(ScheduleStatus v) => v switch
    {
        ScheduleStatus.Draft => "draft",
        ScheduleStatus.Published => "published",
        _ => throw Unknown(v),
    };

    public static ScheduleStatus ToScheduleStatus(string s) => s switch
    {
        "draft" => ScheduleStatus.Draft,
        "published" => ScheduleStatus.Published,
        _ => throw Unknown<ScheduleStatus>(s),
    };

    public static string Of(StaffStatus v) => v switch
    {
        StaffStatus.Active => "active",
        StaffStatus.Inactive => "inactive",
        _ => throw Unknown(v),
    };

    public static StaffStatus ToStaffStatus(string s) => s switch
    {
        "active" => StaffStatus.Active,
        "inactive" => StaffStatus.Inactive,
        _ => throw Unknown<StaffStatus>(s),
    };

    public static string? Of(PointType? v) => v switch
    {
        null => null,
        PointType.A => "A",
        PointType.B => "B",
        _ => throw Unknown(v.Value),
    };

    public static PointType? ToPointType(string? s) => s switch
    {
        null => null,
        "A" => PointType.A,
        "B" => PointType.B,
        _ => throw Unknown<PointType>(s),
    };

    public static string Of(DayKind v) => v switch
    {
        DayKind.Weekday => "weekday",
        DayKind.Holiday => "holiday",
        DayKind.PublicHoliday => "publicHoliday",
        _ => throw Unknown(v),
    };

    public static DayKind ToDayKind(string s) => s switch
    {
        "weekday" => DayKind.Weekday,
        "holiday" => DayKind.Holiday,
        "publicHoliday" => DayKind.PublicHoliday,
        _ => throw Unknown<DayKind>(s),
    };

    public static string Of(Primitive v) => Enum.IsDefined(v) ? v.ToString() : throw Unknown(v);

    public static Primitive ToPrimitive(string s) =>
        Enum.TryParse<Primitive>(s, ignoreCase: false, out var v) && Enum.IsDefined(v) ? v : throw Unknown<Primitive>(s);

    public static string Of(Severity v) => v switch
    {
        Severity.Hard => "hard",
        Severity.Soft => "soft",
        _ => throw Unknown(v),
    };

    public static Severity ToSeverity(string s) => s switch
    {
        "hard" => Severity.Hard,
        "soft" => Severity.Soft,
        _ => throw Unknown<Severity>(s),
    };

    public static string? Of(Metric? v) => v switch
    {
        null => null,
        Metric.QuotaPoint => "quota_point",
        Metric.FairnessPoint => "fairness_point",
        Metric.DutyDay => "duty_day",
        _ => throw Unknown(v.Value),
    };

    public static Metric? ToMetric(string? s) => s switch
    {
        null => null,
        "quota_point" => Metric.QuotaPoint,
        "fairness_point" => Metric.FairnessPoint,
        "duty_day" => Metric.DutyDay,
        _ => throw Unknown<Metric>(s),
    };

    public static string? Of(PreferenceDirection? v) => v switch
    {
        null => null,
        PreferenceDirection.Prefer => "prefer",
        PreferenceDirection.Avoid => "avoid",
        _ => throw Unknown(v.Value),
    };

    public static PreferenceDirection? ToDirection(string? s) => s switch
    {
        null => null,
        "prefer" => PreferenceDirection.Prefer,
        "avoid" => PreferenceDirection.Avoid,
        _ => throw Unknown<PreferenceDirection>(s),
    };

    public static string Of(SolverJobStatus v) => v switch
    {
        SolverJobStatus.Queued => "queued",
        SolverJobStatus.Running => "running",
        SolverJobStatus.Succeeded => "succeeded",
        SolverJobStatus.Failed => "failed",
        SolverJobStatus.Cancelled => "cancelled",
        _ => throw Unknown(v),
    };

    public static SolverJobStatus ToSolverJobStatus(string s) => s switch
    {
        "queued" => SolverJobStatus.Queued,
        "running" => SolverJobStatus.Running,
        "succeeded" => SolverJobStatus.Succeeded,
        "failed" => SolverJobStatus.Failed,
        "cancelled" => SolverJobStatus.Cancelled,
        _ => throw Unknown<SolverJobStatus>(s),
    };

    private static InvalidDataException Unknown<T>(string s) =>
        new($"資料庫裡的 {typeof(T).Name} 值不認得：{s}");

    private static ArgumentOutOfRangeException Unknown<T>(T v) where T : struct, Enum =>
        new(nameof(v), v, $"{typeof(T).Name} 沒有對應的字串");
}
