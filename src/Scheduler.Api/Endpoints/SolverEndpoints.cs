using Scheduler.Api.Contracts;
using Scheduler.Api.Http;
using Scheduler.Api.Solving;
using Scheduler.Application.Schedules;
using Scheduler.Application.Solving;

namespace Scheduler.Api.Endpoints;

/// <summary>
/// 求解工作與變體的端點。迴圈與狀態機在 Application 的 <see cref="SolverJobService"/>，這裡只做
/// 解參數 → 呼叫 → 包回應；SSE 端點是開發期 transport（§4.7），正式版由 Shell 從同一個
/// <see cref="ISolverProgressFeed"/> 拿事件再 PostWebMessageAsJson，收到的 JSON 一模一樣。
/// </summary>
internal static class SolverEndpoints
{
    public static void MapSolverEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/solver-jobs");

        jobs.MapPost("/", async (HttpContext http, SolverJobService s, CancellationToken ct) =>
        {
            var (month, variantCount, timeLimit) = (await RequestBody.ReadAsync<CreateSolverJobRequestDto>(http)).ToCommand();
            var job = await s.CreateAsync(month, variantCount, timeLimit, ct);
            return Results.Accepted($"/api/solver-jobs/{job.Record.JobId}", job.ToContract());
        });

        jobs.MapGet("/{jobId}", async (string jobId, SolverJobService s, CancellationToken ct) =>
            (await s.GetAsync(jobId, ct)).ToContract());

        jobs.MapDelete("/{jobId}", async (string jobId, SolverJobService s, CancellationToken ct) =>
            (await s.CancelAsync(jobId, ct)).ToContract());

        jobs.MapGet("/{jobId}/variants", async (string jobId, SolverJobService s, CancellationToken ct) =>
            (await s.ListVariantsAsync(jobId, ct)).ToContract());

        jobs.MapGet("/{jobId}/stream", async (string jobId, HttpContext http, SolverJobService s, ISolverProgressFeed feed, CancellationToken ct) =>
        {
            // 先確認工作存在：404 要在回應開始之前擲出，錯誤 middleware 才包得到
            await s.GetAsync(jobId, ct);

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            await http.Response.StartAsync(ct);

            await foreach (var payload in feed.ForJobAsync(jobId, ct))
            {
                await http.Response.WriteAsync($"data: {payload}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
        });

        app.MapPost("/api/schedules/{ym}/apply-variant", async (string ym, HttpContext http, ScheduleCommands c, CancellationToken ct) =>
        {
            var (jobId, variantId) = (await RequestBody.ReadAsync<ApplyVariantRequestDto>(http)).ToCommand();
            return (await c.ApplyVariantAsync(Parse.YearMonth(ym), jobId, variantId, ct)).ToContract();
        });
    }
}
