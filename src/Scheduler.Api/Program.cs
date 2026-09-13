// 唯一一份 HTTP 實作（docs/ARCHITECTURE.md §3.2 硬性規則 #2）。
//
// 開發期直接跑（:5080），正式版由 Scheduler.Shell 用 TestHost 在 process 內 host 同一個
// WebApplication。pipeline 的組裝在 ApiHost，這裡只剩開發期的進入點。
// 任何業務邏輯都不得寫在 Api —— 端點只做「解參數 → 呼叫 Application → 包回應」。

using Scheduler.Api;

// SeedReferenceRoster 預設是 false（fail-safe，#37）；這裡明確開回 true，因為這個進入點只有
// dotnet run 的開發期 :5080 會用，前端 npm run dev 與 frontend/scripts/smoke-mock.ts 的
// api:smoke 都預期打到真後端時人員名冊有 34 人。
var app = await ApiHost.BuildAsync(new ApiHostOptions(Args: args, SeedReferenceRoster: true));
app.Run();
