using Scheduler.Api.Contracts;
using Scheduler.Api.Http;
using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.People;
using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;

namespace Scheduler.Api.Endpoints;

/// <summary>
/// 讀取路徑與 validate 的端點。每個端點只做「解參數 → 呼叫 Application → 包回應」，
/// 業務判斷（404 的條件、篩選、排序）全部在 Application。
/// 尚未接的端點見 api-contract.yaml：寫入、匯出、求解工作。
/// </summary>
internal static class ReadEndpoints
{
    public static void MapReadEndpoints(this IEndpointRouteBuilder app)
    {
        // 存活檢查順便探一次 SQLite native 程式庫：portable 發佈最容易斷的那條路（ARCHITECTURE §9）
        app.MapGet("/api/health", async (IServiceProvider services, CancellationToken ct) =>
            new HealthStatusDto("ok", await Persistence.SchedulerDatabase.ProbeAsync(services, ct)));

        var schedules = app.MapGroup("/api/schedules");

        schedules.MapGet("/", async (ScheduleQueries q, CancellationToken ct) =>
            (await q.ListAsync(ct)).ToContract());

        schedules.MapGet("/{ym}", async (string ym, ScheduleQueries q, CancellationToken ct) =>
            (await q.GetAsync(Parse.YearMonth(ym), ct)).ToContract());

        schedules.MapPost("/{ym}/validate", async (string ym, ScheduleQueries q, CancellationToken ct) =>
            (await q.ValidateAsync(Parse.YearMonth(ym), ct)).ToContract());

        schedules.MapGet("/{ym}/violations", async (string ym, string? severity, string? date, ScheduleQueries q, CancellationToken ct) =>
            (await q.ListViolationsAsync(Parse.YearMonth(ym), Parse.OptionalSeverity(severity), Parse.OptionalDate(date), ct)).ToContract());

        schedules.MapGet("/{ym}/point-board", async (string ym, ScheduleQueries q, CancellationToken ct) =>
            (await q.GetPointBoardAsync(Parse.YearMonth(ym), ct)).ToContract());

        schedules.MapGet("/{ym}/days/{date}", async (string ym, string date, ScheduleQueries q, CancellationToken ct) =>
            (await q.GetDayDetailAsync(Parse.YearMonth(ym), Parse.Date(date), ct)).ToContract());

        schedules.MapGet("/{ym}/vacancies", async (string ym, ScheduleQueries q, CancellationToken ct) =>
            (await q.ListVacanciesAsync(Parse.YearMonth(ym), ct)).ToContract());

        schedules.MapGet("/{ym}/candidates", async (string ym, string? areaId, string? date, ScheduleQueries q, CancellationToken ct) =>
            (await q.ListCandidatesAsync(
                Parse.YearMonth(ym),
                Parse.Required(areaId, "areaId"),
                Parse.Date(Parse.Required(date, "date")),
                ct)).ToContract());

        var blockedDays = app.MapGroup("/api/blocked-days");

        blockedDays.MapGet("/{ym}", async (string ym, BlockedDayQueries q, CancellationToken ct) =>
            (await q.GetRegistrationAsync(Parse.YearMonth(ym), ct)).ToContract());

        blockedDays.MapGet("/{ym}/feasibility", async (string ym, BlockedDayQueries q, CancellationToken ct) =>
            (await q.GetFeasibilityAsync(Parse.YearMonth(ym), ct)).ToContract());

        // 設定類 GET 是 repository 直接回傳，沒有查詢類別（CLAUDE.md 現況）。
        var settings = app.MapGroup("/api/settings");

        settings.MapGet("/areas", async (ISettingsRepository r, CancellationToken ct) =>
            (await r.GetAreasAsync(ct)).ToContract());

        settings.MapGet("/ranks", async (ISettingsRepository r, CancellationToken ct) =>
            (await r.GetRanksAsync(ct)).ToContract());

        settings.MapGet("/eligibility-matrix", async (ISettingsRepository r, CancellationToken ct) =>
            (await r.GetEligibilityAsync(ct)).ToContract());

        settings.MapGet("/point-rules", async (ISettingsRepository r, CancellationToken ct) =>
            (await r.GetPointRulesAsync(ct)).ToContract());

        settings.MapGet("/constraints", async (ISettingsRepository r, CancellationToken ct) =>
            (await r.GetConstraintsAsync(ct)).ToContract());

        settings.MapGet("/monthly-overrides/{ym}", async (string ym, ISettingsRepository r, CancellationToken ct) =>
            (await r.GetMonthlyOverrideAsync(Parse.YearMonth(ym), ct)).ToContract());

        app.MapGet("/api/calendars/{year}", async (string year, CalendarQueries q, CancellationToken ct) =>
            (await q.GetYearAsync(Parse.Year(year), ct)).ToContract());

        app.MapGet("/api/staff", async (string? status, StaffQueries q, CancellationToken ct) =>
            (await q.ListAsync(Parse.OptionalStaffStatus(status), ct)).ToContract());
    }
}
