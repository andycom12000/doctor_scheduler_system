using System.Net;
using System.Text.Json.Nodes;

namespace Scheduler.Api.Tests;

/// <summary>
/// 契約守法（ARCHITECTURE §7）：把 Api 跑起來逐個端點打，狀態碼與回應本體用 api-contract.yaml 驗。
/// 業務語義的正確性在 Application.Tests；這裡只驗「形狀、狀態碼、錯誤碼、參數解析」——Api 該負責的事。
/// </summary>
public sealed class ReadEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _api;

    public ReadEndpointTests(ApiFixture api)
    {
        _api = api;
    }

    [Fact]
    public async Task Health()
    {
        var body = await _api.GetAsync("/api/health", "getHealth");
        Assert.Equal("ok", body["status"]!.GetValue<string>());
    }

    // -- 值班表 -------------------------------------------------------------

    [Fact]
    public async Task 值班表清單_依年月排序_帶狀態與硬違規數()
    {
        var body = await _api.GetAsync("/api/schedules", "listSchedules");
        var months = body["months"]!.AsArray();
        Assert.Equal(new[] { "2026-08", "2026-09" }, months.Select(m => m!["yearMonth"]!.GetValue<string>()));
        Assert.Equal("published", months[0]!["status"]!.GetValue<string>());
        Assert.NotNull(months[0]!["publishedAt"]);
        Assert.Equal("draft", months[1]!["status"]!.GetValue<string>());
        Assert.Null(months[1]!["publishedAt"]);
        Assert.True(months[1]!["hardViolationCount"]!.GetValue<int>() >= 1, "9/1、9/2 連值應至少一條硬違規");
    }

    [Fact]
    public async Task 單月值班表_只列已指派的格子_publishedAt為null()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09", "getSchedule");
        Assert.Equal("2026-09", body["yearMonth"]!.GetValue<string>());
        Assert.Equal(30, body["dayCount"]!.GetValue<int>());
        Assert.Equal(5, body["areas"]!.AsArray().Count);
        Assert.Equal(7, body["duties"]!.AsArray().Count);
        Assert.Contains("publishedAt", ((JsonObject)body).Select(kv => kv.Key));
        Assert.Null(body["publishedAt"]);
        Assert.Equal("area:area-a:2026-09-01", body["duties"]![0]!["cellKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task 已發布的值班表_publishedAt是ISO時間()
    {
        var body = await _api.GetAsync("/api/schedules/2026-08", "getSchedule");
        Assert.Equal("published", body["status"]!.GetValue<string>());
        Assert.StartsWith("2026-07-28", body["publishedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task 尚無值班表的月份_404_NOT_FOUND()
    {
        var body = await _api.GetAsync("/api/schedules/2030-01", "getSchedule", HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", body["error"]!["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body["error"]!["message"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("2026-9")]
    [InlineData("2026-13")]
    [InlineData("abc")]
    public async Task 年月格式錯誤_422_INVALID_REQUEST(string ym)
    {
        var body = await _api.GetAsync($"/api/schedules/{ym}", "getSchedule", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", body["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task 驗證_回違規與摘要()
    {
        var body = await _api.CallAsync(HttpMethod.Post, "/api/schedules/2026-09/validate", "validateSchedule", HttpStatusCode.OK);
        Assert.False(body["ok"]!.GetValue<bool>());
        var violations = body["violations"]!.AsArray();
        Assert.Equal(body["summary"]!["hard"]!.GetValue<int>(), violations.Count(v => v!["severity"]!.GetValue<string>() == "hard"));
        Assert.Contains(violations, v => v!["code"]!.GetValue<string>() == "H4_MIN_GAP");
    }

    [Fact]
    public async Task 驗證_尚無值班表_404()
    {
        await _api.CallAsync(HttpMethod.Post, "/api/schedules/2030-01/validate", "validateSchedule", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task 違規清單_可依嚴重度與日期篩()
    {
        var all = (await _api.GetAsync("/api/schedules/2026-09/violations", "listViolations"))["violations"]!.AsArray();
        var hard = (await _api.GetAsync("/api/schedules/2026-09/violations?severity=hard", "listViolations"))["violations"]!.AsArray();
        var day = (await _api.GetAsync("/api/schedules/2026-09/violations?date=2026-09-02", "listViolations"))["violations"]!.AsArray();

        Assert.True(all.Count >= hard.Count);
        Assert.All(hard, v => Assert.Equal("hard", v!["severity"]!.GetValue<string>()));
        Assert.All(day, v => Assert.Contains(v!["cellKeys"]!.AsArray(), k => k!.GetValue<string>().EndsWith(":2026-09-02")));
        Assert.NotEmpty(day);
    }

    [Fact]
    public async Task 違規清單_severity不合法_422()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/violations?severity=medium", "listViolations", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", body["error"]!["code"]!.GetValue<string>());
    }

    // -- 檢視 ---------------------------------------------------------------

    [Fact]
    public async Task 點數看板_依身分組分區_NP的上限為null_月結轉帶入()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/point-board", "getPointBoard");
        var groups = body["groups"]!.AsArray();
        Assert.Equal(4, groups.Count);

        var rows = groups.SelectMany(g => g!["rows"]!.AsArray()).ToDictionary(r => r!["staffId"]!.GetValue<string>(), r => r!);
        Assert.DoesNotContain("s-gone", rows.Keys);
        Assert.Null(rows["s-np"]["quotaCap"]);
        Assert.Contains("quotaCap", ((JsonObject)rows["s-np"]).Select(kv => kv.Key));
        Assert.Equal(2, rows["s-r4"]["carryOverApplied"]!.GetValue<int>());
        Assert.Equal(1, rows["s-r4"]["duties"]!.GetValue<int>());
    }

    [Fact]
    public async Task 單日詳表_五區_空格的staff為null()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/days/2026-09-02", "getDayDetail");
        var areas = body["areas"]!.AsArray();
        Assert.Equal(5, areas.Count);
        var icu = areas.Single(a => a!["areaId"]!.GetValue<string>() == "area-icu")!;
        Assert.False(icu["filled"]!.GetValue<bool>());
        Assert.Null(icu["staff"]);
        var chief = areas.Single(a => a!["areaId"]!.GetValue<string>() == "area-chief")!;
        Assert.Equal("s-r5", chief["staff"]!["staffId"]!.GetValue<string>());
    }

    [Fact]
    public async Task 單日詳表_日期不在該月_404()
    {
        await _api.GetAsync("/api/schedules/2026-09/days/2026-10-01", "getDayDetail", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task 單日詳表_日期格式錯誤_422()
    {
        await _api.GetAsync("/api/schedules/2026-09/days/2026-9-2", "getDayDetail", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task 空缺_依日期彙總()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/vacancies", "listVacancies");
        var byDate = body["byDate"]!.AsArray();
        Assert.Equal(body["total"]!.GetValue<int>(), byDate.Sum(d => d!["count"]!.GetValue<int>()));
        Assert.DoesNotContain(byDate, d => d!["date"]!.GetValue<string>() == "2026-09-01");
        var sep2 = byDate.Single(d => d!["date"]!.GetValue<string>() == "2026-09-02")!;
        Assert.Equal(3, sep2["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task 候選人_列出並標阻擋理由()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/candidates?areaId=area-icu&date=2026-09-02", "listCandidates");
        var candidates = body["candidates"]!.AsArray();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.NotNull(c!["blockingReasons"]));
        var r2 = candidates.Single(c => c!["staffId"]!.GetValue<string>() == "s-r2")!;
        Assert.NotEmpty(r2["blockingReasons"]!.AsArray());
    }

    [Fact]
    public async Task 候選人_缺參數_422()
    {
        var body = await _api.GetAsync("/api/schedules/2026-09/candidates?date=2026-09-02", "listCandidates", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", body["error"]!["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/api/schedules/2030-01/violations", "listViolations")]
    [InlineData("/api/schedules/2030-01/point-board", "getPointBoard")]
    [InlineData("/api/schedules/2030-01/days/2030-01-01", "getDayDetail")]
    [InlineData("/api/schedules/2030-01/vacancies", "listVacancies")]
    [InlineData("/api/schedules/2030-01/candidates?areaId=area-a&date=2030-01-01", "listCandidates")]
    public async Task 由值班表推導的檢視_尚無值班表_404(string path, string operationId)
    {
        var body = await _api.GetAsync(path, operationId, HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", body["error"]!["code"]!.GetValue<string>());
    }

    // -- 不可排班日 ---------------------------------------------------------

    [Fact]
    public async Task 不可排班日登記_不需要值班表存在_每位在職人員一列()
    {
        var body = await _api.GetAsync("/api/blocked-days/2026-10", "getBlockedDays");
        Assert.Equal(16, body["monthlyCap"]!.GetValue<int>());
        Assert.Single(body["entries"]!.AsArray());
        Assert.Equal(7, body["byStaff"]!.AsArray().Count);
        var r4 = body["byStaff"]!.AsArray().Single(r => r!["staffId"]!.GetValue<string>() == "s-r4")!;
        Assert.Equal(1, r4["count"]!.GetValue<int>());
        Assert.Equal(15, r4["remaining"]!.GetValue<int>());
        Assert.Equal("2026-10-05", body["byDate"]![0]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task 可行性預警_三層巢狀供需_附提醒()
    {
        var body = await _api.GetAsync("/api/blocked-days/2026-10/feasibility", "getFeasibility");
        Assert.Equal(3, body["bySupply"]!.AsArray().Count);
        Assert.Equal(31, body["byDate"]!.AsArray().Count);
        Assert.NotNull(body["warnings"]);
    }

    // -- 設定 ---------------------------------------------------------------

    [Fact]
    public async Task 區域設定()
    {
        var body = await _api.GetAsync("/api/settings/areas", "getAreaSettings");
        Assert.Equal(3, body["areaTypes"]!.AsArray().Count);
        Assert.Equal(5, body["areas"]!.AsArray().Count);
    }

    [Fact]
    public async Task 身分設定_NP的上限與點數類型為null()
    {
        var body = await _api.GetAsync("/api/settings/ranks", "getRankSettings");
        Assert.Equal(10, body["ranks"]!.AsArray().Count);
        Assert.Equal(4, body["groups"]!.AsArray().Count);
        var np = body["ranks"]!.AsArray().Single(r => r!["code"]!.GetValue<string>() == "NP")!;
        Assert.Null(np["quotaCap"]);
        Assert.Null(np["pointType"]);
        Assert.Equal("B", body["ranks"]!.AsArray().Single(r => r!["code"]!.GetValue<string>() == "R4")!["pointType"]!.GetValue<string>());
    }

    [Fact]
    public async Task 資格矩陣_包在matrix底下()
    {
        var body = await _api.GetAsync("/api/settings/eligibility-matrix", "getEligibilityMatrix");
        Assert.True(body["matrix"]!["R4"]!["CHIEF"]!.GetValue<bool>());
        Assert.False(body["matrix"]!["PGY1"]!["ICU"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 點數規則_兩套點數與查表()
    {
        var body = await _api.GetAsync("/api/settings/point-rules", "getPointRules");
        Assert.Equal(2, body["quota"]!["holiday"]!.GetValue<int>());
        Assert.Equal(4, body["fairness"]!["tables"]!["A"]!.AsArray().Count);
        Assert.Equal("holiday", body["fairness"]!["tables"]!["A"]![0]!["today"]!.GetValue<string>());
        Assert.Equal(10, body["fairness"]!["consecutiveSaturdayBonus"]!["windowDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task 約束定義_七硬七軟_列舉是契約字串_不限的範圍省略()
    {
        var body = await _api.GetAsync("/api/settings/constraints", "getConstraints");
        var hard = body["hard"]!.AsArray();
        var soft = body["soft"]!.AsArray();
        Assert.Equal(7, hard.Count);
        Assert.Equal(7, soft.Count);

        var h1 = hard.Single(c => c!["code"]!.GetValue<string>() == "H1_AREA_COVERAGE")!;
        Assert.Equal("ExactCount", h1["primitive"]!.GetValue<string>());
        Assert.DoesNotContain("scope", ((JsonObject)h1).Select(kv => kv.Key));
        Assert.DoesNotContain("metric", ((JsonObject)h1).Select(kv => kv.Key));

        var h4 = hard.Single(c => c!["code"]!.GetValue<string>() == "H4_MIN_GAP")!;
        Assert.Equal(3, h4["params"]!["days"]!.GetValue<int>());
        Assert.Equal("NP", h4["scope"]!["exemptRankCodes"]![0]!.GetValue<string>());

        var np = soft.Single(c => c!["scope"]?["dayKinds"] is not null)!;
        Assert.Equal("holiday", np["scope"]!["dayKinds"]![0]!.GetValue<string>());
        Assert.Equal("avoid", np["params"]!["direction"]!.GetValue<string>());

        Assert.Contains(soft, c => c!["metric"]?.GetValue<string>() == "quota_point");
    }

    [Fact]
    public async Task 當月覆寫_沒設過也回200()
    {
        var body = await _api.GetAsync("/api/settings/monthly-overrides/2026-09", "getMonthlyOverride");
        Assert.Equal("2026-09", body["yearMonth"]!.GetValue<string>());
    }

    // -- 行事曆與人員 -------------------------------------------------------

    [Fact]
    public async Task 行事曆_整年_國定假日與weekday()
    {
        var body = await _api.GetAsync("/api/calendars/2026", "getCalendar");
        var days = body["days"]!.AsArray();
        Assert.Equal(365, days.Count);
        var teachersDay = days.Single(d => d!["date"]!.GetValue<string>() == "2026-09-28")!;
        Assert.True(teachersDay["isPublicHoliday"]!.GetValue<bool>());
        Assert.Equal(2, teachersDay["quotaPointValue"]!.GetValue<int>());
        Assert.Equal(0, days.Single(d => d!["date"]!.GetValue<string>() == "2026-09-06")!["weekday"]!.GetValue<int>());
    }

    [Fact]
    public async Task 行事曆_沒有例外日的年份_純週末()
    {
        var body = await _api.GetAsync("/api/calendars/2031", "getCalendar");
        Assert.Equal(365, body["days"]!.AsArray().Count);
        Assert.DoesNotContain(body["days"]!.AsArray(), d => d!["isPublicHoliday"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 行事曆_年份不是數字_422()
    {
        await _api.GetAsync("/api/calendars/abcd", "getCalendar", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task 人員名冊_可依狀態篩_counts不受篩選影響()
    {
        var all = await _api.GetAsync("/api/staff", "listStaff");
        Assert.Equal(8, all["items"]!.AsArray().Count);
        Assert.Equal(7, all["counts"]!["active"]!.GetValue<int>());
        Assert.Equal(1, all["counts"]!["inactive"]!.GetValue<int>());

        var inactive = await _api.GetAsync("/api/staff?status=inactive", "listStaff");
        Assert.Single(inactive["items"]!.AsArray());
        Assert.Equal(7, inactive["counts"]!["active"]!.GetValue<int>());

        var r4 = all["items"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == "s-r4")!;
        Assert.Contains("CHIEF", r4["eligibleAreaTypes"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public async Task 人員名冊_status不合法_422()
    {
        await _api.GetAsync("/api/staff?status=fired", "listStaff", HttpStatusCode.UnprocessableEntity);
    }
}
