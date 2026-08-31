// 開發期 transport 主機。
//
// 硬性規則 #2：本專案與 Scheduler.Shell 是兩個 transport，共用同一組 handler。
// 任何業務邏輯都不得寫在這裡 —— 端點只做 HTTP ←→ Scheduler.Application 的轉換。
// 遷移到前後端分離時，刪掉 Scheduler.Shell 即可，本專案原封不動。
//
// 端點尚未實作：領域模型與 API 契約由下一階段規劃，見 api-contract.yaml。

var builder = WebApplication.CreateBuilder(args);

// 開發期 Vite dev server 以 proxy 轉送 /api，同源，故不需 CORS。
// 若前端改為直接跨源呼叫，在此加入具名 CORS policy。

var app = builder.Build();

// api-contract.yaml 目前唯一定義的端點。
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
