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
/// <see cref="ApiFixture"/>，它固定用 true 且會先清空再灌回固定情境的 8 人。
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
