using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Persistence;

namespace Scheduler.Api.Tests;

/// <summary>
/// #37：<c>ApiHostOptions.SeedReferenceRoster</c> 要真的從 <see cref="ApiHost.BuildAsync"/> 一路
/// 傳到 <c>SchedulerDatabase.InitializeAsync</c>。自己一顆資料庫、自己組 host——不透過
/// <see cref="ApiFixture"/>：它也是用 <c>SeedReferenceRoster: false</c> 再灌自己固定情境的 8 人，
/// 這裡要驗證的是旗標本身有沒有生效，不想跟它共用同一份組裝邏輯。
/// </summary>
public sealed class SeedFlagTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _app = await ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true,
            SeedReferenceRoster: false,
            ConfigurePersistence: services => services.AddSchedulerPersistence(_connection)));
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void 預設值是_fail_safe_關閉的()
    {
        // 不經 Shell 直接部署 Scheduler.Api 的遷移路徑（§3.2 硬性規則 2）不該預設出貨假名單；
        // 開發期 Program.cs 與 Shell 的 DEBUG 分支都要明確開回 true 才種得到。
        Assert.False(new ApiHostOptions().SeedReferenceRoster);
    }

    [Fact]
    public async Task 旗標關閉時_listStaff回空_但行事曆與設定仍照常種()
    {
        using var client = _app.GetTestClient();

        var staffResponse = await client.GetAsync("/api/staff");
        Assert.Equal(HttpStatusCode.OK, staffResponse.StatusCode);
        var staff = JsonNode.Parse(await staffResponse.Content.ReadAsStringAsync())!;
        Assert.Empty(staff["items"]!.AsArray());

        var areasResponse = await client.GetAsync("/api/settings/areas");
        Assert.Equal(HttpStatusCode.OK, areasResponse.StatusCode);
        var areas = JsonNode.Parse(await areasResponse.Content.ReadAsStringAsync())!;
        Assert.NotEmpty(areas["areas"]!.AsArray());

        var calendarResponse = await client.GetAsync("/api/calendars/2026");
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        var calendar = JsonNode.Parse(await calendarResponse.Content.ReadAsStringAsync())!;
        Assert.NotEmpty(calendar["days"]!.AsArray());
    }
}
