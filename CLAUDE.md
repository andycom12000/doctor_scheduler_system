# CLAUDE.md

醫師值班排班求解器。OR-Tools CP-SAT + WPF/WebView2 殼 + Vite SPA，交付形態是 Windows portable。

## 先讀

- `CONTEXT.md` — 詞彙表。**兩套點數（額度點數／公平性點數）與兩種假日（假日／國定假日）
  絕不可混用**，講「點數」或「假日」而不指明是哪一個，一律視為錯誤。
- `docs/ARCHITECTURE.md` — 技術決策已定案。**第 2 節是「已排除方案」，提任何技術選型前先看過**，
  Blazor / Tauri / Electron / MAUI / Timefold / meta-framework 等都已評估並否決，理由都在裡面。
- `api-contract.yaml` — 前後端唯一耦合點。42 個操作、52 個 schema。
- `docs/adr/` — 四個領域決策的理由。動到不可排班日、約束模型、變體產生方式、月結轉快照之前先讀。
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
- `Scheduler.Persistence` 已落地（EF Core + SQLite）：`Entities/`（自己的一套 entity，不拿 Domain
  record 當 entity）、`Repositories/`（六個 repository 實作 + `EfUnitOfWork`）、`Mapping/`
  （列舉字串與契約一致；時間戳一律 UTC ISO-8601 字串，SQLite 才能排序）、`Seed/`（出廠值與 2026
  行事曆例外日）、`Migrations/`（進版控，啟動時自動套用）。`SchedulerDatabase.InitializeAsync`
  是啟動流程，`SchedulerDatabase.DefaultPath` 是程式旁的 `data/scheduler.db`。
  改了 `SchedulerDbContext` 要跑 `dotnet ef migrations add <Name> --project src/Scheduler.Persistence`，
  否則 `tests/Scheduler.Persistence.Tests` 的 snapshot 比對會失敗
- `Scheduler.Application` 的**讀取路徑**已落地：`Persistence/`（repository 介面）、
  `Scheduling/SchedulingContextLoader`（組某月 context 的唯一地方：上月尾巴撈
  `max(minGap, maxConsecutive)` 天、行事曆跨到次月與連值週六視窗、月結轉依 ADR-0004 分支、
  人員撈全部含停用；回 `LoadedContext` 連同標頭、約束設定、身分組、提醒）、
  `Schedules/ScheduleQueries`（值班表、validate、違規、點數看板、單日、空缺、候選人）、
  `BlockedDays/BlockedDayQueries`（登記表、可行性預警；層次由資格矩陣推出，不寫死區域類型）、
  `People/StaffQueries`、`Calendars/CalendarQueries`、`Errors/SchedulerException`（契約錯誤碼）。
  候選人的阻擋理由不重寫規則：把他放進那格跑一次 `ViolationChecker`，多出來的硬違規就是理由。
  設定類 GET 是 repository 直接回傳，沒有查詢類別。
- `Scheduler.Application` 的**寫入路徑**已落地，查詢類旁邊各一個命令類：
  `Schedules/ScheduleCommands`（setDuty 自動建草稿、swap、publish）、`BlockedDays/BlockedDayCommands`、
  `Settings/SettingsCommands`（五份文件整份取代 + 逐月覆寫）、`Calendars/CalendarCommands`、
  `People/StaffCommands`。寫入走 `IUnitOfWork.CommitAsync`：repository 只登記變更、不落盤，
  一個用例一次 commit。setDuty 只擋結構不變式（同人同日另一區 → `STAFF_ALREADY_ON_DUTY`），
  硬約束違規照收、回全量違規；publish 用 Domain 的 `CarryOverSettlement` 結算月結轉，
  第一次發布才寫 `carry_over_applied`（ADR-0004）。設定 PUT 在 `SettingsCommands` 守住讀取路徑
  對設定形狀的假設（主鍵重複、指到不存在的類型／組、原語缺參數、公平性查表缺列、範圍空陣列
  正規化成 null），否則一次 PUT 會讓之後每個 GET 都 500。發布時間戳從 DI 的 `TimeProvider` 拿
- `Scheduler.Api` 的**讀取路徑**已落地：`ApiHost.BuildAsync(ApiHostOptions)` 是唯一的 pipeline 組裝
  （含資料庫啟動流程與 Application 的 DI 註冊；`UseTestServer` 給 Shell 與測試用，`Connection` 給
  in-memory 測試用），`Program.cs` 只剩兩行。`Contracts/`（手寫 DTO、列舉的契約字串、Domain → DTO
  對應）、`Endpoints/ReadEndpoints`（19 個讀取端點與 validate）、`Http/`（`SchedulerException` →
  `ErrorResponse` 的 middleware，ErrorCode → 狀態碼只寫在這；路徑與查詢參數一律以字串接、自己解析，
  格式錯誤統一 422 `INVALID_REQUEST`）。契約標 `[X, null]` 的必要欄位要輸出 null，可省略的欄位用
  `WhenWritingNull` 逐一標，不設全域 ignore。
  **寫入端點**也已接上：`Endpoints/WriteEndpoints`（22 個）、`Contracts/RequestMapper`（DTO → Domain，
  缺欄位與不認得的列舉字串是 422）、`Http/RequestBody`（本體自己讀，壞 JSON／空本體統一 422；
  行事曆覆寫從 `JsonObject` 讀，才分得出 `holidayName` 沒送與送 null）。請求 DTO 的欄位全是
  nullable，缺欄位由 mapper 判定，不讓反序列化默默塞預設值。匯出端點尚未接
- **求解**已落地（ARCHITECTURE §4.8）。`Scheduler.Application/Solving/`：`ISolver`（一次解一份；權重另放
  `EffectiveWeights`，因為 `ConstraintDefinition.Weight` 上限 100 裝不下乘過 1.5 的數）、`VariantProfiles`
  （三個具名立場的乘數表、多樣性 15 格）、`SolverJobService`（singleton，單一 slot、狀態機、序列三份、
  每完成一份就落盤、中止保留已完成的變體、進度用 `System.Threading.Channels` 廣播給訂閱者；背景落盤透過
  `ISolverScopeFactory` 開 scope，Application 零套件相依所以不直接拿 DI）。變體指標全部由 Domain 重算，
  Solver 的目標值只給進度看。`Scheduler.Solver/ScheduleModel` 是 CP-SAT 建模器：每個原語一個方法，讀同一份
  `ConstraintSettings` 與 `ConstraintScope`；覆蓋建成 1e9 罰分的軟項（§4.5）；同人同日一格、同格最多
  requiredPerDay 人是結構不變式不是約束；連值週六加分是唯一 reified 項。`CpSatSolver` 丟 `Task.Run`、
  中止用 `StopSearch`。Api 的 `Endpoints/SolverEndpoints`：六個端點含 SSE（`data: {SolverProgress}`，
  payload 多帶 `jobId` 供前端過濾）與 apply-variant；`ISolver → CpSatSolver` 在 `ApiHost` 註冊，
  `ApiHostOptions.ConfigureServices` 可換假的。`/api/health` 現在會探 SQLite native
- `Scheduler.Shell` 只有空 WPF 視窗

## 兩條硬性規則（違反時編譯會過，但架構測試會擋）

1. `Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools。
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離。
   **後果：驗證與求解是兩份實作，必須讀同一份宣告式約束定義才不會漂移——見 ADR-0002。**
2. `Scheduler.Api` 是唯一一份 HTTP 實作，`Scheduler.Shell` 只是它的 host
   （用 `Microsoft.AspNetCore.TestHost` 在 process 內跑同一個 `WebApplication`，不開 socket）。
   路由、binding、錯誤碼對應只寫在 Api；Api 的 endpoint 不含業務邏輯，只做「解參數 → 呼叫
   Application → 包回應」。Shell 不得引用 Application／Domain／Persistence／Solver，只引用 Api。
   遷移到前後端分離時，刪掉 `Scheduler.Shell` 即可。見 `docs/ARCHITECTURE.md` §3.2。

層內附帶規則：只有 `Scheduler.Persistence` 引用 EF Core；repository 介面在 Application、實作在
Persistence；Application 不引用 Persistence。

`tests/Scheduler.ArchitectureTests` 已驗證規則 1 與層內附帶規則（EF Core 只在 Persistence、
Application 不引用 Persistence）。規則 2（Shell 只引用 Api）在 Shell 動工的同一個 PR 補。

**2026-09-06 拍板、尚未實作的後端設計**全部在 `docs/ARCHITECTURE.md` §3.2、§4.8、§5、§7
與 ADR-0004，動工前先讀。
`tests/Scheduler.Domain.Tests` 驗證九原語與違規檢查器的行為，fixture 全用出廠值憑空造。
`tests/Scheduler.Application.Tests` 用記憶體內的假 repository（`InMemoryStore`，也是假的 `IUnitOfWork`
與 `ISolverScopeFactory`）驗證 loader 分支、讀取查詢、寫入命令與求解狀態機（`SolverJobServiceTests` 用假的
`ISolver`：乘數、多樣性輸入、busy、中止保留變體、指標由 Domain 算）。
`tests/Scheduler.Solver.Tests` 是漂移守門（§4.8）：真的跑 CP-SAT，任一輸出丟給 `ViolationChecker`，硬違規只准是
覆蓋。情境含參考名單 33 人、NP 四條、跨月尾巴、登記爆量、關 H2、多樣性、中止、S7 的 reification。fixture 借
Domain.Tests 的 `ContextBuilder`。每個 Solver PR 必過，CI 有跑。
`tests/Scheduler.Api.Tests` 是契約守法測試：`ApiFixture` 用 TestServer + SQLite in-memory 把 Api 跑起來，
`ContractSchema` 把 `api-contract.yaml` 轉成 JSON Schema（`components/schemas` 搬進 `$defs`、
`$ref` 改寫、`format: date` 檢查開著），每個端點的回應都驗形狀與狀態碼。新端點要跟著加一個測試。寫入端點的測試在 `WriteEndpointTests`，
自己一個 class 就有自己的一顆資料庫，每個測試用自己的月份或自己建的人員，不互相踩。
`frontend/scripts/smoke-mock.ts` 加了 `--target`：`npm run mock:smoke` 打 MSW、`npm run api:smoke`
打 :5080 的真後端，同一份斷言兩邊各跑一次找漂移；依賴 mock 種子、尚未落地端點、或會在真後端
留下資料的段落標 mock-only。對真後端的寫入只做「往返一圈不留痕」的段落（人員新增到刪除、
不可排班日登記再清除）。

## 指令

```bash
dotnet build                              # 建置全部
dotnet test                               # 架構規則 + 領域規則 + 存取層 + 應用層 + 契約守法 + 求解器漂移守門
dotnet run --project src/Scheduler.Api    # 開發期後端 :5080

cd frontend
npm run dev          # :5173，/api proxy 到 :5080
npm run dev:mock     # :5173，/api 由 MSW 攔截，不需後端
npm run build        # 型別檢查 + 建置到 ../src/Scheduler.Shell/wwwroot
npm run api:types    # 由 api-contract.yaml 生成 src/api/schema.d.ts
npm run mock:smoke   # 同一份煙霧斷言打 MSW mock
npm run api:smoke    # 同一份煙霧斷言打 :5080 真後端（先 dotnet run）
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

- **`Scheduler.Shell` 裡 `Application` 會撞名。** `System.Windows.Application` 與
  `Scheduler.Application` 命名空間同名，基底型別要完整限定。規則 2 改成 Shell 只引用 Api 之後
  這個坑應該消失，但 `Scheduler.Api` 若對外暴露 Application 型別仍會撞到。
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
求解器端的對應在 `tests/Scheduler.Solver.Tests/NpRulesSolverTests.cs`（把 NP 逼成唯一人選）。
