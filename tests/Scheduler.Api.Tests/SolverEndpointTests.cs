using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Api.Solving;

namespace Scheduler.Api.Tests;

/// <summary>
/// 求解端點的契約守法，跑的是真的 CP-SAT（時間上限 1–2 秒，情境是 fixture 的 8 位人員）。
/// 自己一顆資料庫、自己一個 <c>SolverJobService</c>（單一 slot），class 內的測試依序跑，不會互相搶 slot。
/// </summary>
public sealed class SolverEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _api;

    public SolverEndpointTests(ApiFixture api)
    {
        _api = api;
    }

    private Task<JsonNode> PostAsync(string path, string? body, string op, HttpStatusCode expected = HttpStatusCode.OK) =>
        _api.SendAsync(HttpMethod.Post, path, body, op, expected);

    private async Task<JsonNode> WaitForTerminalAsync(string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var job = await _api.GetAsync($"/api/solver-jobs/{jobId}", "getSolverJob");
            var status = job["status"]!.GetValue<string>();
            if (status is "succeeded" or "failed" or "cancelled")
            {
                return job;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("求解工作沒有結束");
    }

    [Fact]
    public async Task 建立_查詢_變體_套用_一路走完()
    {
        var created = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10","variantCount":1,"timeLimitSecPerVariant":2}""", "createSolverJob", HttpStatusCode.Accepted);
        var jobId = created["jobId"]!.GetValue<string>();
        Assert.Equal("2026-10", created["yearMonth"]!.GetValue<string>());
        Assert.Contains(created["status"]!.GetValue<string>(), new[] { "queued", "running" });
        Assert.Equal(7, created["scale"]!["staff"]!.GetValue<int>());
        Assert.True(created["scale"]!["variables"]!.GetValue<int>() > 0);
        Assert.Equal(7, created["constraintCount"]!["hard"]!.GetValue<int>());
        Assert.Contains("上月尚未發布", created["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Single(w => w.Contains("月結轉")));

        var done = await WaitForTerminalAsync(jobId);
        Assert.Equal("succeeded", done["status"]!.GetValue<string>());
        Assert.True(done["elapsedSec"]!.GetValue<double>() >= 0);
        Assert.Equal("succeeded", done["progress"]!["status"]!.GetValue<string>());
        Assert.Null(done["failureReason"]);

        var variants = (await _api.GetAsync($"/api/solver-jobs/{jobId}/variants", "listVariants"))["variants"]!.AsArray();
        var variant = Assert.Single(variants)!;
        Assert.Equal("v-a", variant["id"]!.GetValue<string>());
        Assert.Equal("重視公平", variant["label"]!.GetValue<string>());
        Assert.Equal(1.5, variant["weightProfile"]!["S1_QUOTA_FAIRNESS"]!.GetValue<double>());
        var duties = variant["duties"]!.AsArray();
        Assert.NotEmpty(duties);
        // 7 位在職裡只有 5 位醫師可排；空缺一定有，但硬違規只能是覆蓋
        Assert.Equal(variant["metrics"]!["vacancies"]!.GetValue<int>(), variant["hardViolationCount"]!.GetValue<int>());
        Assert.DoesNotContain(duties, d => d!["staffId"]!.GetValue<string>() == "s-gone");
        // 停用者不排、不可排班日守住
        Assert.DoesNotContain(duties, d => d!["staffId"]!.GetValue<string>() == "s-r4" && d["date"]!.GetValue<string>() == "2026-10-05");

        var applied = await PostAsync("/api/schedules/2026-10/apply-variant", $$"""{"jobId":"{{jobId}}","variantId":"v-a"}""", "applyVariant");
        Assert.Equal("draft", applied["status"]!.GetValue<string>());
        Assert.Equal(1, applied["revision"]!.GetValue<int>());
        Assert.Equal(duties.Count, applied["duties"]!.AsArray().Count);

        var schedule = await _api.GetAsync("/api/schedules/2026-10", "getSchedule");
        Assert.Equal(duties.Count, schedule["duties"]!.AsArray().Count);
    }

    [Fact]
    public async Task 已發布的月_不可套用_409()
    {
        var created = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-08","variantCount":1,"timeLimitSecPerVariant":1}""", "createSolverJob", HttpStatusCode.Accepted);
        var jobId = created["jobId"]!.GetValue<string>();
        await WaitForTerminalAsync(jobId);

        var error = await PostAsync("/api/schedules/2026-08/apply-variant", $$"""{"jobId":"{{jobId}}","variantId":"v-a"}""", "applyVariant", HttpStatusCode.Conflict);
        Assert.Equal("SCHEDULE_ALREADY_PUBLISHED", error["error"]!["code"]!.GetValue<string>());

        // 變體是 8 月的，套到 9 月是 422
        var wrongMonth = await PostAsync("/api/schedules/2026-09/apply-variant", $$"""{"jobId":"{{jobId}}","variantId":"v-a"}""", "applyVariant", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", wrongMonth["error"]!["code"]!.GetValue<string>());
        await PostAsync("/api/schedules/2026-08/apply-variant", $$"""{"jobId":"{{jobId}}","variantId":"v-z"}""", "applyVariant", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task 不存在的工作_404()
    {
        await _api.GetAsync("/api/solver-jobs/job-nope", "getSolverJob", HttpStatusCode.NotFound);
        await _api.CallAsync(HttpMethod.Delete, "/api/solver-jobs/job-nope", "cancelSolverJob", HttpStatusCode.NotFound);
        await _api.GetAsync("/api/solver-jobs/job-nope/variants", "listVariants", HttpStatusCode.NotFound);
        await _api.GetAsync("/api/solver-jobs/job-nope/stream", "streamSolverJob", HttpStatusCode.NotFound);
        await PostAsync("/api/schedules/2026-10/apply-variant", """{"jobId":"job-nope","variantId":"v-a"}""", "applyVariant", HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"yearMonth":"2026/10"}""")]
    [InlineData("""{"yearMonth":"2026-10","variantCount":5}""")]
    [InlineData("""{"yearMonth":"2026-10","timeLimitSecPerVariant":0}""")]
    [InlineData("not json")]
    public async Task 建立_壞本體_422(string body)
    {
        var error = await PostAsync("/api/solver-jobs", body, "createSolverJob", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", error["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task 同時只能一個_409_中止後才能再建_中止是冪等的()
    {
        var first = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10","variantCount":3,"timeLimitSecPerVariant":60}""", "createSolverJob", HttpStatusCode.Accepted);
        var firstId = first["jobId"]!.GetValue<string>();

        var busy = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10"}""", "createSolverJob", HttpStatusCode.Conflict);
        Assert.Equal("SOLVER_BUSY", busy["error"]!["code"]!.GetValue<string>());
        Assert.Equal(firstId, busy["error"]!["details"]!["jobId"]!.GetValue<string>());

        var cancelled = await _api.CallAsync(HttpMethod.Delete, $"/api/solver-jobs/{firstId}", "cancelSolverJob", HttpStatusCode.OK);
        Assert.Equal("cancelled", cancelled["status"]!.GetValue<string>());
        var again = await _api.CallAsync(HttpMethod.Delete, $"/api/solver-jobs/{firstId}", "cancelSolverJob", HttpStatusCode.OK);
        Assert.Equal("cancelled", again["status"]!.GetValue<string>());

        var second = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10","variantCount":1,"timeLimitSecPerVariant":1}""", "createSolverJob", HttpStatusCode.Accepted);
        await WaitForTerminalAsync(second["jobId"]!.GetValue<string>());
    }

    [Fact]
    public async Task 進度串流_是_SSE_每筆都符合契約_終態後結束()
    {
        var created = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10","variantCount":1,"timeLimitSecPerVariant":2}""", "createSolverJob", HttpStatusCode.Accepted);
        var jobId = created["jobId"]!.GetValue<string>();

        using var response = await _api.Client.GetAsync($"/api/solver-jobs/{jobId}/stream", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = new List<JsonNode>();
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
        {
            while (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) is { } line)
            {
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    events.Add(JsonNode.Parse(line["data: ".Length..])!);
                }
            }
        }

        Assert.NotEmpty(events);
        foreach (var e in events)
        {
            var errors = ContractSchema.Current.ValidateComponent("SolverProgress", e);
            Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + e.ToJsonString());
            Assert.Equal(jobId, e["jobId"]!.GetValue<string>());
        }

        Assert.Equal("succeeded", events[^1]["status"]!.GetValue<string>());
        Assert.Equal(1, events[^1]["variantIndex"]!.GetValue<int>());

        // 結束後再連：只有一筆終態，馬上結束
        var after = await _api.Client.GetStringAsync($"/api/solver-jobs/{jobId}/stream");
        Assert.Single(after.Split("\n\n", StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Shell 拿的是 <see cref="ISolverProgressFeed"/>：全域訂閱、每筆是契約 JSON、與 SSE 寫的是同一份。</summary>
    [Fact]
    public async Task 進度事件來源_全域訂閱的每筆都符合契約_與_SSE_同一份()
    {
        var feed = _api.Services.GetRequiredService<ISolverProgressFeed>();
        using var stop = new CancellationTokenSource();
        var fromFeed = new List<string>();
        var pumping = Task.Run(async () =>
        {
            await foreach (var json in feed.AllAsync(stop.Token))
            {
                fromFeed.Add(json);
            }
        });

        var created = await PostAsync("/api/solver-jobs", """{"yearMonth":"2026-10","variantCount":1,"timeLimitSecPerVariant":1}""", "createSolverJob", HttpStatusCode.Accepted);
        var jobId = created["jobId"]!.GetValue<string>();
        var sse = await _api.Client.GetStringAsync($"/api/solver-jobs/{jobId}/stream");
        await WaitForTerminalAsync(jobId);
        await Task.Delay(200);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pumping.WaitAsync(TimeSpan.FromSeconds(10)));

        var mine = fromFeed.Select(j => JsonNode.Parse(j)!).Where(e => e["jobId"]!.GetValue<string>() == jobId).ToList();
        Assert.NotEmpty(mine);
        foreach (var e in mine)
        {
            var errors = ContractSchema.Current.ValidateComponent("SolverProgress", e);
            Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + e.ToJsonString());
        }

        Assert.Equal("succeeded", mine[^1]["status"]!.GetValue<string>());

        // SSE 寫進 data: 的字串是同一個 feed 序列化的（構造上保證）。SSE 若在工作結束後才連上，拿到的是從
        // 資料庫重建的終態快照，逐字未必等於廣播過的那筆，所以只比終態的身分欄位
        var sseLast = JsonNode.Parse(sse.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[^1]["data: ".Length..].Trim())!;
        Assert.Equal(mine[^1]["jobId"]!.GetValue<string>(), sseLast["jobId"]!.GetValue<string>());
        Assert.Equal(mine[^1]["status"]!.GetValue<string>(), sseLast["status"]!.GetValue<string>());
        Assert.Equal(mine[^1]["variantIndex"]!.GetValue<int>(), sseLast["variantIndex"]!.GetValue<int>());
    }
}
