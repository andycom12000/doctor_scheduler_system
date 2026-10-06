# 醫院排班系統

醫師值班排班求解器。核心是 Google OR-Tools CP-SAT，交付形態是 Windows 上解壓即用的
portable 桌面程式（WPF 殼 + WebView2 渲染 + Vite SPA 前端）。

## 先讀哪裡

| 文件 | 內容 |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | **現況、兩條硬性規則、指令、分支模型、專案特有的坑。** 各層做到哪裡以這份為準，本檔不重複 |
| [`CONTEXT.md`](CONTEXT.md) | 詞彙表。額度點數與公平性點數、假日與國定假日是不同的東西，不可混用 |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 技術決策（已定案）。第 2 節是已排除方案，提任何技術選型前先看 |
| [`api-contract.yaml`](api-contract.yaml) | OpenAPI，前後端唯一耦合點 |
| [`docs/adr/`](docs/adr/) | 領域決策的理由 |
| [`docs/constraint-defaults.md`](docs/constraint-defaults.md) | 約束、額度點數與公平性點數規則的唯一預設值 |
| [`docs/release-process.md`](docs/release-process.md) | 發佈流程 |
| [`frontend/README.md`](frontend/README.md) | 前端開發，**不需要安裝 .NET 或 WebView2** |
| [`docs/user-guide/index.html`](docs/user-guide/index.html) | 給排班者看的使用者說明，發佈時隨包附上 |

## 現況

後端各層與前端畫面已落地，契約 44 個操作全數實作，求解、匯出、發佈包腳本都已可用。
還在進行的工作與各層細節見 [`CLAUDE.md`](CLAUDE.md) 的「現況」與
[GitHub Issues](https://github.com/andycom12000/doctor_scheduler_system/issues)。

## 專案結構

```
├─ HospitalScheduler.sln
├─ Directory.Build.props         # 版本號（<Version>）的唯一來源
├─ src/
│  ├─ Scheduler.Domain/          領域模型與約束定義、違規檢查，零套件相依
│  ├─ Scheduler.Application/     用例層（查詢、命令、求解工作狀態機），repository 與 ISolver 介面
│  ├─ Scheduler.Persistence/     EF Core + SQLite，repository 實作、migration、出廠 seed
│  ├─ Scheduler.Solver/          CP-SAT 建模與求解，全專案唯一可引用 OR-Tools 之處
│  ├─ Scheduler.Api/             唯一一份 HTTP 實作（Minimal API），開發期監聽 :5080
│  └─ Scheduler.Shell/           WPF + WebView2 殼，在 process 內 host 同一個 Api，不開 socket
├─ tests/
│  ├─ Scheduler.ArchitectureTests/   分層規則
│  ├─ Scheduler.Domain.Tests/        九個約束原語與違規檢查
│  ├─ Scheduler.Persistence.Tests/   存取層與 migration snapshot
│  ├─ Scheduler.Application.Tests/   讀取、寫入、求解狀態機
│  ├─ Scheduler.Api.Tests/           以 api-contract.yaml 驗每個端點的回應
│  ├─ Scheduler.Solver.Tests/        求解器與檢查器的漂移守門
│  └─ Scheduler.Shell.Tests/         殼的純轉換邏輯
├─ frontend/                     前端（Vue 3 + TypeScript + Vite + MSW mock）
├─ build/                        發佈腳本、WebView2 Fixed Version 設定
├─ docs/                         架構、ADR、約束預設值、發佈流程、使用者說明
└─ api-contract.yaml
```

## 兩條硬性規則

1. **`Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools。**
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離。
2. **`Scheduler.Api` 是唯一一份 HTTP 實作，`Scheduler.Shell` 只是它的 host。**
   遷移到前後端分離時，刪掉 `Scheduler.Shell` 即可。

違反時編譯仍會成功，由 `tests/Scheduler.ArchitectureTests` 擋下。細節見 [`CLAUDE.md`](CLAUDE.md)。

## 開發

需求：.NET SDK 8、Node.js 20.19 以上。

```bash
dotnet build                              # 建置全部
dotnet test                               # 全部測試
dotnet run --project src/Scheduler.Api    # 後端 :5080

cd frontend
npm install
npm run dev                               # :5173，/api proxy 到 :5080
npm run dev:mock                          # :5173，/api 由 MSW 攔截，不需要後端
```

其餘指令（型別生成、煙霧測試等）見 [`CLAUDE.md`](CLAUDE.md) 的「指令」。

## 分支模型

採 git-flow。**預設分支是 `develop`**，`main` 只接受 release / hotfix 合併。
不要直接 commit 到 `main` 或 `develop`，一律走 PR；`gh pr create` 會自動以 `develop` 為 base。
分支種類、來源與合併目標見 [`CLAUDE.md`](CLAUDE.md) 的「分支模型」。

## 發佈

```powershell
pwsh build/fetch-webview2.ps1                          # 一次性：準備 WebView2 Fixed Version runtime
pwsh build/publish.ps1 -Zip -RosterFile <名冊.csv>     # 產出 publish/DoctorScheduler-v<版本>/ 與 zip
```

發佈包的 `使用者說明/` 資料夾由 `build/publish.ps1` 從 [`docs/user-guide/`](docs/user-guide/) 複製進去。
完整步驟（版本號、名冊檔、驗收、tag）見 [`docs/release-process.md`](docs/release-process.md)，
交付前必跑的驗收清單見 [`docs/ARCHITECTURE.md` §10](docs/ARCHITECTURE.md#10-架構驗收檢查清單)。
