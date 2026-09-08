using System.Globalization;
using Scheduler.Api.Contracts;
using Scheduler.Application.Errors;
using Scheduler.Domain.Model;

namespace Scheduler.Api.Http;

/// <summary>
/// <see cref="SchedulerException"/> → 契約的 <c>ErrorResponse</c>。ErrorCode 對 HTTP 狀態碼的對應
/// 只在這裡（ARCHITECTURE §3.2 規則 2）。其他例外照 ASP.NET 預設處理（開發期看堆疊）。
/// </summary>
internal static class ErrorHandling
{
    public static IApplicationBuilder UseSchedulerErrors(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (SchedulerException e) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = ContractNames.StatusOf(e.Code);
                await context.Response.WriteAsJsonAsync(e.ToContract(), context.RequestAborted);
            }
        });
}

/// <summary>
/// 路徑與查詢參數的解析。Minimal API 內建的 binding 失敗會回沒有 <c>ErrorResponse</c> 形狀的 400，
/// 所以參數一律以字串接、在這裡解析，格式錯誤統一是 422 <c>INVALID_REQUEST</c>。
/// </summary>
internal static class Parse
{
    public static YearMonth YearMonth(string ym) =>
        Domain.Model.YearMonth.TryParse(ym, out var value)
            ? value
            : throw Invalid($"年月格式應為 YYYY-MM：{ym}");

    public static DateOnly Date(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : throw Invalid($"日期格式應為 YYYY-MM-DD：{date}");

    public static DateOnly? OptionalDate(string? date) => date is null ? null : Date(date);

    /// <summary><c>/calendars/{year}/{date}</c>：路徑上的年份與日期要一致。</summary>
    public static DateOnly DateInYear(int year, DateOnly date) =>
        date.Year == year ? date : throw new SchedulerException(ErrorCode.InvalidRequest, $"{date:yyyy-MM-dd} 不在 {year} 年");

    public static int Year(string year) =>
        int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 9999
            ? value
            : throw Invalid($"年份格式應為四位數：{year}");

    public static Domain.Constraints.Severity? OptionalSeverity(string? severity) =>
        severity is null
            ? null
            : ContractNames.ToSeverity(severity) ?? throw Invalid($"severity 只能是 hard 或 soft：{severity}");

    public static StaffStatus? OptionalStaffStatus(string? status) =>
        status is null
            ? null
            : ContractNames.ToStaffStatus(status) ?? throw Invalid($"status 只能是 active 或 inactive：{status}");

    /// <summary>匯出版面；沒給就是契約的預設 <c>area-by-day</c>。</summary>
    public static Application.Schedules.ExportLayout OptionalLayout(string? layout) => layout switch
    {
        null or "" or "area-by-day" => Application.Schedules.ExportLayout.AreaByDay,
        "day-by-staff" => Application.Schedules.ExportLayout.DayByStaff,
        _ => throw Invalid($"layout 只能是 area-by-day 或 day-by-staff：{layout}"),
    };

    public static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw Invalid($"缺少必要參數 {name}") : value;

    private static SchedulerException Invalid(string message) => new(ErrorCode.InvalidRequest, message);
}
