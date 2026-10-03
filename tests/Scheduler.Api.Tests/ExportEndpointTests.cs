using System.Net;
using ClosedXML.Excel;

namespace Scheduler.Api.Tests;

/// <summary>
/// <c>exportSchedule</c>：唯一回位元組而非 JSON 的端點，所以不走 <see cref="ApiFixture.GetAsync"/>，
/// 自己驗狀態碼、內容類型、下載檔名，再把 xlsx 開回來看一格。錯誤回應仍是 JSON，照契約驗。
/// </summary>
public sealed class ExportEndpointTests : IClassFixture<ApiFixture>
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ApiFixture _api;

    public ExportEndpointTests(ApiFixture api)
    {
        _api = api;
    }

    [Fact]
    public async Task 預設版面_區域乘日_回_xlsx_附下載檔名()
    {
        using var response = await _api.Client.GetAsync("/api/schedules/2026-09/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(ContractSchema.Current.HasResponse("exportSchedule", 200));
        Assert.Equal(Xlsx, response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("duty-2026-09.xlsx", disposition.FileName!.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x50, 0x4B }, bytes[..2]); // zip 魔數
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.Single();
        Assert.Equal("區域", sheet.Cell(1, 1).GetString());
        Assert.Equal("9/1 (二)", sheet.Cell(1, 2).GetString());
        Assert.Equal("A", sheet.Cell(2, 1).GetString());
        Assert.Equal("張新人", sheet.Cell(2, 2).GetString()); // fixture：area-a 9/1 是 s-pgy1
        Assert.Equal("總值", sheet.Cell(6, 1).GetString());
        Assert.Equal("李總值", sheet.Cell(6, 3).GetString()); // area-chief 9/2 是 s-r5

        // 9/5 是週六：整欄上底色、只到表格範圍；9/1 平日不上色
        var holiday = XLColor.FromArgb(0xFF, 0xF2, 0xF2);
        Assert.Equal(holiday, sheet.Cell(1, 6).Style.Fill.BackgroundColor);
        Assert.Equal(holiday, sheet.Cell(6, 6).Style.Fill.BackgroundColor);
        Assert.NotEqual(holiday, sheet.Cell(7, 6).Style.Fill.BackgroundColor);
        Assert.NotEqual(holiday, sheet.Cell(2, 2).Style.Fill.BackgroundColor);
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.Equal(1, sheet.SheetView.SplitColumn);
    }

    [Fact]
    public async Task 日乘人員版面_列是日_欄是人員_格子是區域代碼()
    {
        using var response = await _api.Client.GetAsync("/api/schedules/2026-09/export?layout=day-by-staff");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Xlsx, response.Content.Headers.ContentType!.MediaType);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheets.Single();
        Assert.Equal("日期", sheet.Cell(1, 1).GetString());
        Assert.Equal("9/1 (二)", sheet.Cell(2, 1).GetString());

        var header = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
        var col = Array.IndexOf(header, "張新人") + 1;
        Assert.True(col > 1, "人員欄應有張新人");
        Assert.Equal("A", sheet.Cell(2, col).GetString());
        Assert.DoesNotContain(header, h => h.StartsWith("已離職", StringComparison.Ordinal)); // 停用且本月沒值班的人不出現

        // 9/5 週六那一列整列上底色、只到表格範圍
        var holiday = XLColor.FromArgb(0xFF, 0xF2, 0xF2);
        Assert.Equal(holiday, sheet.Cell(6, 1).Style.Fill.BackgroundColor);
        Assert.Equal(holiday, sheet.Cell(6, header.Length).Style.Fill.BackgroundColor);
        Assert.NotEqual(holiday, sheet.Cell(6, header.Length + 1).Style.Fill.BackgroundColor);
        Assert.NotEqual(holiday, sheet.Cell(2, 1).Style.Fill.BackgroundColor);
    }

    [Fact]
    public async Task 尚無值班表的月份_404_NOT_FOUND()
    {
        var body = await _api.GetAsync("/api/schedules/2030-01/export", "exportSchedule", HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", body["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task 有同人同日兩區_409_DOUBLE_BOOKING_PRESENT_排除後_200()
    {
        await _api.SendAsync(HttpMethod.Patch, "/api/schedules/2027-07/duties", """{"areaId":"area-icu","date":"2027-07-05","staffId":"s-r3"}""", "setDuty", HttpStatusCode.OK);
        await _api.SendAsync(HttpMethod.Patch, "/api/schedules/2027-07/duties", """{"areaId":"area-a","date":"2027-07-05","staffId":"s-r3"}""", "setDuty", HttpStatusCode.OK);

        var body = await _api.GetAsync("/api/schedules/2027-07/export", "exportSchedule", HttpStatusCode.Conflict);
        Assert.Equal("DOUBLE_BOOKING_PRESENT", body["error"]!["code"]!.GetValue<string>());
        Assert.Equal(1, body["error"]!["details"]!["doubleBookingCount"]!.GetValue<int>());

        await _api.SendAsync(HttpMethod.Patch, "/api/schedules/2027-07/duties", """{"areaId":"area-a","date":"2027-07-05","staffId":null}""", "setDuty", HttpStatusCode.OK);
        using var response = await _api.Client.GetAsync("/api/schedules/2027-07/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("by-magic")]
    [InlineData("")]
    public async Task layout不合法_422_INVALID_REQUEST(string layout)
    {
        var body = await _api.GetAsync($"/api/schedules/2026-09/export?layout={layout}", "exportSchedule", HttpStatusCode.UnprocessableEntity);
        Assert.Equal("INVALID_REQUEST", body["error"]!["code"]!.GetValue<string>());
    }
}
