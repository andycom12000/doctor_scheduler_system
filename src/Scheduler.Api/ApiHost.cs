using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Api.Endpoints;
using Scheduler.Api.Http;
using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.People;
using Scheduler.Application.Schedules;
using Scheduler.Application.Scheduling;
using Scheduler.Application.Settings;
using Scheduler.Persistence;

namespace Scheduler.Api;

/// <summary>
/// <see cref="ApiHost.BuildAsync"/> 的選項。
/// </summary>
/// <param name="UseTestServer">
/// true 時不開 socket，用 <c>Microsoft.AspNetCore.TestHost</c> 在 process 內跑；正式版的 Scheduler.Shell
/// 與契約守法測試都走這條（ARCHITECTURE §6.2）。false 是開發期 <c>dotnet run</c> 的 :5080。
/// </param>
/// <param name="DatabasePath">SQLite 檔案位置；預設是程式旁的 <c>data/scheduler.db</c>（portable 硬性要求）。</param>
/// <param name="ConfigurePersistence">
/// 覆寫 Persistence 的註冊（測試用 in-memory 資料庫：自己 <c>AddSchedulerPersistence(connection)</c>）。
/// 有給時 <paramref name="DatabasePath"/> 不看。用委派而不是直接收連線型別，Api 的公開介面才不會漏出 Persistence 的型別。
/// </param>
/// <param name="Args">命令列參數，開發期 <c>dotnet run</c> 用。</param>
public sealed record ApiHostOptions(
    bool UseTestServer = false,
    string? DatabasePath = null,
    Action<IServiceCollection>? ConfigurePersistence = null,
    string[]? Args = null);

/// <summary>
/// 唯一一份 HTTP pipeline 的組裝（ARCHITECTURE §3.2 規則 2）。開發期 Program.cs 與正式版 Shell
/// 都從這裡拿同一個 <see cref="WebApplication"/>；資料庫啟動流程也在這裡跑完，Shell 不需要看到 Persistence。
/// </summary>
public static class ApiHost
{
    public static async Task<WebApplication> BuildAsync(ApiHostOptions options, CancellationToken cancellationToken = default)
    {
        // ContentRoot 預設是目前工作目錄；WPF exe 由捷徑啟動時那可以是任何地方。
        // portable 的前提是一切都在程式旁，與 SchedulerDatabase.DefaultPath 一樣以 AppContext.BaseDirectory 為準。
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = options.Args ?? Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
        });

        if (options.UseTestServer)
        {
            builder.WebHost.UseTestServer();
        }

        // 開發期 Vite dev server 以 proxy 轉送 /api，同源；正式版是 WebView2 攔截。都不需要 CORS。

        if (options.ConfigurePersistence is not null)
        {
            options.ConfigurePersistence(builder.Services);
        }
        else
        {
            builder.Services.AddSchedulerPersistence(options.DatabasePath ?? SchedulerDatabase.DefaultPath);
        }

        AddApplication(builder.Services);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DictionaryKeyPolicy = null;
            // 契約裡 [X, null] 的必要欄位要輸出 null；可省略的欄位在 DTO 上逐一標 WhenWritingNull。
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        var app = builder.Build();

        // 啟動流程：建 data/ → 套 migration → WAL → 首次 seed → 標記中斷的求解工作。可重複執行。
        await SchedulerDatabase.InitializeAsync(app.Services, cancellationToken);

        app.UseSchedulerErrors();
        app.MapReadEndpoints();
        app.MapWriteEndpoints();

        return app;
    }

    /// <summary>Application 零套件相依，DI 註冊住在這裡。與 repository 同為 scoped。</summary>
    private static void AddApplication(IServiceCollection services)
    {
        services.AddScoped<SchedulingContextLoader>();
        services.AddScoped<ScheduleQueries>();
        services.AddScoped<BlockedDayQueries>();
        services.AddScoped<StaffQueries>();
        services.AddScoped<CalendarQueries>();
        services.AddScoped<ScheduleCommands>();
        services.AddScoped<BlockedDayCommands>();
        services.AddScoped<SettingsCommands>();
        services.AddScoped<CalendarCommands>();
        services.AddScoped<StaffCommands>();
        // 發布時間戳由這裡拿，測試可換成固定時鐘。
        services.TryAddSingleton(TimeProvider.System);
    }
}
