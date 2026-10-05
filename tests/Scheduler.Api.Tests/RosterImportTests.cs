using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace Scheduler.Api.Tests;

/// <summary>
/// #82：發佈包第一次啟動從名冊檔匯入、只匯一次。用暫存目錄裡的檔案資料庫（in-memory 撐不過「重新啟動」），
/// 每次 <see cref="StartAsync"/> 等於 Release Shell 啟動一次。名冊內容全是假名與 <c>T001…</c> 員編，真實名冊不進版控。
/// </summary>
public sealed class RosterImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "roster-import-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "data", "scheduler.db");
    private string RosterPath => Path.Combine(_dir, "roster", "roster.csv");

    public RosterImportTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "roster"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string ValidRoster =
        "員編,姓名,身分\r\nT001,測試甲,PGY1\r\nT002,測試乙,R3\r\nT003,測試丙,PTR\r\n";

    private void WriteRoster(string content, bool bom = false) =>
        File.WriteAllText(RosterPath, content, new UTF8Encoding(bom));

    private async Task<WebApplication> StartAsync(string? rosterPath)
    {
        var app = await ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true, DatabasePath: DbPath, RosterFilePath: rosterPath));
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonArray> ListStaffAsync(WebApplication app)
    {
        using var client = app.GetTestClient();
        var response = await client.GetAsync("/api/staff");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["items"]!.AsArray();
    }

    private static async Task StopAsync(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 第一次啟動匯入_員編姓名身分逐筆相同_含BOM(bool bom)
    {
        WriteRoster(ValidRoster, bom);
        var app = await StartAsync(RosterPath);
        try
        {
            var items = await ListStaffAsync(app);
            Assert.Equal(3, items.Count);
            var rows = items.Select(i => (i!["employeeNo"]!.GetValue<string>(), i["name"]!.GetValue<string>(), i["rankCode"]!.GetValue<string>(), i["status"]!.GetValue<string>()))
                .OrderBy(r => r.Item1).ToArray();
            Assert.Equal(("T001", "測試甲", "PGY1", "active"), rows[0]);
            Assert.Equal(("T002", "測試乙", "R3", "active"), rows[1]);
            Assert.Equal(("T003", "測試丙", "PTR", "active"), rows[2]);
            // 可值區域類型照資格矩陣推導，與 POST /api/staff 一樣
            Assert.NotEmpty(items.Single(i => i!["employeeNo"]!.GetValue<string>() == "T002")!["eligibleAreaTypes"]!.AsArray());
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task 人全刪掉再重新啟動_仍然是零人_名冊檔還在也不復活()
    {
        WriteRoster(ValidRoster);
        var app = await StartAsync(RosterPath);
        try
        {
            using var client = app.GetTestClient();
            foreach (var item in await ListStaffAsync(app))
            {
                var response = await client.DeleteAsync("/api/staff/" + item!["id"]!.GetValue<string>());
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
            Assert.Empty(await ListStaffAsync(app));
        }
        finally
        {
            await StopAsync(app);
        }

        SqliteConnection.ClearAllPools();
        var restarted = await StartAsync(RosterPath);
        try
        {
            Assert.Empty(await ListStaffAsync(restarted));
        }
        finally
        {
            await StopAsync(restarted);
        }
    }

    [Fact]
    public async Task 舊資料庫有人員但沒有標記_升級後不會被塞進名冊()
    {
        // 模擬舊版：先用開發期假名名單啟動（人員表有人、沒有匯入標記），再帶著名冊檔啟動
        var old = await ApiHost.BuildAsync(new ApiHostOptions(UseTestServer: true, DatabasePath: DbPath, SeedReferenceRoster: true));
        await old.StartAsync();
        int before;
        try { before = (await ListStaffAsync(old)).Count; }
        finally { await StopAsync(old); }
        Assert.Equal(34, before);

        SqliteConnection.ClearAllPools();
        WriteRoster(ValidRoster);
        var upgraded = await StartAsync(RosterPath);
        try
        {
            var items = await ListStaffAsync(upgraded);
            Assert.Equal(34, items.Count);
            Assert.DoesNotContain(items, i => i!["employeeNo"]!.GetValue<string>() == "T001");
        }
        finally
        {
            await StopAsync(upgraded);
        }
    }

    [Fact]
    public async Task 沒有名冊檔_正常啟動且名冊是空的_之後補檔仍會匯入()
    {
        var app = await StartAsync(RosterPath);
        try { Assert.Empty(await ListStaffAsync(app)); }
        finally { await StopAsync(app); }

        // 沒匯入過就沒有標記：補上檔案後下次啟動才匯入
        SqliteConnection.ClearAllPools();
        WriteRoster(ValidRoster);
        var again = await StartAsync(RosterPath);
        try { Assert.Equal(3, (await ListStaffAsync(again)).Count); }
        finally { await StopAsync(again); }
    }

    [Fact]
    public async Task 沒給名冊路徑_名冊是空的()
    {
        WriteRoster(ValidRoster);
        var app = await StartAsync(rosterPath: null);
        try { Assert.Empty(await ListStaffAsync(app)); }
        finally { await StopAsync(app); }
    }

    [Fact]
    public async Task 名冊檔驗證不過_不匯入不崩潰_留一筆紀錄_也不寫標記()
    {
        WriteRoster("員編,姓名,身分\nT001,測試甲,PGY1\nT001,測試乙,R3\nT003,測試丙,XX\n");
        var app = await StartAsync(RosterPath);
        try { Assert.Empty(await ListStaffAsync(app)); }
        finally { await StopAsync(app); }

        var log = await File.ReadAllTextAsync(Path.Combine(_dir, "data", "roster-import.log"));
        Assert.Contains("第 3 列", log);
        Assert.Contains("第 4 列", log);
        // 紀錄不洩漏姓名與員編
        Assert.DoesNotContain("測試甲", log);
        Assert.DoesNotContain("T001", log);

        // 修好檔案後重啟就匯入（失敗那次沒有寫標記）
        SqliteConnection.ClearAllPools();
        WriteRoster(ValidRoster);
        var fixedApp = await StartAsync(RosterPath);
        try { Assert.Equal(3, (await ListStaffAsync(fixedApp)).Count); }
        finally { await StopAsync(fixedApp); }
    }

    [Fact]
    public void 發佈腳本複製名冊檔的目的地與程式讀取的相對路徑一致()
    {
        // Shell 用 ApiHost.RosterFileRelativePath 組路徑（單一來源）；publish.ps1 是另一份寫死的，這裡守住不漂移
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "build", "publish.ps1")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var script = File.ReadAllText(Path.Combine(dir!, "build", "publish.ps1"));
        var relative = ApiHost.RosterFileRelativePath.Replace('\\', '/');
        Assert.Contains($"(Join-Path $OutputPath '{relative}')", script);
    }

    [Fact]
    public async Task 參考名單與名冊檔不可同時指定()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true, DatabasePath: DbPath, SeedReferenceRoster: true, RosterFilePath: RosterPath)));
    }
}
