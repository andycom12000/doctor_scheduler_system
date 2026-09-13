using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Model;
using Scheduler.Persistence;

namespace Scheduler.Api.Tests;

/// <summary>
/// 把 <see cref="ApiHost"/> 用 TestServer 跑起來（不開 socket，與正式版 Shell 同一條路），
/// 資料庫是一顆 SQLite in-memory：壽命等於連線壽命，所以連線由 fixture 開著。
/// 出廠 seed 含設定、行事曆與參考名單 34 人；<see cref="SeedAsync"/> 先清掉參考名單、
/// 換成這顆固定情境的 8 位人員，值班表也由它造，讓既有測試的人數與 id 維持不變。
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _app = await ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true,
            ConfigurePersistence: services => services.AddSchedulerPersistence(_connection)));
        await _app.StartAsync();
        Client = _app.GetTestClient();

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// 情境：8 位人員（總值、ICU、病房各有人，含 1 位 NP 與 1 位停用）；2026-08 已發布；
    /// 2026-09 草稿排了 9/1 全 5 區與 9/2 的兩格（同一人 9/1、9/2 連值，造一條硬違規）；
    /// 2026-10 尚無值班表但有一筆不可排班日登記。
    /// </summary>
    private async Task SeedAsync()
    {
        using var scope = _app.Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var schedules = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        var blockedDays = scope.ServiceProvider.GetRequiredService<IBlockedDayRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // 出廠 seed 帶參考名單 34 人；這顆情境要的是固定的 8 人，先清掉參考名單
        // 才不會撞員編、也不會把既有測試的人數／規模斷言全部改掉。
        foreach (var seeded in await staff.ListAsync())
        {
            await staff.RemoveAsync(seeded.Id);
        }

        await uow.CommitAsync();

        await staff.AddAsync(new Staff("s-r4", "E001", "王總值", "R4"));
        await staff.AddAsync(new Staff("s-r5", "E002", "李總值", "R5"));
        await staff.AddAsync(new Staff("s-r2", "E003", "陳中階", "R2"));
        await staff.AddAsync(new Staff("s-r3", "E004", "林中階", "R3"));
        await staff.AddAsync(new Staff("s-pgy1", "E005", "張新人", "PGY1"));
        await staff.AddAsync(new Staff("s-r1", "E006", "黃住院", "R1"));
        await staff.AddAsync(new Staff("s-np", "E007", "吳專師", "NP"));
        await staff.AddAsync(new Staff("s-gone", "E008", "已離職", "R1", StaffStatus.Inactive));

        var aug = new YearMonth(2026, 8);
        await schedules.UpsertAsync(new ScheduleHeader(aug, ScheduleStatus.Published, 3, new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.FromHours(8))));
        await schedules.SetDutyAsync(aug, "area-chief", new DateOnly(2026, 8, 31), "s-r5");
        await schedules.ReplaceCarryOverAsync(aug, new[] { new CarryOverEntry("s-r4", 2) });

        var sep = new YearMonth(2026, 9);
        await schedules.UpsertAsync(ScheduleHeader.NewDraft(sep) with { Revision = 1 });
        await schedules.SetDutyAsync(sep, "area-a", new DateOnly(2026, 9, 1), "s-pgy1");
        await schedules.SetDutyAsync(sep, "area-b", new DateOnly(2026, 9, 1), "s-r1");
        await schedules.SetDutyAsync(sep, "area-c", new DateOnly(2026, 9, 1), "s-r3");
        await schedules.SetDutyAsync(sep, "area-icu", new DateOnly(2026, 9, 1), "s-r2");
        await schedules.SetDutyAsync(sep, "area-chief", new DateOnly(2026, 9, 1), "s-r4");
        await schedules.SetDutyAsync(sep, "area-a", new DateOnly(2026, 9, 2), "s-pgy1");
        await schedules.SetDutyAsync(sep, "area-chief", new DateOnly(2026, 9, 2), "s-r5");

        await blockedDays.AddAsync(new BlockedDay("s-r4", new DateOnly(2026, 10, 5)));

        await uow.CommitAsync();
    }

    /// <summary>打一個端點、驗狀態碼、用契約 schema 驗回應本體，回傳解析後的 JSON。</summary>
    public Task<JsonNode> CallAsync(HttpMethod method, string path, string operationId, HttpStatusCode expected) =>
        SendAsync(method, path, body: null, operationId, expected)!;

    /// <summary>帶 JSON 本體的版本。<paramref name="body"/> 是原始 JSON 字串，故意壞掉的本體也送得出去。</summary>
    public async Task<JsonNode> SendAsync(HttpMethod method, string path, string? body, string operationId, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{method} {path} 應回 {(int)expected}，實際 {(int)response.StatusCode}：{text}");
        if (expected == HttpStatusCode.NoContent)
        {
            Assert.Equal(string.Empty, text);
            Assert.True(ContractSchema.Current.HasResponse(operationId, 204), $"契約的 {operationId} 沒有定義 204");
            return new JsonObject();
        }

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = JsonNode.Parse(text);
        // 契約有定義該狀態碼就用它的 schema；沒列的（契約未列的狀態碼）用 ErrorResponse 元件驗。
        var errors = ContractSchema.Current.HasResponse(operationId, (int)expected)
            ? ContractSchema.Current.Validate(operationId, (int)expected, json)
            : (int)expected >= 400
                ? ContractSchema.Current.ValidateComponent("ErrorResponse", json)
                : throw new InvalidOperationException($"契約的 {operationId} 沒有定義 {(int)expected} 回應");
        Assert.True(errors.Count == 0, $"{method} {path} 的回應不符契約 {operationId}/{(int)expected}：\n" + string.Join("\n", errors) + "\n本體：" + text);
        return json!;
    }

    public Task<JsonNode> GetAsync(string path, string operationId, HttpStatusCode expected = HttpStatusCode.OK) =>
        CallAsync(HttpMethod.Get, path, operationId, expected);
}
