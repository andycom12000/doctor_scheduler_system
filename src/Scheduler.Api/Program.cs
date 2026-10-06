// 唯一一份 HTTP 實作（docs/ARCHITECTURE.md §3.2 硬性規則 #2）。
//
// 開發期直接跑（:5080），正式版由 Scheduler.Shell 用 TestHost 在 process 內 host 同一個
// WebApplication。pipeline 的組裝在 ApiHost，這裡只剩開發期的進入點。
// 任何業務邏輯都不得寫在 Api —— 端點只做「解參數 → 呼叫 Application → 包回應」。

using Scheduler.Api;

// SeedReferenceRoster 預設是 false（fail-safe，#37）；這裡明確開回 true，因為這個進入點只有
// dotnet run 的開發期 :5080 會用，前端 npm run dev 與 frontend/scripts/smoke-mock.ts 的
// api:smoke 都預期打到真後端時人員名冊有 34 人。
//
// 畫面層 E2E（frontend/scripts/e2e.ts，#84）要一顆全新資料庫、空名冊的後端，用兩個環境變數切換；
// 沒設就是上面的預設（data/scheduler.db + 參考名單），只影響這個開發期進入點，不影響 Shell。
//   SCHEDULER_DATABASE_PATH         SQLite 檔案路徑（沒設＝程式旁的 data/scheduler.db）
//   SCHEDULER_SEED_REFERENCE_ROSTER true／false（不分大小寫）；false 就不種參考名單（沒設＝true，其他值丟例外）
//   SCHEDULER_CALENDAR_AUTO_SYNC    true／false；false 就不在啟動時連外網更新行事曆（沒設＝true，#112）。e2e 設 false
var databasePath = Environment.GetEnvironmentVariable("SCHEDULER_DATABASE_PATH");
var seedFlag = Environment.GetEnvironmentVariable("SCHEDULER_SEED_REFERENCE_ROSTER");
var seedReferenceRoster = string.IsNullOrWhiteSpace(seedFlag)
    || (bool.TryParse(seedFlag, out var parsedSeed)
        ? parsedSeed
        : throw new ArgumentException($"SCHEDULER_SEED_REFERENCE_ROSTER 只接受 true 或 false，收到「{seedFlag}」"));

var syncFlag = Environment.GetEnvironmentVariable("SCHEDULER_CALENDAR_AUTO_SYNC");
var calendarAutoSync = string.IsNullOrWhiteSpace(syncFlag)
    || (bool.TryParse(syncFlag, out var parsedSync)
        ? parsedSync
        : throw new ArgumentException($"SCHEDULER_CALENDAR_AUTO_SYNC 只接受 true 或 false，收到「{syncFlag}」"));

var app = await ApiHost.BuildAsync(new ApiHostOptions(
    Args: args,
    DatabasePath: string.IsNullOrWhiteSpace(databasePath) ? null : databasePath,
    SeedReferenceRoster: seedReferenceRoster,
    CalendarAutoSync: calendarAutoSync));
app.Run();
