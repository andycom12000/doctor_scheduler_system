using System.Net;
using System.Text.Json.Nodes;

namespace Scheduler.Api.Tests;

/// <summary>
/// 寫入端點的契約守法。自己一個 class 就有自己的一顆資料庫（<see cref="IClassFixture{T}"/> 是每個 class 一份），
/// 不會弄髒讀取測試看的資料。同一 class 內的測試 xunit 不保證順序，所以每個測試各用自己的月份或自己建的人員。
/// </summary>
public sealed class WriteEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _api;

    public WriteEndpointTests(ApiFixture api)
    {
        _api = api;
    }

    private Task<JsonNode> PatchAsync(string path, string body, string op, HttpStatusCode expected = HttpStatusCode.OK) =>
        _api.SendAsync(HttpMethod.Patch, path, body, op, expected);

    private Task<JsonNode> PostAsync(string path, string? body, string op, HttpStatusCode expected = HttpStatusCode.OK) =>
        _api.SendAsync(HttpMethod.Post, path, body, op, expected);

    private Task<JsonNode> PutAsync(string path, string? body, string op, HttpStatusCode expected = HttpStatusCode.OK) =>
        _api.SendAsync(HttpMethod.Put, path, body, op, expected);

    // -- 值班表 -------------------------------------------------------------

    [Fact]
    public async Task setDuty_該月尚無值班表_自動建草稿_回改動格與全量違規()
    {
        var body = await PatchAsync("/api/schedules/2026-11/duties", """{"areaId":"area-icu","date":"2026-11-03","staffId":"s-r2"}""", "setDuty");

        Assert.Equal(1, body["revision"]!.GetValue<int>());
        var cell = Assert.Single(body["duties"]!.AsArray());
        Assert.Equal("s-r2", cell!["staffId"]!.GetValue<string>());
        Assert.Equal("area:area-icu:2026-11-03", cell["cellKey"]!.GetValue<string>());
        Assert.NotEmpty(body["violations"]!.AsArray());

        var schedule = await _api.GetAsync("/api/schedules/2026-11", "getSchedule");
        Assert.Equal("draft", schedule["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task setDuty_清空_staffId_為_null_仍帶那一格()
    {
        await PatchAsync("/api/schedules/2026-12/duties", """{"areaId":"area-a","date":"2026-12-10","staffId":"s-pgy1"}""", "setDuty");
        var body = await PatchAsync("/api/schedules/2026-12/duties", """{"areaId":"area-a","date":"2026-12-10","staffId":null}""", "setDuty");

        var cell = Assert.Single(body["duties"]!.AsArray());
        Assert.True(cell!.AsObject().ContainsKey("staffId"));
        Assert.Null(cell["staffId"]);
        Assert.Equal(2, body["revision"]!.GetValue<int>());
    }

    [Fact]
    public async Task setDuty_同人同日另一區_409_帶_areaId()
    {
        await PatchAsync("/api/schedules/2027-01/duties", """{"areaId":"area-icu","date":"2027-01-05","staffId":"s-r3"}""", "setDuty");
        var body = await PatchAsync("/api/schedules/2027-01/duties", """{"areaId":"area-a","date":"2027-01-05","staffId":"s-r3"}""", "setDuty", HttpStatusCode.Conflict);

        Assert.Equal("STAFF_ALREADY_ON_DUTY", body["error"]!["code"]!.GetValue<string>());
        Assert.Equal("area-icu", body["error"]!["details"]!["areaId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"areaId":"area-icu","date":"2027-03-05","staffId":"s-r2"}""")]   // 日期不在本月
    [InlineData("""{"areaId":"area-nope","date":"2027-02-05","staffId":"s-r2"}""")]  // 區域不存在
    [InlineData("""{"areaId":"area-icu","date":"2027-02-05","staffId":"s-nope"}""")] // 人員不存在
    [InlineData("""{"date":"2027-02-05","staffId":"s-r2"}""")]                        // 缺 areaId
    [InlineData("""{"areaId":"area-icu","date":"05/02/2027"}""")]                      // 日期格式
    [InlineData("""{not json""")]                                                     // 壞 JSON
    [InlineData("")]                                                                  // 空本體
    public async Task setDuty_請求無效_422(string body)
    {
        var error = await PatchAsync("/api/schedules/2027-02/duties", body, "setDuty", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", error["error"]!["code"]!.GetValue<string>());
        await _api.GetAsync("/api/schedules/2027-02", "getSchedule", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task swapDuties_對調_兩格都回()
    {
        await PatchAsync("/api/schedules/2027-04/duties", """{"areaId":"area-icu","date":"2027-04-05","staffId":"s-r2"}""", "setDuty");
        var body = await PostAsync("/api/schedules/2027-04/duties/swap",
            """{"a":{"areaId":"area-icu","date":"2027-04-05"},"b":{"areaId":"area-icu","date":"2027-04-12"}}""", "swapDuties");

        var cells = body["duties"]!.AsArray();
        Assert.Equal(2, cells.Count);
        Assert.Null(cells[0]!["staffId"]);
        Assert.Equal("s-r2", cells[1]!["staffId"]!.GetValue<string>());
    }

    [Fact]
    public async Task swapDuties_該月沒有值班表_404()
    {
        var body = await PostAsync("/api/schedules/2030-01/duties/swap",
            """{"a":{"areaId":"area-icu","date":"2030-01-05"},"b":{"areaId":"area-icu","date":"2030-01-12"}}""", "swapDuties", HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", body["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task publish_有硬違規_409_確認後_200_帶月結轉_沒本體也行()
    {
        await PatchAsync("/api/schedules/2027-05/duties", """{"areaId":"area-chief","date":"2027-05-03","staffId":"s-r4"}""", "setDuty");

        var conflict = await PostAsync("/api/schedules/2027-05/publish", null, "publishSchedule", HttpStatusCode.Conflict);
        Assert.Equal("HARD_VIOLATIONS_PRESENT", conflict["error"]!["code"]!.GetValue<string>());

        var body = await PostAsync("/api/schedules/2027-05/publish", """{"acknowledgeViolations":true}""", "publishSchedule");
        Assert.Equal("published", body["status"]!.GetValue<string>());
        Assert.Equal(2, body["revision"]!.GetValue<int>());
        var carryOver = body["carryOver"]!.AsArray();
        // 資深組：s-r4 上限 6 值了一個平日剩 5、s-r5 上限 5 沒值剩 5 → 平手都是 0
        // 中階組：s-r2 剩 8、s-r3 剩 7 → s-r3 差 1；低年級組：s-pgy1 剩 10、s-r1 剩 9 → s-r1 差 1
        var points = carryOver.ToDictionary(e => e!["staffId"]!.GetValue<string>(), e => e!["points"]!.GetValue<int>());
        Assert.Equal(new Dictionary<string, int> { ["s-r4"] = 0, ["s-r5"] = 0, ["s-r2"] = 0, ["s-r3"] = 1, ["s-pgy1"] = 0, ["s-r1"] = 1 }, points);

        var schedule = await _api.GetAsync("/api/schedules/2027-05", "getSchedule");
        Assert.Equal("published", schedule["status"]!.GetValue<string>());
        // 契約守法只開 format: date 的檢查，date-time 的形狀在這裡守：UTC、可解析
        var publishedAt = DateTimeOffset.Parse(schedule["publishedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, publishedAt.Offset);
        Assert.Equal(publishedAt, DateTimeOffset.Parse(body["publishedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task publish_該月沒有值班表_404()
    {
        await PostAsync("/api/schedules/2030-02/publish", null, "publishSchedule", HttpStatusCode.NotFound);
        // fetch 帶了 Content-Type 卻沒帶 body：本體可省略，仍該走到 404 而不是 422
        await PostAsync("/api/schedules/2030-02/publish", "", "publishSchedule", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task 本體不是_application_json_422()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/schedules/2030-03/duties")
        {
            Content = new StringContent("areaId=area-a", System.Text.Encoding.UTF8, "text/plain"),
        };
        using var response = await _api.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("INVALID_REQUEST", body["error"]!["code"]!.GetValue<string>());
    }

    // -- 不可排班日 ---------------------------------------------------------

    [Fact]
    public async Task setBlockedDay_與_clearBlockedDay_統計()
    {
        var set = await PutAsync("/api/blocked-days/2027-06/s-r2/2027-06-10", null, "setBlockedDay");
        Assert.Equal(1, set["staffTotals"]!["count"]!.GetValue<int>());
        Assert.Equal(15, set["staffTotals"]!["remaining"]!.GetValue<int>());
        Assert.Equal(1, set["dateTotals"]!["count"]!.GetValue<int>());

        var cleared = await _api.CallAsync(HttpMethod.Delete, "/api/blocked-days/2027-06/s-r2/2027-06-10", "clearBlockedDay", HttpStatusCode.OK);
        Assert.Equal(0, cleared["staffTotals"]!["count"]!.GetValue<int>());
        Assert.Equal(16, cleared["staffTotals"]!["remaining"]!.GetValue<int>());

        // 未登記的格子再清一次仍是 200
        await _api.CallAsync(HttpMethod.Delete, "/api/blocked-days/2027-06/s-r2/2027-06-10", "clearBlockedDay", HttpStatusCode.OK);
    }

    [Fact]
    public async Task setBlockedDay_超過上限_409()
    {
        for (var day = 1; day <= 16; day++)
        {
            await PutAsync($"/api/blocked-days/2027-07/s-r3/2027-07-{day:00}", null, "setBlockedDay");
        }

        var body = await PutAsync("/api/blocked-days/2027-07/s-r3/2027-07-20", null, "setBlockedDay", HttpStatusCode.Conflict);
        Assert.Equal("BLOCKED_DAY_CAP_EXCEEDED", body["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task setBlockedDay_人員不存在_404_日期不在該月_422()
    {
        await PutAsync("/api/blocked-days/2027-08/s-nope/2027-08-10", null, "setBlockedDay", HttpStatusCode.NotFound);
        await PutAsync("/api/blocked-days/2027-08/s-r2/2027-09-10", null, "setBlockedDay", HttpStatusCode.UnprocessableEntity);
    }

    // -- 設定 ---------------------------------------------------------------

    [Fact]
    public async Task putAreaSettings_原樣放回_200_刪掉被引用的區域_409()
    {
        var current = await _api.GetAsync("/api/settings/areas", "getAreaSettings");
        var same = await PutAsync("/api/settings/areas", current.ToJsonString(), "putAreaSettings");
        Assert.Equal(current.ToJsonString(), same.ToJsonString());

        var areas = current["areas"]!.AsArray();
        var without = current.DeepClone();
        without["areas"]!.AsArray().RemoveAt(areas.Select((a, i) => (a, i)).First(x => x.a!["id"]!.GetValue<string>() == "area-a").i);
        var conflict = await PutAsync("/api/settings/areas", without.ToJsonString(), "putAreaSettings", HttpStatusCode.Conflict);
        Assert.Equal("AREA_IN_USE", conflict["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task putAreaSettings_刪掉仍被引用的區域類型_409_指到未知類型_422()
    {
        var current = await _api.GetAsync("/api/settings/areas", "getAreaSettings");
        var typeRemoved = current.DeepClone();
        var types = typeRemoved["areaTypes"]!.AsArray();
        types.RemoveAt(types.Select((t, i) => (t, i)).First(x => x.t!["code"]!.GetValue<string>() == "ICU").i);
        var conflict = await PutAsync("/api/settings/areas", typeRemoved.ToJsonString(), "putAreaSettings", HttpStatusCode.Conflict);
        Assert.Equal("AREA_TYPE_IN_USE", conflict["error"]!["code"]!.GetValue<string>());

        var unknown = current.DeepClone();
        unknown["areas"]![0]!["areaTypeCode"] = "NOPE";
        await PutAsync("/api/settings/areas", unknown.ToJsonString(), "putAreaSettings", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task putRankSettings_原樣放回_200_刪掉被引用的身分_409()
    {
        var current = await _api.GetAsync("/api/settings/ranks", "getRankSettings");
        await PutAsync("/api/settings/ranks", current.ToJsonString(), "putRankSettings");

        var without = current.DeepClone();
        var ranks = without["ranks"]!.AsArray();
        ranks.RemoveAt(ranks.Select((r, i) => (r, i)).First(x => x.r!["code"]!.GetValue<string>() == "R2").i);
        var conflict = await PutAsync("/api/settings/ranks", without.ToJsonString(), "putRankSettings", HttpStatusCode.Conflict);
        Assert.Equal("RANK_IN_USE", conflict["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task putEligibilityMatrix_原樣放回_200()
    {
        var current = await _api.GetAsync("/api/settings/eligibility-matrix", "getEligibilityMatrix");
        var same = await PutAsync("/api/settings/eligibility-matrix", current.ToJsonString(), "putEligibilityMatrix");
        Assert.Equal(current.ToJsonString(), same.ToJsonString());
    }

    [Fact]
    public async Task putPointRules_原樣放回_200_查表缺列_422()
    {
        var current = await _api.GetAsync("/api/settings/point-rules", "getPointRules");
        var same = await PutAsync("/api/settings/point-rules", current.ToJsonString(), "putPointRules");
        Assert.Equal(current.ToJsonString(), same.ToJsonString());

        var broken = current.DeepClone();
        broken["fairness"]!["tables"]!["A"]!.AsArray().RemoveAt(0);
        await PutAsync("/api/settings/point-rules", broken.ToJsonString(), "putPointRules", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task putConstraints_原樣放回_200_空範圍正規化_不認得的原語_422()
    {
        var current = await _api.GetAsync("/api/settings/constraints", "getConstraints");
        var same = await PutAsync("/api/settings/constraints", current.ToJsonString(), "putConstraints");
        Assert.Equal(current.ToJsonString(), same.ToJsonString());

        var emptyScope = current.DeepClone();
        var h4 = emptyScope["hard"]!.AsArray().First(c => c!["code"]!.GetValue<string>() == "H4_MIN_GAP")!;
        h4["scope"] = new JsonObject { ["exemptRankCodes"] = new JsonArray() };
        try
        {
            var saved = await PutAsync("/api/settings/constraints", emptyScope.ToJsonString(), "putConstraints");
            var savedH4 = saved["hard"]!.AsArray().First(c => c!["code"]!.GetValue<string>() == "H4_MIN_GAP")!;
            Assert.False(savedH4.AsObject().ContainsKey("scope"), "空陣列應正規化成不限，整個 scope 省略");
        }
        finally
        {
            // 還原，別讓同一顆資料庫上的其他測試看到被改過的 NP 規則
            await PutAsync("/api/settings/constraints", current.ToJsonString(), "putConstraints");
        }

        var badPrimitive = current.DeepClone();
        badPrimitive["hard"]![0]!["primitive"] = "Magic";
        await PutAsync("/api/settings/constraints", badPrimitive.ToJsonString(), "putConstraints", HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>壞輸入不能被靜默接受：缺必要欄位、超出上界、Budget 缺 cap，都要 422 且設定不變。</summary>
    [Theory]
    [InlineData("hard", "H1_AREA_COVERAGE", "enabled", null)]              // 缺 enabled → 不能靜默停用硬約束
    [InlineData("soft", "S1_QUOTA_FAIRNESS", "weight", null)]              // 缺 weight → 不能靜默變 0
    [InlineData("soft", "S1_QUOTA_FAIRNESS", "weight", 101)]
    [InlineData("hard", "H4_MIN_GAP", "params", "{}")]                     // MinGap 缺 days
    [InlineData("hard", "H4_MIN_GAP", "params", "{\"days\": 4000000}")]    // 天數溢位
    [InlineData("hard", "H6_NP_MONTHLY_DAYS", "params", "{}")]             // Budget(duty_day) 缺 cap → 規則靜默失效
    public async Task putConstraints_壞輸入_422_設定不變(string list, string code, string field, object? value)
    {
        var current = await _api.GetAsync("/api/settings/constraints", "getConstraints");
        var broken = current.DeepClone();
        var target = broken[list]!.AsArray().First(c => c!["code"]!.GetValue<string>() == code)!.AsObject();
        target.Remove(field);
        if (value is int n)
        {
            target[field] = n;
        }
        else if (value is string json)
        {
            target[field] = JsonNode.Parse(json);
        }

        await PutAsync("/api/settings/constraints", broken.ToJsonString(), "putConstraints", HttpStatusCode.UnprocessableEntity);
        var after = await _api.GetAsync("/api/settings/constraints", "getConstraints");
        Assert.Equal(current.ToJsonString(), after.ToJsonString());
    }

    [Theory]
    [InlineData("quota", "holiday", null)]                                 // 缺 holiday → 不能靜默變 0
    [InlineData("quota", "holiday", -1)]
    [InlineData("fairness", "consecutiveSaturdayBonus", "{\"points\": 1, \"windowDays\": 4000000}")] // 視窗溢位
    [InlineData("fairness", "consecutiveSaturdayBonus", "{\"points\": 1}")] // 缺 windowDays
    public async Task putPointRules_壞輸入_422_設定不變(string section, string field, object? value)
    {
        var current = await _api.GetAsync("/api/settings/point-rules", "getPointRules");
        var broken = current.DeepClone();
        var target = broken[section]!.AsObject();
        target.Remove(field);
        if (value is int n)
        {
            target[field] = n;
        }
        else if (value is string json)
        {
            target[field] = JsonNode.Parse(json);
        }

        await PutAsync("/api/settings/point-rules", broken.ToJsonString(), "putPointRules", HttpStatusCode.UnprocessableEntity);
        var after = await _api.GetAsync("/api/settings/point-rules", "getPointRules");
        Assert.Equal(current.ToJsonString(), after.ToJsonString());
        // 讀取路徑沒被弄壞
        await _api.GetAsync("/api/schedules/2026-09", "getSchedule");
    }

    [Fact]
    public async Task putEligibilityMatrix_指到不存在的身分或類型_422()
    {
        var current = await _api.GetAsync("/api/settings/eligibility-matrix", "getEligibilityMatrix");
        var unknownRank = current.DeepClone();
        unknownRank["matrix"]!["R9"] = new JsonObject { ["WARD"] = true };
        await PutAsync("/api/settings/eligibility-matrix", unknownRank.ToJsonString(), "putEligibilityMatrix", HttpStatusCode.UnprocessableEntity);

        var unknownType = current.DeepClone();
        unknownType["matrix"]!["R2"]!["NOPE"] = true;
        await PutAsync("/api/settings/eligibility-matrix", unknownType.ToJsonString(), "putEligibilityMatrix", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task putMonthlyOverride_路徑月份為準()
    {
        var body = await PutAsync("/api/settings/monthly-overrides/2027-09", """{"yearMonth":"2000-01","quotaCapByRank":{"R6":3}}""", "putMonthlyOverride");
        Assert.Equal("2027-09", body["yearMonth"]!.GetValue<string>());
        Assert.Equal(3, body["quotaCapByRank"]!["R6"]!.GetValue<int>());

        var read = await _api.GetAsync("/api/settings/monthly-overrides/2027-09", "getMonthlyOverride");
        Assert.Equal(3, read["quotaCapByRank"]!["R6"]!.GetValue<int>());
    }

    // -- 行事曆 -------------------------------------------------------------

    [Fact]
    public async Task overrideCalendarDay_補班日_視為平日_年份與日期不一致_422()
    {
        var body = await PatchAsync("/api/calendars/2027/2027-10-09", """{"isMakeUpWorkday":true}""", "overrideCalendarDay");
        Assert.False(body["isHoliday"]!.GetValue<bool>());
        Assert.True(body["isMakeUpWorkday"]!.GetValue<bool>());
        Assert.True(body["overridden"]!.GetValue<bool>());
        Assert.Equal(1, body["quotaPointValue"]!.GetValue<int>());

        await PatchAsync("/api/calendars/2026/2027-10-09", """{"isHoliday":true}""", "overrideCalendarDay", HttpStatusCode.UnprocessableEntity);
        await PatchAsync("/api/calendars/2027/2027-10-09", """{"isHoliday":"yes"}""", "overrideCalendarDay", HttpStatusCode.UnprocessableEntity);
    }

    // -- 人員 ---------------------------------------------------------------

    [Fact]
    public async Task 人員_新增_201_編輯_狀態_刪除_204()
    {
        var created = await PostAsync("/api/staff", """{"employeeNo":"E900","name":"新人","rankCode":"R2"}""", "createStaff", HttpStatusCode.Created);
        var id = created["id"]!.GetValue<string>();
        Assert.Equal("active", created["status"]!.GetValue<string>());
        Assert.Equivalent(new[] { "WARD", "ICU" }, created["eligibleAreaTypes"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());

        var updated = await PatchAsync($"/api/staff/{id}", """{"employeeNo":"E900","name":"改名","rankCode":"R4"}""", "updateStaff");
        Assert.Equal("改名", updated["name"]!.GetValue<string>());
        Assert.Contains("CHIEF", updated["eligibleAreaTypes"]!.AsArray().Select(x => x!.GetValue<string>()));

        var inactive = await PatchAsync($"/api/staff/{id}/status", """{"status":"inactive"}""", "setStaffStatus");
        Assert.Equal("inactive", inactive["status"]!.GetValue<string>());
        await PatchAsync($"/api/staff/{id}/status", """{"status":"gone"}""", "setStaffStatus", HttpStatusCode.UnprocessableEntity);

        // 刪除要級聯清掉他的不可排班日（走真的 repository）
        await PutAsync($"/api/blocked-days/2027-11/{id}/2027-11-03", null, "setBlockedDay");
        await _api.SendAsync(HttpMethod.Delete, $"/api/staff/{id}", null, "deleteStaff", HttpStatusCode.NoContent);
        await _api.SendAsync(HttpMethod.Delete, $"/api/staff/{id}", null, "deleteStaff", HttpStatusCode.NotFound);
        var registration = await _api.GetAsync("/api/blocked-days/2027-11", "getBlockedDays");
        Assert.DoesNotContain(registration["entries"]!.AsArray(), e => e!["staffId"]!.GetValue<string>() == id);
    }

    [Fact]
    public async Task 人員_員編重複_409_有值班紀錄不可刪_409()
    {
        var dup = await PostAsync("/api/staff", """{"employeeNo":"E001","name":"撞員編","rankCode":"R2"}""", "createStaff", HttpStatusCode.Conflict);
        Assert.Equal("EMPLOYEE_NO_TAKEN", dup["error"]!["code"]!.GetValue<string>());

        var hasDuties = await _api.SendAsync(HttpMethod.Delete, "/api/staff/s-r2", null, "deleteStaff", HttpStatusCode.Conflict);
        Assert.Equal("STAFF_HAS_DUTIES", hasDuties["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task 人員_更新不存在_404_缺欄位_422()
    {
        await PatchAsync("/api/staff/s-nope", """{"employeeNo":"E1","name":"x","rankCode":"R2"}""", "updateStaff", HttpStatusCode.NotFound);
        await PostAsync("/api/staff", """{"employeeNo":"E901","rankCode":"R2"}""", "createStaff", HttpStatusCode.UnprocessableEntity);
    }
}
