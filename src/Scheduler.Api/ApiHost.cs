using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Api.Endpoints;
using Scheduler.Api.Http;
using Scheduler.Api.Solving;
using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.People;
using Scheduler.Application.Schedules;
using Scheduler.Application.Scheduling;
using Scheduler.Application.Settings;
using Scheduler.Application.Solving;
using Scheduler.Persistence;
using Scheduler.Solver;

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
/// <param name="ConfigureServices">
/// 在 Application 與 Solver 註冊之後再跑的覆寫，測試用來把 <see cref="ISolver"/> 換成假的。
/// </param>
/// <param name="Args">命令列參數，開發期 <c>dotnet run</c> 用。</param>
/// <param name="SeedReferenceRoster">
/// 首次啟動時是否種參考名單 34 人（#37）。**預設 false，fail-safe**：不經 Shell、直接部署
/// `Scheduler.Api` 的遷移路徑（§3.2 硬性規則 2 講的那條）不該預設出貨一份假名單。
/// 開發期 <c>Program.cs</c> 明確傳 true（給 `npm run dev`、`smoke-mock.ts` 的 34 人斷言用）；
/// `Scheduler.Shell` 依 DEBUG/RELEASE 編譯期決定，Debug 傳 true、Release 傳 false。
/// 行事曆例外日、約束等其他出廠設定不受這個旗標影響，永遠照常種。
/// </param>
/// <param name="RosterFilePath">
/// 發佈包的名冊檔路徑（#82，BCL 型別，Shell 才傳得進來）。名冊來源三選一：都不給＝名冊空的（預設）、
/// <paramref name="SeedReferenceRoster"/>＝假名參考名單（開發期）、這個＝從檔案匯入（Release）。
/// 檔案存在、資料庫從沒匯入過、人員表空的才匯入，匯入後寫標記不再匯入；檔案不存在或驗證不過就不匯入、正常啟動。
/// 與 <paramref name="SeedReferenceRoster"/> 同時指定會丟 <see cref="ArgumentException"/>。
/// </param>
public sealed record ApiHostOptions(
    bool UseTestServer = false,
    string? DatabasePath = null,
    Action<IServiceCollection>? ConfigurePersistence = null,
    Action<IServiceCollection>? ConfigureServices = null,
    string[]? Args = null,
    bool SeedReferenceRoster = false,
    string? RosterFilePath = null);

/// <summary>
/// 唯一一份 HTTP pipeline 的組裝（ARCHITECTURE §3.2 規則 2）。開發期 Program.cs 與正式版 Shell
/// 都從這裡拿同一個 <see cref="WebApplication"/>；資料庫啟動流程也在這裡跑完，Shell 不需要看到 Persistence。
/// </summary>
public static class ApiHost
{
    /// <summary>
    /// 發佈包內名冊檔的相對路徑（相對程式資料夾，#82）。唯一一份，來自 Persistence 的 <c>RosterImporter</c>；
    /// Shell 看不到 Persistence，從這裡拿（BCL string），<c>build/publish.ps1</c> 複製的目的地也要與它一致。
    /// </summary>
    public static string RosterFileRelativePath => Scheduler.Persistence.Seed.RosterImporter.RelativePath;

    public static async Task<WebApplication> BuildAsync(ApiHostOptions options, CancellationToken cancellationToken = default)
    {
        // 名冊來源二選一：放在最前面，免得建好 host 才發現選項矛盾
        if (options.SeedReferenceRoster && options.RosterFilePath is not null)
        {
            throw new ArgumentException("SeedReferenceRoster 與 RosterFilePath 不可同時指定", nameof(options));
        }

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
        options.ConfigureServices?.Invoke(builder.Services);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DictionaryKeyPolicy = null;
            // 契約裡 [X, null] 的必要欄位要輸出 null；可省略的欄位在 DTO 上逐一標 WhenWritingNull。
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        var app = builder.Build();

        // 啟動流程：建 data/ → 套 migration → WAL → 首次 seed → 標記中斷的求解工作。可重複執行。
        await SchedulerDatabase.InitializeAsync(
            app.Services, options.SeedReferenceRoster, cancellationToken, options.RosterFilePath);

        app.UseSchedulerErrors();
        app.MapReadEndpoints();
        app.MapWriteEndpoints();
        app.MapSolverEndpoints();

        return app;
    }

    /// <summary>Application 零套件相依，DI 註冊住在這裡。與 repository 同為 scoped。</summary>
    private static void AddApplication(IServiceCollection services)
    {
        services.AddScoped<SchedulingContextLoader>();
        services.AddScoped<ScheduleQueries>();
        services.AddScoped<ScheduleExportQueries>();
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

        // 求解：迴圈與狀態機是 singleton（單一 slot），CP-SAT 實作只在 Solver 專案；背景落盤透過 scope factory 拿 scoped 服務。
        services.TryAddSingleton<ISolver, CpSatSolver>();
        services.AddSingleton<ISolverScopeFactory, ServiceProviderSolverScopeFactory>();
        services.AddSingleton<SolverJobService>();
        // Shell 與 SSE 端點共用的進度事件來源，簽章只有 BCL 型別（§6.2）
        services.AddSingleton<ISolverProgressFeed, SolverProgressFeed>();
    }
}
