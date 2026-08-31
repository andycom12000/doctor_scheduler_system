# CLAUDE.md

醫師值班排班求解器。OR-Tools CP-SAT + WPF/WebView2 殼 + Vite SPA，交付形態是 Windows portable。

## 先讀

- `docs/ARCHITECTURE.md` — 技術決策已定案。**第 2 節是「已排除方案」，提任何技術選型前先看過**，
  Blazor / Tauri / Electron / MAUI / Timefold / meta-framework 等都已評估並否決，理由都在裡面。
- `api-contract.yaml` — 前後端唯一耦合點。

## 現況

骨架階段。領域模型、CP-SAT 建模、API 契約、UI 規格**都還沒開始**，屬下一階段。
目前 `Scheduler.Domain` / `Application` / `Solver` 只有 `AssemblyMarker`，
`Scheduler.Shell` 只有空 WPF 視窗，契約只有 `/api/health`。

## 兩條硬性規則（違反時編譯會過，但架構測試會擋）

1. `Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools。
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離。
2. `Scheduler.Api` 與 `Scheduler.Shell` 是兩個 transport，共用同一組 handler。
   兩者都不得實作業務邏輯，只做 transport ↔ Application 的轉換。
   遷移到前後端分離時，刪掉 `Scheduler.Shell` 即可。

`tests/Scheduler.ArchitectureTests` 驗證規則 1。規則 2 目前沒有自動化驗證，靠 review。

## 指令

```bash
dotnet build                              # 建置全部
dotnet test                               # 架構規則驗證
dotnet run --project src/Scheduler.Api    # 開發期後端 :5080

cd frontend
npm run dev          # :5173，/api proxy 到 :5080
npm run dev:mock     # :5173，/api 由 MSW 攔截，不需後端
npm run build        # 型別檢查 + 建置到 ../src/Scheduler.Shell/wwwroot
npm run api:types    # 由 api-contract.yaml 生成 src/api/schema.d.ts
```

發佈：`pwsh build/fetch-webview2.ps1`（一次性）→ `pwsh build/publish.ps1`。

## 專案特有的坑

- **`Scheduler.Shell` 裡 `Application` 會撞名。** 本組件同時引用 `Scheduler.Application`
  命名空間與 `System.Windows.Application` 型別，基底型別必須完整限定。
- **不要開 trim / AOT。** OR-Tools 的 P/Invoke wrapper 兩者皆不相容，
  `Directory.Build.props` 已明確關閉。
- **不要改成 `PublishSingleFile`。** 自解壓到 `%TEMP%` 會觸發 AppLocker / EDR。
- **所有執行期狀態寫在程式旁的 `data/`**，不得碰 `%APPDATA%` / `%LOCALAPPDATA%` / 登錄檔。
  這是 portable 的硬性要求，也是驗收項目。
- **`frontend/vite.config.ts` 的 `build.target` 綁定隨附的 WebView2 版本**
  （釘選在 `build/webview2.json`）。改一邊就要改另一邊。
- **前端一律 `fetch('/api/...')` 相對路徑。** 唯一允許平台分支的檔案是
  `frontend/src/realtime.ts`。

## 尚未驗證的高風險項

`docs/ARCHITECTURE.md` §7 列的必驗項目一項都還沒做。最優先的是：
**OR-Tools native lib 對 `vcruntime140` 的依賴，必須在一台乾淨的 Windows 上實測**，
開發機驗不出來。這件事會影響是否需要在發佈包裡帶 VC++ runtime。
