using Scheduler.Api.CalendarSync;
using Scheduler.Api.Contracts;
using Scheduler.Api.Http;
using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.Calendars.Sync;
using Scheduler.Application.People;
using Scheduler.Application.Schedules;
using Scheduler.Application.Settings;

namespace Scheduler.Api.Endpoints;

/// <summary>
/// 寫入路徑的端點。與讀取端點同一條規矩：解參數與本體 → 呼叫 Application → 包回應，
/// 結構不變式、被引用檢查、月結轉結算都在 Application。
/// 求解工作與套用變體在 SolverEndpoints；匯出是唯讀 GET，在 ReadEndpoints。
/// </summary>
internal static class WriteEndpoints
{
    public static void MapWriteEndpoints(this IEndpointRouteBuilder app)
    {
        var schedules = app.MapGroup("/api/schedules");

        schedules.MapPatch("/{ym}/duties", async (string ym, HttpContext http, ScheduleCommands c, CancellationToken ct) =>
        {
            var (cell, staffId) = (await RequestBody.ReadAsync<SetDutyRequestDto>(http)).ToCommand();
            return (await c.SetDutyAsync(Parse.YearMonth(ym), cell, staffId, ct)).ToContract();
        });

        schedules.MapPost("/{ym}/duties/swap", async (string ym, HttpContext http, ScheduleCommands c, CancellationToken ct) =>
        {
            var (a, b) = (await RequestBody.ReadAsync<SwapDutiesRequestDto>(http)).ToCommand();
            return (await c.SwapAsync(Parse.YearMonth(ym), a, b, ct)).ToContract();
        });

        schedules.MapPost("/{ym}/publish", async (string ym, HttpContext http, ScheduleCommands c, CancellationToken ct) =>
        {
            var body = await RequestBody.ReadOptionalAsync<PublishRequestDto>(http);
            return (await c.PublishAsync(Parse.YearMonth(ym), body?.AcknowledgeViolations ?? false, ct)).ToContract();
        });

        var blockedDays = app.MapGroup("/api/blocked-days");

        blockedDays.MapPut("/{ym}/{staffId}/{date}", async (string ym, string staffId, string date, BlockedDayCommands c, CancellationToken ct) =>
            (await c.SetAsync(Parse.YearMonth(ym), staffId, Parse.Date(date), ct)).ToContract());

        blockedDays.MapDelete("/{ym}/{staffId}/{date}", async (string ym, string staffId, string date, BlockedDayCommands c, CancellationToken ct) =>
            (await c.ClearAsync(Parse.YearMonth(ym), staffId, Parse.Date(date), ct)).ToContract());

        var settings = app.MapGroup("/api/settings");

        settings.MapPut("/areas", async (HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplaceAreasAsync((await RequestBody.ReadAsync<AreaSettingsDto>(http)).ToDomain(), ct)).ToContract());

        settings.MapPut("/ranks", async (HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplaceRanksAsync((await RequestBody.ReadAsync<RankSettingsDto>(http)).ToDomain(), ct)).ToContract());

        settings.MapPut("/eligibility-matrix", async (HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplaceEligibilityAsync((await RequestBody.ReadAsync<EligibilityMatrixDto>(http)).ToDomain(), ct)).ToContract());

        settings.MapPut("/point-rules", async (HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplacePointRulesAsync((await RequestBody.ReadAsync<PointRulesDto>(http)).ToDomain(), ct)).ToContract());

        settings.MapPut("/constraints", async (HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplaceConstraintsAsync((await RequestBody.ReadAsync<ConstraintSettingsDto>(http)).ToDomain(), ct)).ToContract());

        settings.MapPut("/monthly-overrides/{ym}", async (string ym, HttpContext http, SettingsCommands c, CancellationToken ct) =>
            (await c.ReplaceMonthlyOverrideAsync(Parse.YearMonth(ym), (await RequestBody.ReadAsync<MonthlyOverrideDto>(http)).ToQuotaCapByRank(), ct)).ToContract());

        // 重試行事曆自動更新。冪等：沒開或已在跑就不另起一輪，一律回目前狀態。
        app.MapPost("/api/calendars/sync", async (CalendarSyncRunner runner, CalendarSyncQueries q, CancellationToken ct) =>
        {
            runner.StartIfIdle();
            return (await q.GetStatusAsync(ct)).ToContract();
        });

        app.MapPatch("/api/calendars/{year}/{date}", async (string year, string date, HttpContext http, CalendarCommands c, CancellationToken ct) =>
        {
            var day = Parse.DateInYear(Parse.Year(year), Parse.Date(date));
            var patch = (await RequestBody.ReadObjectAsync(http)).ToCalendarPatch();
            return (await c.OverrideDayAsync(day, patch, ct)).ToContract();
        });

        var staff = app.MapGroup("/api/staff");

        staff.MapPost("/", async (HttpContext http, StaffCommands c, CancellationToken ct) =>
        {
            var created = await c.CreateAsync((await RequestBody.ReadAsync<StaffWriteDto>(http)).ToCommand(), ct);
            return Results.Created($"/api/staff/{created.Id}", created.ToContract());
        });

        staff.MapPatch("/{id}", async (string id, HttpContext http, StaffCommands c, CancellationToken ct) =>
            (await c.UpdateAsync(id, (await RequestBody.ReadAsync<StaffWriteDto>(http)).ToCommand(), ct)).ToContract());

        staff.MapDelete("/{id}", async (string id, StaffCommands c, CancellationToken ct) =>
        {
            await c.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        staff.MapPatch("/{id}/status", async (string id, HttpContext http, StaffCommands c, CancellationToken ct) =>
            (await c.SetStatusAsync(id, (await RequestBody.ReadAsync<StaffStatusRequestDto>(http)).ToStatus(), ct)).ToContract());
    }
}
