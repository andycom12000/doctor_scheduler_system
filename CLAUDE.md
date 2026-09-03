# CLAUDE.md

醫師值班排班求解器。OR-Tools CP-SAT + WPF/WebView2 殼 + Vite SPA，交付形態是 Windows portable。

## 先讀

- `CONTEXT.md` — 詞彙表。**兩套點數（額度點數／公平性點數）與兩種假日（假日／國定假日）
  絕不可混用**，講「點數」或「假日」而不指明是哪一個，一律視為錯誤。
- `docs/ARCHITECTURE.md` — 技術決策已定案。**第 2 節是「已排除方案」，提任何技術選型前先看過**，
  Blazor / Tauri / Electron / MAUI / Timefold / meta-framework 等都已評估並否決，理由都在裡面。
- `api-contract.yaml` — 前後端唯一耦合點。42 個操作、52 個 schema。
- `docs/adr/` — 三個領域決策的理由。動到不可排班日、約束模型、變體產生方式之前先讀。
- `docs/constraint-defaults.md` — 7 硬 / 7 軟約束的唯一預設值。seed、mock、測試 fixture 都從它抄，不得另發明代碼或數字。

## 現況

**需求已釐清、契約已定案，領域層已落地，其餘層仍是骨架。**

- 領域已完整定義：5 區（A、B、C、ICU、總值）、33 位醫師、10 種身分、4 個身分組，
  7 條硬約束、7 條軟約束，收斂成**九個宣告式原語**（見 ADR-0002）
- `api-contract.yaml` 是完整的，前端可用 MSW mock 獨立開工
- `docs/design-revisions.md` 是要回寫到 Claude Design 的畫面修訂清單
- `Scheduler.Domain` 已實作：`Model/`（聚合根與行事曆）、`Constraints/`（九原語的宣告式定義，
  範圍是資料不是 `if`）、`Defaults/`（`docs/constraint-defaults.md` 的程式碼版）、
  `Scheduling/`（`SchedulingContext`、cellKey、兩套點數的 `MetricEvaluator`）、
  `Validation/`（`ViolationChecker` 與 `ScheduleScores`）。零 PackageReference。
  cellKey 慣例：逐格規則用 `area:{areaId}:{date}`，個人序列／累計規則用 `staff:{staffId}:{date}`，
  額度與連值只標超出上限的那幾格
- `Application` / `Solver` 仍只有 `AssemblyMarker`，`Scheduler.Shell` 只有空 WPF 視窗，
  `Scheduler.Api` 只實作了 `/api/health`。
  `Scheduler.Persistence` 只有 `Microsoft.Data.Sqlite` 的相依與一個載入自檢（`SqliteRuntimeProbe`）

## 兩條硬性規則（違反時編譯會過，但架構測試會擋）

1. `Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools。
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離。
   **後果：驗證與求解是兩份實作，必須讀同一份宣告式約束定義才不會漂移——見 ADR-0002。**
2. `Scheduler.Api` 與 `Scheduler.Shell` 是兩個 transport，共用同一組 handler。
   兩者都不得實作業務邏輯，只做 transport ↔ Application 的轉換。
   遷移到前後端分離時，刪掉 `Scheduler.Shell` 即可。

`tests/Scheduler.ArchitectureTests` 驗證規則 1。規則 2 目前沒有自動化驗證，靠 review。
`tests/Scheduler.Domain.Tests` 驗證九原語與違規檢查器的行為，fixture 全用出廠值憑空造。

## 指令

```bash
dotnet build                              # 建置全部
dotnet test                               # 架構規則 + 領域規則測試
dotnet run --project src/Scheduler.Api    # 開發期後端 :5080

cd frontend
npm run dev          # :5173，/api proxy 到 :5080
npm run dev:mock     # :5173，/api 由 MSW 攔截，不需後端
npm run build        # 型別檢查 + 建置到 ../src/Scheduler.Shell/wwwroot
npm run api:types    # 由 api-contract.yaml 生成 src/api/schema.d.ts
```

發佈：`pwsh build/fetch-webview2.ps1`（一次性）→ `pwsh build/publish.ps1`
（內含 VC++ runtime app-local 複製與 `build/check-native-deps.ps1` 的 import table 檢查；
開發機沒有 pwsh 7 時用 `powershell -File` 跑也可以，腳本是 5.1 相容的）。

## 分支模型（git-flow）

**預設分支是 `develop`，不是 `main`。** 日常工作一律從 `develop` 開分支、合回 `develop`。

| 分支 | 從哪開 | 合回 | 用途 |
|---|---|---|---|
| `main` | — | — | 只接受 release / hotfix 合併。每個 commit 都是一次實際交付 |
| `develop` | — | — | 整合分支，GitHub 預設分支 |
| `feature/<slug>` | `develop` | `develop` | 功能開發 |
| `release/<version>` | `develop` | `main` + `develop` | 發佈準備，合進 main 後在 main 上打 tag |
| `hotfix/<version>` | `main` | `main` + `develop` | 已交付版本的緊急修正 |

**不要直接 commit 到 `main` 或 `develop`**，一律走 PR。

### 每個 clone 都要做一次的設定

git-flow 的設定存在 `.git/config`，**不會隨 repo 進版控**。新 clone 後跑一次：

```bash
git config gitflow.branch.master  main      # ← 不是 master，跑 git flow init -d 會猜錯
git config gitflow.branch.develop develop
git config gitflow.prefix.feature    "feature/"
git config gitflow.prefix.bugfix     "bugfix/"
git config gitflow.prefix.release    "release/"
git config gitflow.prefix.hotfix     "hotfix/"
git config gitflow.prefix.support    "support/"
git config gitflow.prefix.versiontag "v"
git flow init -d
```

`git flow config` 可確認結果。**先設 config 再 init**——直接跑 `git flow init -d`
會預設用 `master` 當 production branch，然後真的幫你開一個 `master` 分支出來。

### 日常指令

CLI（AVH edition）已裝在 `~/.local/bin`，也可以完全不用它、用純 git：

```bash
# 用 CLI
git flow feature start <slug>          # 從 develop 開 feature/<slug>
git flow feature publish <slug>        # push 並設 upstream

# 或純 git（等價）
git switch develop && git pull
git switch -c feature/<slug>
git push -u origin feature/<slug>
```

**不要用 `git flow feature finish`** —— 它會在本機直接合進 develop 並刪掉分支，
繞過 PR。開 PR 才是合併路徑：

```bash
gh pr create        # base 自動是 develop（預設分支已設定）
```

合併後再收尾：`git switch develop && git pull && git branch -d feature/<slug>`。

本專案的版本號對應 portable 發佈包的資料夾版本，tag 只打在 `main` 上。

## 專案特有的坑

- **`Scheduler.Shell` 裡 `Application` 會撞名。** 本組件同時引用 `Scheduler.Application`
  命名空間與 `System.Windows.Application` 型別，基底型別必須完整限定。
- **不要開 trim / AOT。** OR-Tools 的 P/Invoke wrapper 兩者皆不相容，
  `Directory.Build.props` 已明確關閉。SQLite 的 `e_sqlite3` 同樣是 native，一併受影響。
- **OR-Tools 的 native DLL 依賴 `msvcp140` / `vcruntime140` / `vcruntime140_1`，NuGet 不附帶。**
  `build/publish.ps1` 會從 VS Build Tools 的 Redist 目錄 app-local 複製進發佈包，
  `build/check-native-deps.ps1` 掃 import table 守住它。不要「為了省檔案」把這三個 DLL 拿掉，
  也不要改成要求使用者安裝 vc_redist。見 `docs/ARCHITECTURE.md` §9.2。
- **`RuntimeIdentifier` / `SelfContained` 要寫在 `Scheduler.Shell.csproj` 裡，不能移回
  `Directory.Build.props` 用條件式設定**——props 在專案本體前匯入，條件看不到專案屬性，
  publish 會默默變成 framework-dependent。
- **不要加認證、角色、多單位、`If-Match` 樂觀鎖、伺服器端分頁。**
  單機、單一使用者、單一 process、單一科部——這些全部沒有防禦對象，已在需求釐清中明確排除。
  `revision` 只是修改次數計數器，不是併發控制。
- **不要改成 `PublishSingleFile`。** 自解壓到 `%TEMP%` 會觸發 AppLocker / EDR。
- **所有執行期狀態寫在程式旁的 `data/`**，不得碰 `%APPDATA%` / `%LOCALAPPDATA%` / 登錄檔。
  這是 portable 的硬性要求，也是驗收項目。
- **`frontend/vite.config.ts` 的 `build.target` 綁定隨附的 WebView2 版本**
  （釘選在 `build/webview2.json`）。改一邊就要改另一邊。
- **前端一律 `fetch('/api/...')` 相對路徑。** 唯一允許平台分支的檔案是
  `frontend/src/realtime.ts`。

## Agent skills

### Issue tracker

GitHub Issues（`andycom12000/doctor_scheduler_system`，private），透過 `gh` CLI 操作。
See `docs/agents/issue-tracker.md`.

### Triage labels

五個預設標籤，字串與角色名稱相同，已建在 GitHub 上。See `docs/agents/triage-labels.md`.

### Domain docs

Single-context —— root 一份 `CONTEXT.md` + `docs/adr/`。前後端是同一領域的兩個層，
不是兩個 bounded context。See `docs/agents/domain.md`.

## 尚未驗證的高風險項

`docs/ARCHITECTURE.md` §9 的 VC++ runtime 與 SQLite native 兩項已用靜態分析確認並處理
（§9.2）。**還欠一次乾淨 Windows 上的實際啟動**——靜態分析證明不了載入順序與版本相容。

另一個容易腐爛的地方：**NP 的四條專屬規則幾乎測不到**——NP 只有 1 人且是後備人力，
真實資料下極少觸發。`tests/Scheduler.Domain.Tests/NpRulesTests.cs` 用憑空造的情境
守著這四條（含跨月 tail 與豁免名單），動到 NP 規則或 `ConstraintScope` 時先跑它。
求解器端（`Scheduler.Solver`）對應的測試尚未存在。
