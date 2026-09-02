# 醫院排班系統

醫師值班排班求解器。核心是 Google OR-Tools CP-SAT，交付形態是 Windows 上解壓即用的
portable 桌面程式（WPF 殼 + WebView2 渲染 + Vite SPA 前端）。

**動手前請先讀 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)**，特別是第 2 節「已排除方案」——
技術選型已定案，該文件記錄了每個決定的理由與已被否決的選項。

前端開發者請直接看 [`frontend/README.md`](frontend/README.md)，**不需要安裝 .NET 或 WebView2**。

---

## 現況

專案骨架已建立，**領域模型、求解建模、API 契約、UI 規格皆尚未開始**，屬下一階段規劃。

| 已完成 | 尚未開始 |
|---|---|
| 分層專案結構與相依關係 | 領域模型（`Scheduler.Domain`） |
| 架構規則的自動化驗證 | CP-SAT 建模（`Scheduler.Solver`） |
| 前端工作區（Vue 3 + TS + Vite + MSW） | 用例層（`Scheduler.Application`） |
| 發佈設定與打包腳本 | API 端點（`api-contract.yaml` 只有 `/api/health`） |
| WebView2 橋接規格（文件） | WebView2 宿主實作（`Scheduler.Shell`） |

## 專案結構

```
├─ HospitalScheduler.sln
├─ src/
│  ├─ Scheduler.Domain/        純領域模型，無任何外部相依
│  ├─ Scheduler.Application/   用例層，transport 無關
│  ├─ Scheduler.Solver/        CP-SAT 實作，全專案唯一可引用 OR-Tools 之處
│  ├─ Scheduler.Api/           開發期 transport：Minimal API（:5080）
│  └─ Scheduler.Shell/         正式版 transport：WPF + WebView2 殼
├─ frontend/                   前端工作區（Vue 3 + TypeScript + Vite）
├─ tests/
│  └─ Scheduler.ArchitectureTests/   分層規則的自動化驗證
├─ build/                      打包腳本、WebView2 fixed runtime
├─ docs/ARCHITECTURE.md        技術決策紀錄（先讀這份）
└─ api-contract.yaml           OpenAPI，前後端唯一耦合點
```

## 兩條硬性規則

1. **`Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools。**
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離，保留日後替換引擎的可能。
   違反時編譯仍會成功，由 `tests/Scheduler.ArchitectureTests` 擋下。

2. **`Scheduler.Api` 與 `Scheduler.Shell` 共用同一組 handler，不得各自實作業務邏輯。**
   兩者都只做 transport ↔ `Scheduler.Application` 的轉換。
   這是「無痛遷移到前後端分離」的技術基礎——遷移時刪掉 `Scheduler.Shell` 即可。

## 開發

需求：.NET SDK 8、Node.js ≥ 20.19。

```bash
# 後端（:5080）
dotnet run --project src/Scheduler.Api

# 前端（:5173，/api 自動 proxy 到 :5080）
cd frontend && npm install && npm run dev
```

不想跑後端時，前端可獨立以 MSW mock 開發：`cd frontend && npm run dev:mock`。

```bash
dotnet build          # 建置全部
dotnet test           # 跑架構規則驗證
```

## 分支模型

採 git-flow。**預設分支是 `develop`**，`main` 只接受 release / hotfix 合併。

```
main                只接受 release / hotfix，每個 commit 都是一次交付，tag 打在這裡
└─ develop          整合分支（GitHub 預設分支）
   ├─ feature/<slug>        從 develop 開，PR 合回 develop
   └─ release/<version>     從 develop 開，合回 main + develop
main
└─ hotfix/<version>         從 main 開，合回 main + develop
```

不要直接 commit 到 `main` 或 `develop`，一律走 PR。
`gh pr create` 會自動以 `develop` 為 base。

## 發佈

```powershell
pwsh build/fetch-webview2.ps1   # 一次性：準備 WebView2 Fixed Version runtime
pwsh build/publish.ps1          # 產出 publish/HospitalScheduler/
```

`fetch-webview2.ps1` 需要手動下載一次 `.cab`（Microsoft 未提供穩定的直接下載網址），
腳本會告訴你去哪裡抓、要抓哪個版本。版本釘選在 `build/webview2.json`。

**交付前必須跑過 [`docs/ARCHITECTURE.md` §8 的驗收清單](docs/ARCHITECTURE.md#8-架構驗收檢查清單)**，
其中最優先的是在一台**沒有 .NET、沒有 WebView2、沒有 VC++ Redist 的乾淨 Windows** 上實測。
OR-Tools 的 native library **確實**依賴 `msvcp140` / `vcruntime140`（見 `docs/ARCHITECTURE.md` §9.2），
`publish.ps1` 會把它們 app-local 放進發佈包並以 `check-native-deps.ps1` 靜態驗證，
但載入順序與版本相容仍只有乾淨機器驗得出來。
