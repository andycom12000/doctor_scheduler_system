// 唯一一份 HTTP 實作（docs/ARCHITECTURE.md §3.2 硬性規則 #2）。
//
// 開發期直接跑（:5080），正式版由 Scheduler.Shell 用 TestHost 在 process 內 host 同一個
// WebApplication。任何業務邏輯都不得寫在這裡 —— 端點只做「解參數 → 呼叫 Application → 包回應」。
// 遷移到前後端分離時，刪掉 Scheduler.Shell 即可，本專案原封不動。
//
// 端點尚未實作：目前只有 /api/health，其餘見 api-contract.yaml。

using Scheduler.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 開發期 Vite dev server 以 proxy 轉送 /api，同源，故不需 CORS。
// 若前端改為直接跨源呼叫，在此加入具名 CORS policy。

// 資料庫在程式旁的 data/scheduler.db（portable 硬性要求）。
builder.Services.AddSchedulerPersistence(SchedulerDatabase.DefaultPath);

var app = builder.Build();

// 啟動流程：建 data/ → 套 migration → WAL → 首次 seed → 標記中斷的求解工作。可重複執行。
await SchedulerDatabase.InitializeAsync(app.Services);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
