# 醫師值班排班系統 — 技術架構

> 接手者請**先讀完第 2 節「已排除方案」**，避免重新提出已被否決的選項。
>
> 領域詞彙見 `CONTEXT.md`。端點契約見 `api-contract.yaml`。
> 領域決策的理由見 `docs/adr/`。約束的預設值見 `docs/constraint-defaults.md`。

---

## 0. 這份文件的來歷

第一版是專案骨架階段的 scaffold，內容是「還沒有領域模型時，技術選型長什麼樣」。
本版是在案主的實際約束（`constraints.md`）與設計稿到齊、經過完整的需求釐清之後改寫的，
**領域相關的段落全部是新的**，技術選型段落則多數沿用——那些理由沒有因為領域釐清而改變。

一個重要的權威界定：**設計稿對「版面、互動、資訊架構」有權威，對「領域內容」沒有。**
設計稿上的 13 區 51 人、N1–N4 職級、主責／支援、必休／希望休兩層，
都是雛型階段的假資料，與實際領域無關。

---

## 1. 技術決策（已定案）

| 項目 | 決定 |
|---|---|
| Runtime | .NET 8/9，self-contained |
| 殼 | **WPF** |
| UI 渲染 | **WebView2**，Fixed Version 隨附 |
| 前端 | **Vue 3 + TypeScript + Vite** |
| 求解器 | **Google.OrTools CP-SAT**，in-process |
| 前後端橋接 | **`WebResourceRequested` 攔截**，不開 port |
| 資料儲存 | **SQLite**，經 **EF Core** 存取（2026-09-06 拍板，見 §5），單一檔案置於程式旁的 `data/` |
| 發佈形態 | **資料夾式 portable**（解壓即用） |

### 1.1 決策理由

**為何 .NET**：OR-Tools 官方 binding 僅 C++ / Python / Java / .NET。選 .NET 才能在同一
process 內取得全速多執行緒 CP-SAT，無 sidecar、無 port。求解器的語言限制反過來決定了整個
stack，不是 UI 偏好決定的。

**為何 WPF 而非 WinForms**：殼裡幾乎全是 WebView，只需約 50 行 WPF。選 WPF 的唯一理由是
Per-Monitor V2 DPI 行為較佳——目標環境螢幕組合混亂（4K 筆電接 1080p 外接螢幕常見）。

**為何 WebView2 而非原生 XAML UI**：核心 UI 是密集格線，需色彩編碼、拖拉、列印。這在
CSS Grid 成本極低，在 WPF DataGrid 是一場硬仗。同時前端要交給專職 JS 開發者，需標準前端生態。

**為何 Fixed Version WebView2**：portable 的必要條件。Evergreen 模式要求目標機器已安裝
runtime，不符合免安裝前提。代價約 180MB 與自行負責更新。

**為何 `WebResourceRequested` 而非 `AddHostObjectToScript`**：host objects 是 COM proxy
語意，前端得寫 Windows 專屬程式碼，遷移時整份重寫。攔截法讓前端寫標準 `fetch('/api/...')`，
**與未來雲端版逐字相同**。

**為何 SQLite 而非 JSON 檔**：資料量小得無所謂（一個月約 150 筆值班，十年不到兩萬筆），
選 SQLite 的理由不是效能，是**交易安全與部分寫入**：

- 「改一格值班」是本系統最高頻的操作。JSON 方案下每次點格都要重寫整個月的檔案，
  寫到一半斷電就整月毀了。SQLite 有 WAL 與交易。
- `revision` 遞增、月結轉結算、名冊統計這些在 SQL 下幾乎免費。
- 單一檔案，「複製整個資料夾即搬移」的 portable 要求完全不受影響。

代價是多一個 native 相依。本專案已因 OR-Tools 綁死 native lib，`Microsoft.Data.Sqlite`
的 native 部署單純得多，不會讓事情更糟。

**放棄 JSON 的代價要知道**：出事時無法用記事本直接改資料救回來。
在「客戶端沒有工程師」的交付情境下這是真的損失，接受它是因為交易安全更重要——
會需要「用記事本救資料」的場景，多半正是寫入中斷造成的，而那正是 SQLite 要消除的。

**為何資料夾式而非單一 exe**：`PublishSingleFile` 實為自解壓到 `%TEMP%` 再執行，
是 AppLocker / EDR 的頭號攔截特徵，且冷啟動變慢。「免安裝」要的是 xcopy 部署，不是單一檔案。

---

## 2. 已排除方案（勿重新提案）

| 方案 | 排除理由 |
|---|---|
| Blazor（WASM / Server / Hybrid） | 前端需交給 JS 開發者，要標準 JS 框架生態 |
| Tauri / Electron | Rust 與 Node **均無 OR-Tools 官方 binding**，必須 sidecar |
| MAUI / WinUI 3 | 以 MSIX 封裝為主，是「安裝」不是「免安裝」 |
| Photino.NET | Windows 底層仍是 WebView2，依賴一分沒省；`RegisterCustomSchemeHandler` 為同步簽章，長時間求解難處理 |
| Avalonia / 原生 XAML UI | 格線 UI 自行實作成本過高，且無法讓 JS 開發者獨立作業 |
| 純瀏覽器 WASM 求解 | 靜態託管無法設 COOP/COEP → 無 `SharedArrayBuffer` → 單執行緒 |
| `PublishSingleFile` 單一 exe | 自解壓觸發資安攔截 |
| Timefold Solver | JVM 生態，portable 情境下體積、process、技術棧全部翻倍 |
| 自寫 metaheuristic | 放棄最佳性保證與「證明無解」的能力 |
| Next.js / Nuxt / SvelteKit | SSR / server actions 在此情境全用不到 |
| PostgreSQL / LiteDB | 前者需要安裝，違反 portable；後者相對 SQLite 沒帶來對應價值 |
| ~~EF Core~~ | **2026-09-06 推翻，改為採用。** 原理由「相對 raw SQLite 沒帶來對應價值」沒有算進「推廣到下一個客戶時可能要伺服器版、換資料庫引擎」。規矩見 §5 |
| 認證、角色、多單位、`If-Match` 樂觀鎖 | **單機、單一使用者、單一 process、單一科部**，全部沒有防禦對象 |
| 伺服器端分頁 | 全院 34 人、每月 150 筆值班，分頁是憑空發明的複雜度 |

---

## 3. 領域模型與分層

### 3.1 專案結構

```
HospitalScheduler/
├─ HospitalScheduler.sln
├─ CONTEXT.md                    # 詞彙表
├─ api-contract.yaml             # OpenAPI，前後端唯一耦合點
├─ src/
│  ├─ Scheduler.Domain/          # 純領域模型 + 約束檢查器，無外部相依
│  ├─ Scheduler.Solver/          # CP-SAT 建模器（OR-Tools 相依隔離於此）
│  ├─ Scheduler.Application/     # 用例層，transport 無關；repository 介面、SchedulingContextLoader、SolverJobService 在此
│  ├─ Scheduler.Persistence/     # EF Core + SQLite 讀寫，repository 實作
│  ├─ Scheduler.Api/             # 唯一一份 HTTP 實作（Minimal API）：開發期直接跑，正式版由 Shell 在 process 內 host
│  └─ Scheduler.Shell/           # 正式版：WPF + WebView2 殼，只 host Scheduler.Api
├─ frontend/                     # Vue 3 SPA
├─ tests/
└─ build/
```

### 3.2 兩條硬性規則

1. **`Scheduler.Domain` 與 `Scheduler.Application` 不得引用任何 OR-Tools 型別。**
   求解器相依只存在於 `Scheduler.Solver`，由介面隔離。
   由 `tests/Scheduler.ArchitectureTests` 自動驗證。理由見 ADR-0002。

2. **`Scheduler.Api` 是唯一一份 HTTP 實作，`Scheduler.Shell` 只是它的 host。**
   路由比對、model binding、JSON 序列化、`ErrorCode` → HTTP status 的對應只寫在
   `Scheduler.Api` 一份。正式版的 Shell 用 `Microsoft.AspNetCore.TestHost` 把同一個
   `WebApplication` 在 process 內跑起來（不開 socket），WebView2 攔到的請求直接轉交。
   兩者都不得含業務邏輯：Api 的每個 endpoint 只做「解參數 → 呼叫 Application → 包回應」。
   這是「無痛遷移到前後端分離」的技術基礎——遷移時刪掉 `Scheduler.Shell` 即可，
   而且開發期測過的 pipeline 就是正式版跑的那一個。
   **架構測試要守：Shell 只能引用 Api（與 WebView2／WPF），不得引用 Application、Domain、
   Persistence、Solver。** 唯一例外是求解進度：WebResourceRequested 的回應不會漸進送出，
   Shell 要從 Api 的 DI 容器取得進度事件來源再 `PostWebMessageAsJson`，這是 transport 工作。
   （2026-09-06 拍板。原先 §6.2 的草圖是 Application 自帶 HTTP 形狀的 dispatcher，已改。）

   附帶的層內規則，同樣由架構測試守：
   - 只有 `Scheduler.Persistence` 可以引用 EF Core。Application 看不到 `DbContext`。
   - Repository **介面在 Application、實作在 Persistence**。Application 不引用 Persistence。

### 3.3 約束以九個原語表達

規則 1 帶來一個必然後果：**驗證與求解是兩份實作**。
`POST /schedules/{ym}/validate` 與 `GET /schedules/{ym}/violations` 回答的是
「給定一份既有值班表，哪些格子違規」——這不能走求解器，否則 OR-Tools 相依會被拉進
Application 層。

避免兩份實作漂移的方法是**讓它們讀同一份宣告式定義**（ADR-0002）。
案主的 15 條規則收斂成九個原語：

| 原語 | 對應規則 | 硬／軟 |
|---|---|---|
| `ExactCount` | 每日每區恰好 1 人 | 硬（求解器內建成極高權重軟項，見 §4.5） |
| `Eligible` | 身分 × 區域類型 資格矩陣 | 硬 |
| `Budget` | 額度點數上限；NP 每月 20 天 | 硬 |
| `MinGap` | 值休休值（間隔 ≥ 3 天），NP 豁免。**跨月** | 硬 |
| `MaxConsecutive` | NP 最多連六。**跨月** | 硬（原文標 soft，案主釐清為硬） |
| `Forbidden` | 不可排班日 | 硬 |
| `Preference` | R2/R3 優先 ICU；R4~6 優先總值；NP 避開假日；NP 盡量不用 | 軟 |
| `Fairness` | 額度點數組內公平（起始偏移＝月結轉）；公平性點數組內公平 | 軟 |
| `Consistency` | 盡量讓同一人值同一區 | 軟 |

7 硬 / 7 軟，完整的預設值（代碼、scope、參數、權重）在 **`docs/constraint-defaults.md`**——
那才是「唯一的一份定義」，seed、mock、測試 fixture 都從它抄。

關鍵抽象是**度量（Metric）是一等公民**，有三種：額度點數、公平性點數、值班天數。
`Budget` 與 `Fairness` 都對度量參數化，因此：

- 「額度點數上限」與「NP 每月天數上限」共用同一個原語
- 兩套點數的公平性共用同一個原語——**兩套點數不需要兩套程式碼，只需要兩個度量定義**

兩個軟約束的分數要有明確定義，否則驗證層與求解層會各算各的：

- `Fairness`：組內 **max − min**。線性、CP-SAT 直接可建；變異數建不了。
  對額度點數比的是**剩餘額度**（上限 − 已排 − 月結轉偏移），不是原始點數
- `Consistency`：每人「值班數 − 最常值的那一區的值班數」，即離開主區的次數

所有原語的適用範圍有三個維度：**身分、區域類型、日類**（平日／假日／國定假日）。
NP 的四條特例（豁免值休休、天數上限、最多連六、盡量不用）全部透過 `scope` 表達，
**不在程式裡寫 if**。日類維度是「NP 避開假日」唯一能被表達的方式——
沒有它，這條規則用九原語寫不出來。「避開」用 `params.direction: avoid`，
權重永遠非負，0–100 對每條軟約束都是同一個意思。

宣告式定義的形狀見 `api-contract.yaml` 的 `ConstraintSettings`，
它同時是 `GET · PUT /api/settings/constraints` 的回應——設定端點讀寫的就是定義本身。
但要注意那份文件持有的是**規則的身分與參數**：`Eligible` 的矩陣、`Forbidden` 的登記、
`ExactCount` 的人數、`Budget` 的上限都住在各自的聚合根，由原語去讀，不複製進 `params`。

### 3.4 兩件不是求解約束的事

- **「每人最多登記 16 天不可排班日」是登記期的驗證規則**，不是求解約束。
  求解器只看到已登記的日子（`Forbidden`），完全不需要知道 16 這個數字。
- **「跨月補償」不是獨立約束**，是 `Fairness` 的起始偏移參數。

### 3.5 兩個假日概念

「假日」在本領域有兩個不能混用的意思，`CONTEXT.md` 有正式定義：

- **假日**（週六、週日、國定假日）→ 決定額度點數 1 或 2，決定公平性點數查表的列
- **國定假日**（不含一般週末）→ 只用於「連值兩個週六」的加分判斷

若「連值兩個週六」規則裡的假日含週末，條件永遠為假、規則不會觸發——
這是釐清過程中發現的原始文件矛盾。**補班日視為平日。**

---

## 4. 求解流程

### 4.1 規模

5 區 × 約 30 天 = 約 150 格，33 位醫師。決策變數約 5000 個布林。
**這是 CP-SAT 的秒級規模。**

以 9 個假日的月份計，全月需求約 195 額度點數，全院供給約 231 點，**餘裕約 19%**。

### 4.2 變體以權重組合產生

`POST /api/solver-jobs` 產生 3 份變體，方法是**用不同的軟約束權重組合各求解一次**
（ADR-0003），而非列舉次佳解。每份變體代表一種取捨立場（重視公平／重視延續性／平衡），
比較畫面才有話可講。加一條輕量的多樣性約束，防止兩組權重湊巧解出同一份。

三份**序列**求解——多樣性約束需要看到前一份的結果。每份 15 秒上限，總計 45 秒。

### 4.3 唯一決策相依的目標項

公平性點數表雖然要看「隔日」，但當日與隔日是否為假日**都是行事曆常數**，
所以 `公平性點數(人, 日)` 是純查表常數。

全部規則裡只有一條真正決策相依：**「週六值班且下週六又值班 → +1 點」**，
它是兩個值班變數的乘積，需要 reification。這在 CP-SAT 是輕負擔。

### 4.4 跨月邊界

`MinGap`（值休休）、NP 的 `MaxConsecutive`、「連值兩個週六」三條規則都跨月：
9/30 值班的人 10/1、10/2 不能再值。單看一個月，每月 1、2 號的違規系統會看不見。

求解與驗證都以**上個月最後 `max(minGap, maxConsecutive)` 天的值班**為固定輸入——
草稿或已發布皆可，草稿時 `SolverJob.warnings` 提醒。跨月的違規歸屬**後一個月**。
同理，上月尚未發布時月結轉為空，也走 `warnings`，不擋。

公平性點數的「隔日」在 12/31 是隔年 1/1，後端行事曆查表必須跨年。

### 4.5 覆蓋在求解器裡不是硬約束

「每日每區恰好 1 人」在驗證層是硬違規，但求解器把它建成**極高權重的軟項**。
理由：登記過多導致無解時，CP-SAT 只會回一句 INFEASIBLE，排班者拿到零資訊。
降成軟項後永遠回得來「空缺最少」的變體，缺在哪一格看得見，`Variant.metrics.vacancies`
與 `/vacancies` 端點才有意義。這是驗證層與求解層對同一份定義**刻意不同**的唯一解讀（ADR-0002）。

### 4.6 變體的權重是乘數

三份變體的權重組合是**乘在使用者設定上的乘數**（`Variant.weightProfile`），
不是替換：使用者把某條軟約束設 0 停用，任何變體都不會偷偷把它打開。
「平衡」變體全部乘 1。乘數表在 `docs/constraint-defaults.md`。

### 4.7 進度推送

**不提供完成百分比。** CP-SAT 不是逐步推進到 100% 的演算法——它可能 3 秒找到好解、
再花 57 秒證明沒有更好的，也可能 50 秒才找到第一個可行解。任何「百分比」都是謊話。

推送的是狀態與收斂資訊：

```jsonc
{
  "status": "running",
  "variantIndex": 2, "variantCount": 3,
  "elapsedSec": 8.4, "timeLimitSec": 15,
  "solutionCount": 5,
  "bestObjective": 142, "bestBound": 138,
  "gap": 0.028          // 0 = 已證明最佳。會跳動，不單調，不可當進度條
}
```

C# → JS 用 `core.PostWebMessageAsJson(...)`。開發期 transport 另提供
`GET /api/solver-jobs/{jobId}/stream`（SSE）。前端的平台分支封裝在
`frontend/src/realtime.ts`——**全專案唯一有平台分支的檔案**。

求解可中止：`DELETE /api/solver-jobs/{jobId}`。

### 4.8 執行模型與 Solver 邊界（2026-09-06 拍板）

**迴圈在 Application，Solver 一次只解一份。** `SolverJobService`（Application）負責排隊、
單一 slot（`SOLVER_BUSY`）、狀態機、讀乘數表、算每一份的實際權重
（使用者設 0 乘任何數仍是 0）、把前幾份的解與「最少差 15 格」丟給 Solver、控制中止與
`variantIndex`。`ISolver` 介面在 Application，實作在 `Scheduler.Solver`，它不知道「變體」：

```
輸入：SchedulingContext、ConstraintSettings（權重已乘好）、要避開的前幾份解、
      最少差幾格、時間上限、進度回呼、取消權杖
輸出：值班清單、找到幾個解、目標值、下界、求解器狀態
```

**變體的指標由 Domain 重算。** Solver 只回值班清單；`Variant.metrics`、`hardViolationCount`、
`softScore` 全部由 Application 拿 `ViolationChecker` 與 `ScheduleScores` 算。
求解器的目標值只用來搜尋，不對外報。這樣變體頁面與驗證頁面的數字保證一致，
建模跟 Domain 定義漂移時會直接看出來。

**漂移守門測試**：Solver 的任一輸出丟給 `ViolationChecker`，硬違規必須為零，
唯一允許的例外是覆蓋（空缺）。情境含 NP 四條、跨月、登記爆量。每個 Solver PR 必過。
這是 ADR-0002 的自動化守門。

**進度事件只有一個來源，兩個訂閱者**：Api 的 SSE endpoint 與 Shell 的
`PostWebMessageAsJson`，收到的 JSON 一模一樣。

**持久化**：狀態轉換（queued、running、結束）時才寫資料庫，逐秒的進度只留記憶體；
查詢 job 時活著就從記憶體讀，結束了就從資料庫讀。程式啟動時把資料庫裡仍是 running 的
job 一律改成 failed，原因「程式重啟中斷」。

**中止保留已完成的變體**：三份序列求解，第 2 份跑到一半中止時第 1 份已完整，
cancelled 的 job 可列出、可套用那一份。

---

## 5. 資料儲存

SQLite 單一檔案 `data/scheduler.db`，WAL 模式，**EF Core** 存取（2026-09-06 推翻 §2 原排除，
理由：推廣到下一個客戶時可能要伺服器版、換資料庫引擎，EF Core 換 provider 即可）。

EF Core 的規矩：

- 只有 `Scheduler.Persistence` 引用 EF Core，架構測試守。
- Domain 是零相依的 record，**不直接當 entity**。Persistence 自帶一套 entity class，
  與 Domain record 互轉的程式碼住在 Persistence。
- Migration 進版控，程式啟動時自動套用（`Database.Migrate()`）。
- 不開 lazy loading，每個查詢明寫要載什麼。
- 列舉存字串且與契約相同（`draft`、`quota_point`……），時間戳存 UTC ISO-8601 字串——
  SQLite provider 拒絕對 `DateTimeOffset` 排序，固定 UTC 之後字串順序就是時間順序。
- 「整份取代」就地同步（同主鍵更新、多的刪、缺的補），不做 delete-all + insert-all：
  同一批裡刪掉再插回同一個主鍵會撞外鍵，change tracker 也不接受。

**設定資料正規化拆表，不存 JSON blob**——資料不能綁死在本程式的序列化格式裡。
主要資料表（概要，實際 schema 由 migration 定義）：

| 表 | 內容 |
|---|---|
| `schedule` | 一列一個月。`(year, month)` 為自然主鍵，**無代理鍵** |
| `duty` | 一列一格值班。`(year, month, area_id, date)` 唯一 |
| `blocked_day` | 不可排班日。**不外鍵到 `schedule`**（ADR-0001） |
| `carry_over` | 某月**發布時結算出**的月結轉（該月的輸出）。重複發布整份覆寫 |
| `carry_over_applied` | 某月**第一次發布時凍結**的上月月結轉（該月的輸入）。之後重新發布不重拍。見 ADR-0004 |
| `staff` | 人員名冊 |
| `area_type` / `area` / `rank_group` / `rank` / `eligibility` / `point_rule*` / `constraint*` / `monthly_override` | 設定，正規化。PUT 整份取代時就地同步（同鍵更新、多的刪、缺的補） |
| `calendar_day` | 行事曆**只存例外日**：國定假日、補班日、使用者覆寫。週六日讀取時算出來。內建只有 2026，之後年份先由使用者逐日覆寫 |
| `solver_job` / `variant` / `variant_duty` | 求解紀錄。**全部保留，不做清理**。進度不寫，只寫狀態轉換 |

不進資料庫的：「每人每月最多登記 16 天」是常數，住在 `Scheduler.Domain.Defaults`，
契約沒有改它的 API（`docs/constraint-defaults.md` 第 119 行）。

**`SchedulingContextLoader` 在 Application 層。** 組一份 `SchedulingContext` 要撈十幾樣東西
（當月行事曆加次月 1 號、上月月尾的值班、月結轉、逐月覆寫、人員、五份設定……），
跨月與跨年的撈取規則只在它一個地方，其他 handler 都叫它，不自己組。
Persistence 只提供單純的「給我某月的 duty」「給我某年的例外日」這類介面。

**所有執行期狀態寫在程式旁的 `data/`**，不得碰 `%APPDATA%` / `%LOCALAPPDATA%` / 登錄檔。
這是 portable 的硬性要求，也是驗收項目。

**沒有樂觀鎖。** `revision` 只是修改次數計數器，供 UI 顯示（「草稿 v2」）與
前端快取失效。單一使用者、單一 process，不存在併發寫入。
若未來要做多人版，那時再加 `etag`／`If-Match`——那是後端的事，前端的 `fetch` 不用改。

---

## 6. WebView2 橋接規格

### 6.1 環境初始化

```csharp
var env = await CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: Path.Combine(AppContext.BaseDirectory, "webview2"),
    userDataFolder:          Path.Combine(AppContext.BaseDirectory, "data", "wv2data"));
await webView.EnsureCoreWebView2Async(env);
```

`userDataFolder` **不可省略**。預設會寫入 `%LOCALAPPDATA%`，違反 portable 前提。

### 6.2 靜態資產與 API 攔截

```csharp
var core = webView.CoreWebView2;

core.SetVirtualHostNameToFolderMapping("app.local",
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    CoreWebView2HostResourceAccessKind.Allow);

// Scheduler.Api 的 WebApplication 以 TestServer 在 process 內跑起來，不開 socket。
// Shell 只拿到一個 HttpClient，路由、binding、錯誤碼對應全部在 Api 那一份（§3.2 規則 2）。
var app = Scheduler.Api.ApiHost.Build(useTestServer: true);
await app.StartAsync();
var apiClient = app.GetTestClient();

core.AddWebResourceRequestedFilter("https://app.local/api/*", CoreWebView2WebResourceContext.All);
core.WebResourceRequested += async (s, e) =>
{
    var deferral = e.GetDeferral();            // 支援非同步，長時間運算必須
    using var res = await apiClient.SendAsync(ToHttpRequestMessage(e.Request));
    e.Response = core.Environment.CreateWebResourceResponse(
        await res.Content.ReadAsStreamAsync(), (int)res.StatusCode, res.ReasonPhrase, FlattenHeaders(res));
    deferral.Complete();
};

webView.Source = new Uri("https://app.local/index.html");
```

求解進度不走這條路（WebResourceRequested 的回應不會漸進送出），Shell 從
`app.Services` 取進度事件來源，訂閱後 `PostWebMessageAsJson`（§4.7、§4.8）。

### 6.3 SPA deep link fallback

`SetVirtualHostNameToFolderMapping` 是檔案對應而非 web server，重新整理在子路徑會 404。
需額外攔截 Document 請求：非 `/api/` 且無副檔名者一律回 `index.html`。

### 6.4 匯出檔案

`GET /api/schedules/{ym}/export` **直接回檔案位元組**，不回下載連結。
前端用 `URL.createObjectURL` 觸發，WebView2 走原生下載流程、跳系統存檔對話框，
使用者存到哪裡是他家的事，不碰 `data/`。

雲端版一字不用改——這也是不採用「回一個帶 `expiresAt` 的 URL」的理由：
portable 環境沒有 HTTP server 能提供那種連結，也沒有可放暫存檔的地方。

**列印純前端**（CSS `@media print`），不需要任何端點。

### 6.5 除錯

```csharp
core.Settings.AreDevToolsEnabled = true;   // Debug 建置開啟，Release 關閉
```

---

## 7. 開發流程與分工

```
開發期:  Vite dev server ──proxy /api──> dotnet run (Scheduler.Api)
正式版:  WPF + WebView2 攔截 ─────────> 同一個 Scheduler.Api pipeline（TestServer，process 內）
```

前端開發者**完全不需要安裝 .NET 或 WebView2**。搭配 MSW mock，他連 backend 都不用跑。
兩人的唯一耦合點是 `api-contract.yaml`。

此流程的關鍵好處：**遷移路徑每天都在被實際使用**，不會等到要遷移時才發現有問題。

**後端對契約的守法方式**（2026-09-06 拍板）：DTO 手寫，不從 yaml 生成 C#。
守法靠兩件事：
- CI 測試把 `Scheduler.Api` 跑起來逐個 endpoint 打，回應 JSON 用 `api-contract.yaml` 的
  schema 驗。形狀與語義（狀態碼、錯誤碼）都抓得到。跟著 endpoint 逐個加，不一次寫 42 個。
- `frontend/scripts/smoke-mock.ts` 的 base URL 可切換，同一份斷言打 MSW mock 與真後端各一次，
  兩邊漂移直接看得到。

`frontend/vite.config.ts` 的 `build.target` 對齊隨附的 WebView2 版本
（釘選在 `build/webview2.json`）。**改一邊就要改另一邊。**

鎖定 Chromium 版本是隨附 fixed-version 的免費好處——不需 polyfill 與 legacy transpile。
**代價是必須在真正的 fixed-version runtime 內測試**，不可只在最新版 Chrome 驗證。

---

## 8. 發佈設定

```xml
<PublishSingleFile>false</PublishSingleFile>   <!-- 資料夾式，不要自解壓 -->
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishTrimmed>false</PublishTrimmed>         <!-- OR-Tools 反射會被裁掉 -->
```

- **不要開 trim，不要開 AOT** — OR-Tools 的 P/Invoke wrapper 兩者皆不保證相容
- `RuntimeIdentifier` / `SelfContained` **寫在 `Scheduler.Shell.csproj` 本身**。
  曾經放在 `Directory.Build.props` 用 `IsPublishable` 條件式設定，結果條件永遠為假
  （props 在專案本體之前匯入，看不到專案裡宣告的屬性），publish 出來是 framework-dependent
  的 26 個檔案——沒裝 .NET 的機器直接起不來。這件事靠 §9 的 native 相依檢查與檔案數才發現
- WebView2 Fixed Version 由 `build/fetch-webview2.ps1` 下載，版本釘選在 `build/webview2.json`
- **VC++ runtime 三個檔案 app-local 隨附**（`msvcp140.dll`、`vcruntime140.dll`、`vcruntime140_1.dll`），
  由 `build/publish.ps1` 從 Visual Studio / Build Tools 的 `VC\Redist\MSVC\<ver>\x64\Microsoft.VC143.CRT`
  複製到發佈包根目錄。理由見 §9
- `build/check-native-deps.ps1` 掃描發佈包內每個 PE 檔的 import table，任何既不在包內、
  也不是 Windows 自帶的 DLL 都會讓 publish 失敗。它是靜態分析，**取代不了乾淨機器實測**，
  但能在開發機上擋住「少帶一個 DLL」這一類的退化

發佈產物：

```
HospitalScheduler/
├─ HospitalScheduler.exe
├─ *.dll                         # .NET self-contained runtime + 受管組件
├─ ortools.dll, google-ortools-native.dll, abseil_dll.dll, libprotobuf.dll,
│  libscip.dll, highs.dll, re2.dll, zlib1.dll, bz2.dll, libutf8_validity.dll
│                                # OR-Tools native（RID-specific publish 時直接落在根目錄，
│                                #  不在 runtimes/ 之下）
├─ e_sqlite3.dll                 # SQLite native，同上
├─ msvcp140.dll, vcruntime140.dll, vcruntime140_1.dll
│                                # VC++ runtime，app-local，由 publish.ps1 放入
├─ runtimes/win-x64/native/      # 只剩 WebView2Loader.dll 的副本（套件行為，無害）
├─ webview2/                     # Fixed Version runtime（~180MB）
├─ wwwroot/                      # 前端 build 產物
└─ data/                         # 所有狀態，含 scheduler.db 與 WebView2 user data
```

---

## 9. 已知風險與必驗項目

| 項目 | 說明 | 何時驗 |
|---|---|---|
| **VC++ Runtime** | **已確認依賴，已處理。** 見 §9.2 | 乾淨機器實測仍列在 §10 |
| **SQLite native 部署** | **已確認。** `e_sqlite3.dll` 只 import `KERNEL32.dll`（CRT 靜態連結），RID-specific publish 時落在根目錄，載入正常 | 已完成 |
| **NP 路徑幾乎測不到** | NP 只有 1 人且是後備人力，四條專屬規則在真實資料下極少觸發。**必須寫不依賴真實資料的單元測試**，否則是最容易腐爛的一塊 | 實作 Solver 時 |
| 唯讀路徑 | 程式可能被放在無寫入權限的位置，啟動時需檢查並明確報錯 | 早期 |
| SmartScreen | 未簽章 exe 會觸發警告。需事先告知案主 | 交付前 |
| 更新機制 | portable 版無自動更新，第一版接受手動換資料夾 | 已接受 |

### 9.1 「R2/R3 優先 ICU」的壓力來自登記量，不是結構

> 本節第一版寫成「結構性無法滿足」，是算錯了：把額度**上限**當成必須用滿的**配額**。
> `Budget` 只約束 ≤，沒有任何目標項要求 R4~R6 把 69 點用完；組內公平比的是剩餘額度，
> 13 個人各剩 3 點一樣公平。

零登記時的正確算式（9 個假日的月份）：R4~R6 只值總值 39 點 → ICU 39 點全給 R2/R3 →
R2/R3 剩 13 點去一般病房 → 病房 117 − 13 = 104 點由低年級（PGY/R1/打工R）的 110 點供給。
**兩條偏好都能完全滿足**，只是低年級利用率 95%、很緊。

真正的動態是：**低年級的不可排班日越多，資深就越被拉進 ICU、擠掉 R2/R3。**
`S3_R2R3_PREFER_ICU` 被犧牲的程度是登記量的函數，所以：

- 權重用一般值 50，不特別壓低
- 可行性預警的 `bySupply` 用巢狀累計（總值 ⊂ 資深；總值＋ICU ⊂ 資深＋中階；全部 ⊂ 全部）
  算供需，「一般病房需求 vs 低年級剩餘供給」那一層就是這條偏好會不會被犧牲的先行指標
- 逐區域類型各算各的供給是錯的——R2 會同時被算進 ICU 與一般病房

### 9.2 OR-Tools native 程式庫依賴 MSVC runtime——已確認，app-local 隨附

2026-09-02 以 `Google.OrTools 9.15.6755` + `Microsoft.Data.Sqlite 8.0.11` 做 self-contained
win-x64 publish，解析每個 native DLL 的 PE import table（`build/check-native-deps.ps1`），結果：

| DLL | 依賴 |
|---|---|
| `ortools.dll`、`google-ortools-native.dll`、`abseil_dll.dll`、`libprotobuf.dll`、`libscip.dll`、`highs.dll`、`re2.dll` | `MSVCP140.dll`、`VCRUNTIME140.dll`、`VCRUNTIME140_1.dll` + UCRT（`api-ms-win-crt-*`） |
| `zlib1.dll`、`bz2.dll`、`libutf8_validity.dll` | `VCRUNTIME140.dll` + UCRT |
| `e_sqlite3.dll` | 只有 `KERNEL32.dll` |
| .NET runtime 自身（`coreclr.dll`、`clrjit.dll`、`hostfxr.dll`…） | 只有 UCRT |
| WPF native（`wpfgfx_cor3.dll`、`PresentationNative_cor3.dll`…） | 自帶 `vcruntime140_cor3.dll`，不依賴系統版 |

結論：

- **`Google.OrTools` 的 NuGet 套件不附帶 `msvcp140` / `vcruntime140` / `vcruntime140_1`。**
  開發機因為裝了 Visual Studio 所以載得起來，乾淨的 Windows 上會在第一次呼叫 CP-SAT 時
  `DllNotFoundException`（實際錯的是它的相依，訊息只會說找不到 `google-ortools-native`）
- UCRT（`api-ms-win-crt-*`、`ucrtbase`）自 Windows 10 起是作業系統元件，**不需要**隨附
- 處置：把三個 DLL **app-local** 放在 exe 旁。Microsoft 允許此部署方式（它們在 VC redist
  的可轉散發清單裡），Windows 的 DLL 搜尋順序會先找應用程式目錄，所以就算目標機器裝了
  別的版本也不互相干擾。**不需要**執行 `vc_redist.x64.exe`，portable 前提維持
- `build/publish.ps1` 第 3 步負責複製，找不到來源時直接失敗並說明要裝哪個元件；第 6 步跑
  `check-native-deps.ps1` 再驗一次。這條檢查對 OR-Tools 升版同樣有效——哪天它多依賴一個
  DLL，publish 會當場擋下

同一次驗證順帶抓到 §8 說的 `IsPublishable` 條件失效問題：之前的 publish 根本不是 self-contained。

**還沒做的**：在一台沒有 .NET、沒有 VC++ Redist 的 Windows 上實際啟動並跑一次求解。
靜態分析證明「沒有懸空的 import」，證明不了「載入順序與版本相容都對」。這一項留在 §10。

---

## 10. 架構驗收檢查清單

- [ ] 在乾淨的 Windows（無 .NET、無 WebView2、無 VC++ Redist）解壓即可執行
- [ ] `%APPDATA%` / `%LOCALAPPDATA%` / 登錄檔無任何寫入
- [ ] 複製整個資料夾到另一台機器，所有資料與設定完整保留
- [ ] 程式放在唯讀路徑時，啟動有明確錯誤訊息而非崩潰
- [ ] 求解期間 UI 不凍結，**狀態與收斂資訊即時更新（非百分比進度）**，可中止
- [ ] 求解過程中斷電，重啟後資料庫無損毀、已存的草稿完整
- [ ] 求解中斷電重啟後，該 job 顯示 failed（程式重啟中斷），不卡在 running
- [ ] 已發布月份的「由上月帶入」偏移，在上月被改動並重新發布後**不變**（ADR-0004）
- [ ] SPA 子路徑重新整理不會 404
- [ ] 匯出 Excel 會跳出系統存檔對話框，且未在 `data/` 以外留下暫存檔
- [ ] 4K 螢幕與 1080p 外接螢幕間拖曳視窗，DPI 縮放正常
- [ ] 列印輸出正常（A4）
- [ ] 前端 build 產物可直接部署至靜態主機，搭配 `Scheduler.Api` 正常運作（遷移路徑驗證）
