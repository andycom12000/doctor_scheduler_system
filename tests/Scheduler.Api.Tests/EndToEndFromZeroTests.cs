using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Defaults;
using Scheduler.Persistence;
using Scheduler.Persistence.Seed;

namespace Scheduler.Api.Tests;

/// <summary>
/// #83：模擬發佈版第一次啟動（<c>SeedReferenceRoster: false</c>，名冊是空的），照使用者的操作順序
/// 從建人員一路走到發布、匯出、下個月再求解。每一步的回應都過契約 schema。
/// 自己一顆資料庫、不透過 <see cref="ApiFixture"/>（它會先灌 8 人的固定情境，這裡要的是空名冊）。
/// 月份選 2026-02／2026-03：2026 年才有內建國定假日（2 月有春節連假），而且其他測試的月份不會落在
/// 這兩個月（它們用 2026-07 以後與 2027 年）。測試月份在過去，因此不依賴「今天」。
/// </summary>
public sealed class EndToEndFromZeroTests : IAsyncLifetime
{
    private const string Month = "2026-02";
    private const string NextMonth = "2026-03";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _app = await ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true,
            SeedReferenceRoster: false,
            ConfigurePersistence: services => services.AddSchedulerPersistence(_connection)));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<JsonNode> CallAsync(HttpMethod method, string path, string? body, string operationId, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{method} {path} 應回 {(int)expected}，實際 {(int)response.StatusCode}：{text}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonNode.Parse(text)!;
        var errors = ContractSchema.Current.Validate(operationId, (int)expected, json);
        Assert.True(errors.Count == 0, $"{method} {path} 的回應不符契約 {operationId}/{(int)expected}：\n" + string.Join("\n", errors) + "\n本體：" + text);
        return json;
    }

    private Task<JsonNode> GetAsync(string path, string operationId) =>
        CallAsync(HttpMethod.Get, path, null, operationId, HttpStatusCode.OK);

    private async Task<JsonNode> SolveAsync(string yearMonth, int timeLimitSec)
    {
        var created = await CallAsync(HttpMethod.Post, "/api/solver-jobs",
            $$"""{"yearMonth":"{{yearMonth}}","variantCount":1,"timeLimitSecPerVariant":{{timeLimitSec}}}""",
            "createSolverJob", HttpStatusCode.Accepted);
        var jobId = created["jobId"]!.GetValue<string>();

        var deadline = DateTime.UtcNow.AddSeconds(timeLimitSec + 40);
        while (DateTime.UtcNow < deadline)
        {
            var job = await GetAsync($"/api/solver-jobs/{jobId}", "getSolverJob");
            var status = job["status"]!.GetValue<string>();
            if (status == "succeeded")
            {
                return created;
            }

            Assert.True(status is "queued" or "running", $"求解工作以 {status} 結束：{job["failureReason"]}");
            await Task.Delay(200);
        }

        throw new TimeoutException("求解工作沒有結束");
    }

    private static IEnumerable<string> Warnings(JsonNode job) =>
        job["warnings"]!.AsArray().Select(w => w!.GetValue<string>());

    [Fact]
    public async Task 全新資料庫_從建名冊到發布匯出_再排下個月()
    {
        // 1. 空名冊
        Assert.Empty((await GetAsync("/api/staff", "listStaff"))["items"]!.AsArray());

        // 2. 照 DefaultRanks.ReferenceHeadcount 建人員（姓名、員編借 ReferenceRoster，不借它的 id）
        var roster = ReferenceRoster.Build();
        Assert.Equal(DefaultRanks.ReferenceHeadcount.Values.Sum(), roster.Count);
        var idByEmployeeNo = new Dictionary<string, string>();
        foreach (var person in roster)
        {
            var newStaff = await CallAsync(HttpMethod.Post, "/api/staff",
                $$"""{"employeeNo":"{{person.EmployeeNo}}","name":"{{person.Name}}","rankCode":"{{person.RankCode}}"}""",
                "createStaff", HttpStatusCode.Created);
            idByEmployeeNo[person.EmployeeNo] = newStaff["id"]!.GetValue<string>();
        }

        var staff = (await GetAsync("/api/staff", "listStaff"))["items"]!.AsArray();
        Assert.Equal(roster.Count, staff.Count);
        var byRank = staff.GroupBy(s => s!["rankCode"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(DefaultRanks.ReferenceHeadcount.OrderBy(kv => kv.Key), byRank.OrderBy(kv => kv.Key));

        // 3. 行事曆：測試月份的國定假日（2 月春節連假）有讀到
        var calendar = await GetAsync($"/api/calendars/{Month[..4]}", "getCalendar");
        var febDays = calendar["days"]!.AsArray().Where(d => d!["date"]!.GetValue<string>().StartsWith(Month, StringComparison.Ordinal)).ToList();
        Assert.Equal(28, febDays.Count);
        var spring = febDays.Single(d => d!["date"]!.GetValue<string>() == "2026-02-17")!;
        Assert.True(spring["isPublicHoliday"]!.GetValue<bool>());
        Assert.True(spring["isHoliday"]!.GetValue<bool>());
        Assert.Equal("春節", spring["holidayName"]!.GetValue<string>());

        // 4. 登記不可排班日：6 位醫師、各 2 天。2026-02 的平日（非假日）：2–6、9–13、23–26 日
        // （2/1 是週日、2/7–8、14–15、21–22 是週末，2/16–20 是春節連假與補假，2/27–28 是和平紀念日連假）
        var weekdays = new[] { 2, 3, 4, 5, 6, 9, 10, 11, 12, 13, 23, 24 };
        var blocked = new List<(string StaffId, string Date)>();
        var rosterByRank = roster.GroupBy(p => p.RankCode).ToDictionary(g => g.Key, g => g.ToList());
        var picks = new[] { DefaultRanks.R4, DefaultRanks.R5, DefaultRanks.R2, DefaultRanks.R3, DefaultRanks.R1, DefaultRanks.PGY2 };
        for (var i = 0; i < picks.Length; i++)
        {
            var id = idByEmployeeNo[rosterByRank[picks[i]][0].EmployeeNo];
            foreach (var day in new[] { weekdays[2 * i], weekdays[2 * i + 1] })
            {
                var date = $"{Month}-{day:00}";
                await CallAsync(HttpMethod.Put, $"/api/blocked-days/{Month}/{id}/{date}", null, "setBlockedDay", HttpStatusCode.OK);
                blocked.Add((id, date));
            }
        }

        // 5. 可行性預警
        var feasibility = await GetAsync($"/api/blocked-days/{Month}/feasibility", "getFeasibility");
        Assert.True(feasibility["feasible"]!.GetValue<bool>());

        // 6. 求解（要斷言空缺 0，時限給足）
        var created = await SolveAsync(Month, timeLimitSec: 20);
        Assert.Equal(34, created["scale"]!["staff"]!.GetValue<int>());
        Assert.Contains(SchedulingContextLoader.PreviousMonthNotPublishedWarning, Warnings(created));
        var jobId = created["jobId"]!.GetValue<string>();
        var variants = (await GetAsync($"/api/solver-jobs/{jobId}/variants", "listVariants"))["variants"]!.AsArray();
        var variant = Assert.Single(variants)!;

        // 7. 空缺 0、硬違規 0、登記過的不可排班日沒被排
        Assert.Equal(0, variant["metrics"]!["vacancies"]!.GetValue<int>());
        Assert.Equal(0, variant["hardViolationCount"]!.GetValue<int>());
        var duties = variant["duties"]!.AsArray();
        Assert.NotEmpty(duties);
        foreach (var (staffId, date) in blocked)
        {
            Assert.DoesNotContain(duties, d => d!["staffId"]!.GetValue<string>() == staffId && d["date"]!.GetValue<string>() == date);
        }

        // 8. 套用 → 草稿
        var variantId = variant["id"]!.GetValue<string>();
        var applied = await CallAsync(HttpMethod.Post, $"/api/schedules/{Month}/apply-variant",
            $$"""{"jobId":"{{jobId}}","variantId":"{{variantId}}"}""", "applyVariant", HttpStatusCode.OK);
        Assert.Equal("draft", applied["status"]!.GetValue<string>());
        Assert.Equal(duties.Count, applied["duties"]!.AsArray().Count);

        // 9. 發布。求解輸出的解每次不同，月結轉可能全 0，所以先在 JUNIOR 組挑剩餘額度最大者、
        // 清掉他一格值班（造一個空缺），他的剩餘額度就嚴格大於同組任何有值班者，同組其他有值班者的月結轉必 > 0。
        // 清格造了空缺（硬違規），發布要帶 acknowledgeViolations。
        var draftBoard = await GetAsync($"/api/schedules/{Month}/point-board", "getPointBoard");
        var dutiesByStaff = applied["duties"]!.AsArray().Where(d => d!["staffId"] is not null).GroupBy(d => d!["staffId"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.First()!);
        var group = draftBoard["groups"]!.AsArray()
            .Single(g => g!["groupCode"]!.GetValue<string>() == "JUNIOR")!["rows"]!.AsArray();
        var picked = group.Where(r => dutiesByStaff.ContainsKey(r!["staffId"]!.GetValue<string>()))
            .MaxBy(r => r!["quotaRemaining"]!.GetValue<int>())!;
        var pickedId = picked["staffId"]!.GetValue<string>();
        var cell = dutiesByStaff[pickedId];
        await CallAsync(HttpMethod.Patch, $"/api/schedules/{Month}/duties",
            $$"""{"areaId":"{{cell["areaId"]!.GetValue<string>()}}","date":"{{cell["date"]!.GetValue<string>()}}","staffId":null}""",
            "setDuty", HttpStatusCode.OK);

        var published = await CallAsync(HttpMethod.Post, $"/api/schedules/{Month}/publish", """{"acknowledgeViolations":true}""", "publishSchedule", HttpStatusCode.OK);
        Assert.Equal("published", published["status"]!.GetValue<string>());
        Assert.Equal(1, published["publishedVersion"]!.GetValue<int>());
        var carryOver = published["carryOver"]!.AsArray();
        Assert.NotEmpty(carryOver);
        var actual = carryOver.ToDictionary(e => e!["staffId"]!.GetValue<string>(), e => e!["points"]!.GetValue<int>());

        // 月結轉 = 同組內「剩餘額度最多的人」為 0、其他人與他的差額（NP 不計）。剩餘額度的尺是
        // 上限 − 已排 − 起始偏移（Domain 的 QuotaRemaining），看板的 quotaRemaining 是 cap − quotaPoints，
        // 所以用 quotaRemaining − carryOverApplied 重算。2 月沒有上月結轉，偏移是 0，但欄位必須存在。
        var febBoard = await GetAsync($"/api/schedules/{Month}/point-board", "getPointBoard");
        var expected = new Dictionary<string, int>();
        foreach (var g in febBoard["groups"]!.AsArray())
        {
            var rows = g!["rows"]!.AsArray().Where(r => r!["quotaRemaining"] is not null).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            int Remaining(JsonNode r) => r["quotaRemaining"]!.GetValue<int>() - r["carryOverApplied"]!.GetValue<int>();
            var max = rows.Max(r => Remaining(r!));
            foreach (var r in rows)
            {
                expected[r!["staffId"]!.GetValue<string>()] = max - Remaining(r!);
            }
        }

        Assert.Equal(expected.OrderBy(kv => kv.Key), actual.OrderBy(kv => kv.Key));
        // 確定性的部分：被清格者的剩餘額度大於同組任何有值班者（原剩餘 r_P 加上該格點數 p ≥ 1，
        // 而其他人 r_j ≤ r_P），所以同組其他有值班者的月結轉都比他大、必 > 0。
        // 整月 0 班的人剩餘額度是上限、可能更大，不在這個斷言內，由上面的完整重算把關。
        var juniorIds = febBoard["groups"]!.AsArray().Single(g => g!["groupCode"]!.GetValue<string>() == "JUNIOR")!["rows"]!.AsArray().Select(r => r!["staffId"]!.GetValue<string>()).ToList();
        var otherWorkers = juniorIds.Where(id => id != pickedId && dutiesByStaff.ContainsKey(id)).ToList();
        Assert.NotEmpty(otherWorkers);
        Assert.All(otherWorkers, id => Assert.True(actual[id] > actual[pickedId], $"{id} 的月結轉應大於被清格者"));
        Assert.All(otherWorkers, id => Assert.True(actual[id] > 0, $"{id} 的月結轉應 > 0"));

        // 10. 匯出
        using (var response = await _client.GetAsync($"/api/schedules/{Month}/export"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(ContractSchema.Current.HasResponse("exportSchedule", 200));
            Assert.Equal(Xlsx, response.Content.Headers.ContentType!.MediaType);
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }

        // 11. 下個月：不再警告上月尚未發布，點數看板帶得到上月月結轉
        var next = await SolveAsync(NextMonth, timeLimitSec: 5);
        Assert.DoesNotContain(SchedulingContextLoader.PreviousMonthNotPublishedWarning, Warnings(next));
        // 點數看板要有值班表才查得到：把下個月的變體套成草稿（草稿時看板即時讀上月）
        var nextId = next["jobId"]!.GetValue<string>();
        var nextVariant = Assert.Single((await GetAsync($"/api/solver-jobs/{nextId}/variants", "listVariants"))["variants"]!.AsArray())!;
        await CallAsync(HttpMethod.Post, $"/api/schedules/{NextMonth}/apply-variant",
            $$"""{"jobId":"{{nextId}}","variantId":"{{nextVariant["id"]!.GetValue<string>()}}"}""", "applyVariant", HttpStatusCode.OK);
        var board = await GetAsync($"/api/schedules/{NextMonth}/point-board", "getPointBoard");
        var applied2 = board["groups"]!.AsArray()
            .SelectMany(g => g!["rows"]!.AsArray())
            .ToDictionary(r => r!["staffId"]!.GetValue<string>(), r => r!["carryOverApplied"]!.GetValue<int>());
        foreach (var entry in carryOver)
        {
            Assert.Equal(entry!["points"]!.GetValue<int>(), applied2[entry["staffId"]!.GetValue<string>()]);
        }
    }
}
