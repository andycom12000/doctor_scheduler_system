# 前端實作計畫

2026-09-12 盤點後擬定。後端 42 個操作已全部落地、Shell 已能載入 `wwwroot/`，
前端目前只有環境檢查畫面與一套完整的 MSW mock。這份文件回答三件事：
**現況差距在哪、用什麼做、依什麼順序做**。

術語依 `CONTEXT.md`；畫面依 Claude Design 專案的 TURN 3／TURN 4 canvas（已套用
`docs/design-revisions.md` 的修訂）；端點依 `api-contract.yaml`。

---

## 0. 現況盤點

### 0.1 後端：可以當真的用

| 層 | 狀態 |
|---|---|
| Domain / Persistence / Application / Api | 契約 42 個操作全部落地，契約守法測試（`tests/Scheduler.Api.Tests`）每個端點都驗 |
| Solver | CP-SAT 落地，漂移守門與目標值方向守門都在 CI 跑 |
| Shell | WebView2 host 同一個 `ApiHost`；`/api/` 攔截、靜態檔、無副檔名路徑回 `index.html`（deep link 可用）、進度用 `PostWebMessageAsJson` 推 |
| 種子 | 設定、行事曆與參考名單 34 人（#25）。名單是否種由 `ApiHostOptions.SeedReferenceRoster` 控制，發佈包關閉（#37）。開發期後端（`dotnet run`）預設仍種，前端可照舊依賴 |
| WebView2 使用者資料夾 | 已導到 `data/wv2data`，前端用 `sessionStorage`／`localStorage` 不違反 portable 規則 |

### 0.2 前端：只有骨架與 mock

| 項目 | 狀態 |
|---|---|
| `App.vue` | 環境檢查畫面，要整個換掉 |
| `api/client.ts`、`realtime.ts`、`vite.config.ts` | 定案，不動 |
| `api/types.ts` | 契約型別別名已攤平，可直接用 |
| MSW mock | 42 個操作都有 handler，含 34 人名冊、2026-08 已發布、2026-09 草稿、貪婪求解器與 SSE。**issue #20**：`listVariants` 對不存在的工作回 200 空陣列、`applyVariant` 缺欄位／跨月回 404，與契約的 404／422 不符 |
| 煙霧測試 | `smoke-mock.ts` 同一份斷言打 mock 與真後端；除此之外沒有任何前端測試 |
| CI | `npm run typecheck` 與 `npm run build` 已在跑 |
| 樣式 | `styles.css` 23 行，字型用 Segoe UI／微軟正黑體 |
| 路由 | 無 |
| 建置產物 | `public/mockServiceWorker.js` 會原樣進 `wwwroot/`，隨發佈包出貨（無害，但不該在裡面） |

### 0.3 設計稿：版面與互動已定，可以照著做

Claude Design 專案「醫院護理排班系統UI設計」的 `排班系統 API 註解.dc.html`
TURN 3 與 TURN 4 已經是修訂後的版本：5 區、33 人、10 身分、4 身分組、7／7 約束、
兩個筆刷、兩態人員狀態、無角色標籤。六個畫面加一個覆蓋層：

| 畫面 | 內容 | 版面已定？ |
|---|---|---|
| SCREEN 01 排班主表 | 三個檢視分頁（區域 × 日／日 × 人／單日詳表）、年月切換、工具列、點數看板、身分組容量利用率、違規側欄、圖例 | 是，但格線比例（5 列）要在實作時拿捏 |
| SCREEN 02 區域與點數 | 區域類型／區域、10 身分表（含 R6 本月覆寫）、額度點數、公平性點數 Type A／B 兩張 4 列查表、連值週六加分 | 是 |
| SCREEN 03 資格與約束 | 10 × 3 資格矩陣、7 硬約束、7 軟約束（權重 0–100、三維 scope、「避開」只顯示方向） | 是 |
| SCREEN 04 變體比較 | 短編號、規模、警告列、每份變體的立場與乘數、4（＋1）指標、5 × 30 熱圖、逐格差異、套用 | 是 |
| SCREEN 04b 求解進度覆蓋層 | 第 n／N 份、已耗時／上限、候選解數、gap（數字，**不是進度條**）、三份變體狀態、中止 | 是 |
| SCREEN 05 不可排班日登記 | 筆刷式矩陣（登記／清除）、底部兩列（登記人數／總值可用人數）、登記概況、可行性預警逐日與整月供需、帶入求解 | 是 |
| SCREEN 06 人員維護 | 左清單（身分組與狀態篩選）、右表單、兩態狀態、刪除 409 說明 | 是 |

掛載的設計系統是 **Industry**：淺灰底、鋼藍單一強調色、Barlow Condensed 標題、Barlow 內文、
直角、髮線邊框、四角「+」定位記號。字型走 Google Fonts。

---

## 1. 技術選型（前端內部，不牴觸 ARCHITECTURE §2）

| 項目 | 決定 | 理由 |
|---|---|---|
| 路由 | `vue-router` history 模式 | Shell §6.3 已對無副檔名路徑回 `index.html`，子路徑重新整理不會 404。六個畫面對應六條路由，年月放在路徑（`/schedules/2026-09`） |
| 狀態 | Vue 組合式函式（composables），**不裝 Pinia** | 全域狀態只有「目前年月」與「進行中的求解工作」兩樣，`provide/inject` 加一個 composable 就夠。裝 Pinia 是為了不存在的複雜度 |
| 資料快取 | 自寫 `useResource(key, fetcher)`，key 帶 `ym` 與 `revision`，**不裝 vue-query** | 寫入回應都帶新的 `revision`，失效規則是「同一 `ym` 的所有讀取」一條而已。vue-query 的重試／背景更新／視窗聚焦重抓在單機 process 內 fetch 上全是雜訊 |
| 圖示 | `lucide-vue-next` | Industry 指定 Lucide，stroke 1.5 |
| 樣式 | 純 CSS + CSS 變數，`styles.css` 移植 Industry 的 token（色階、間距、字型、圓角 4px）。**不載入 `_ds_bundle.js`**，那是 canvas editor 用的 | 密集格線靠 CSS Grid，不需要元件庫；Industry 的元件類別（`.btn`、`.table`、`.tag`、`.dialog`）自己用 scoped CSS 重寫，避免整份設計系統 CSS 進 bundle |
| 字型 | **Barlow 與 Barlow Condensed 自行隨附**（Latin 子集，woff2，每個字重約 20 KB）；**中文用系統字型** `"Microsoft JhengHei UI"`，**不隨附 Noto Sans TC** | 目標機器可能沒有網路，Google Fonts 連結會靜默失敗。Noto Sans TC 每個字重數 MB，塞進 bundle 沒有意義；Windows 一定有微軟正黑體 |
| 拖拉 | **自刻 Pointer Events**，範圍限定為「拖一格到另一格 → 對調」 | 唯一的拖拉場景是 swap。見 §3.1 的 spike 條件 |
| 測試 | `vitest` 測純函式；`mock:smoke`／`api:smoke` 守漂移；固定版 runtime 驗證列 `ready-for-human` | 目前零測試。純函式（逐格差異、熱圖分桶、違規 code → 樣式、點數看板分組、行事曆索引）是最值得測的，也最便宜 |
| 列印 | 純 CSS `@media print`，A4 橫式 | ARCHITECTURE §6.4 與 §10 驗收項 |

新增的相依只有三個：`vue-router`、`lucide-vue-next`、`vitest`。其他任何套件進來都要在 PR 裡寫一行理由。

---

## 2. 基礎 PR（做任何畫面之前，一個分支）

`feature/frontend-foundation`（#26）。做完之後五張畫面 PR 平行開工，所以**這個 PR 要把所有共用表面一次做完**（§4 的規則 1）：
六條路由全註冊、各指向 `src/pages/<screen>/index.vue` 的佔位頁；共用 `PageLayout`（標題列、動作 slot、可捲動本體）與 `ConfirmDialog`；
`src/api/` 的 42 個操作函式全部寫好；`smoke-mock.ts` 在第一波凍結。

1. **修 issue #20**（mock 的 `listVariants` 404 與 `applyVariant` 422），順手在 `smoke-mock.ts` 第 11、12 段加斷言。mock 是開發床，這兩個狀態看不到，SCREEN 04 就會做錯。
2. **路由與殼**：`vue-router`、六條路由、左側導覽（六個項目加「本機執行 · 免安裝」）、頂列年月切換器、「填滿視窗、內部捲動」的版面骨架（#52 後改為頂部橫列導覽、年月切換器移入 PageLayout 標題列）。`index.html` 的 `<title>` 改成「醫師值班排班系統」。**Shell 的起始網址是 `https://app.local/index.html`**（`WebViewBridge.IndexUri`），router 要加一條 `/index.html → /` 的 redirect，否則正式版開起來對不到任何路由；Shell 不改。
3. **設計 token**：把 Industry 的 `:root` 變數（`--color-*` 100–900 色階、`--font-*`、`--space-*`、`--radius-*`、`--shadow-*`）移植進 `styles.css`；自行隨附 Barlow 字型；`:focus-visible` 用強調色外框；假日底色、值班格、不可排班日、空缺、違規三種渲染（底色／斜紋／外框）的 token 一次定好。
4. **資料層**：`src/api/` 依契約 tag 拆檔（`schedules.ts`、`views.ts`、`blockedDays.ts`、`solver.ts`、`settings.ts`、`calendars.ts`、`staff.ts`），每個函式一個操作；`useResource` 快取 composable；`useYearMonth` 全域年月；`ApiError` 的 `ErrorCode → 使用者訊息` 對照表，契約的 `ErrorCode` 列舉 13 個全部列（`HARD_VIOLATIONS_PRESENT` 與 `SCHEDULE_ALREADY_PUBLISHED` 各有專屬流程，見 §3.1、§3.5）。
5. **行事曆索引**：`useCalendar(year)` 把 `GET /calendars/{year}` 攤成 `Map<date, CalendarDay>`，所有畫面的表頭、假日底色、額度點數值都從這裡拿，不各自算。
6. **建置**：`vite.config.ts` 加一條 `build` 期排除 `mockServiceWorker.js` 的規則（或把它從 `public/` 移到 mock 模式才注入）。`build.target` 不動。
7. **測試底座**：`vitest` 裝好、`npm test` 指令、CI 加一步。

---

## 3. 逐畫面拆解

每個畫面一個 issue、一個 `feature/frontend-screen-XX` 分支、一個 PR。第一波的目錄規則見 §4。
每條列：消費的端點、前端自己擁有的邏輯、設計修訂清單裡**沒有端點在管、只能由前端守**的行為。

### 3.1 SCREEN 01 排班主表（最大）

端點：`GET /schedules`、`GET /schedules/{ym}`、`PATCH duties`、`POST duties/swap`、`POST validate`、`POST publish`、`GET export`、`GET violations`、`GET point-board`、`GET days/{date}`、`GET vacancies`、`GET candidates`、`GET /calendars/{year}`、`GET /settings/areas`（一般月份的區域中繼資料）、`GET /settings/constraints`（只為了知道 S7 是否停用）、`GET /staff`、`GET /settings/ranks`（空月份骨架的身分組色帶）、`POST /solver-jobs`（空月份「開始求解」）。

前端擁有的邏輯：
- **空月份狀態**（issue #62，SCREEN 01 V02）：年月切換器一定會落到沒有值班表的月份，`GET /schedules/{ym}` 回 404。不顯示錯誤，改畫「日 × 人」密集矩陣骨架（欄＝在職人員依身分組排列、列＝當月日數），中央疊霧化遮罩與貼齊格線的訊息面板，唯一動作是「開始求解」（`POST /solver-jobs` 建工作後導去變體頁接手進度）。空狀態只打 `GET /staff` 與 `GET /calendars/{year}`（加上年月切換器自己的 `GET /schedules`），不打 `settings/areas`／`settings/constraints`／點數看板／違規／空缺／候選人；不再有「點格自動建草稿」與「去登記不可排班日」兩個入口，求解才是產生值班表的唯一路徑。
- 三個檢視共用同一份 `Schedule`，各自只是投影。區域 × 日是 5 列 × 30 欄；日 × 人是 30 列 × 34 欄（欄依 4 個身分組排列，右側「空缺」欄）；單日詳表用 `days/{date}` 自帶的月負載，不再打點數看板。
- **主要互動是點格**：點一格開候選人面板（`GET candidates`），列出剩餘額度、同區延續比例、`blockingReasons`。**有阻擋理由仍可選**，寫入不會被拒，面板只是把理由標紅。
- **次要互動是拖拉對調**：拖一格到另一格呼叫 `swap`。這是唯一的拖拉場景，用 Pointer Events 自刻。README 說拖拉體驗撐不住是唯一值得換框架的理由，所以這一段訂成**有時間盒的 spike**：一天內做出「拖動有預覽、放下後對調、Esc 取消」三件事就 go，做不到就退回「點兩格 → 對調」按鈕，不換框架。
- 違規 `code` → 渲染樣式對照表由前端擁有：H1 空缺用外框、H5 排到不可排班日用斜紋、其餘硬違規底色、軟項不上格。這張表是純函式，進 vitest。
- 點數看板依身分組分區，主欄是額度點數（已排／上限、剩餘、假日班），**公平性點數欄只在 S7 權重 > 0 時顯示**。「由上月帶入」欄的值直接用 `carryOverApplied`，語義（草稿即時、已發布凍結）由後端決定，前端只在已發布時加「發布時凍結」提示。
- 身分組容量利用率由點數看板就地算（組內 Σ已排 ÷ Σ上限）。

只有前端守的行為（設計修訂清單）：
- **修改已發布值班表的第一格**時跳一次確認。以「本次進入畫面後是否已確認過」為狀態，換月份重置。
- **匯出或列印仍是草稿的值班表**時提示「尚未發布，要先發布再匯出嗎？」，可以不發布繼續。
- 工具列的「發布」在已發布時顯示「重新發布」；發布成功後顯示 `PublishResult` 的月結轉摘要。
- 發布時仍有硬違規會回 409 `HARD_VIOLATIONS_PRESENT`：前端跳確認，使用者同意後帶 `acknowledgeViolations: true` 再發一次。這是「明確確認」流程，不是擋。
- 匯出走 `fetch` 拿位元組 → `URL.createObjectURL` → 隱藏 `<a download>`，WebView2 會走原生存檔對話框（§6.4）。

### 3.2 SCREEN 02 區域與點數

端點：`GET·PUT /settings/areas`、`GET·PUT /settings/ranks`、`GET /settings/eligibility-matrix`（唯讀，推「可值類型」欄）、`GET·PUT /settings/point-rules`、`GET·PUT /settings/monthly-overrides/{ym}`。

- 五份設定都是**整份取代**，畫面用「載入 → 本地草稿 → 儲存」三態，儲存後重抓。
- R6 的額度上限走當月覆寫：那一列多一個「指定月份覆寫」輸入與「已覆寫」標記，寫的是 `monthly-overrides/{ym}`，不是 `ranks`。
- 覆寫用的月份選擇器獨立於標題列年月切換器（`useYearMonth`），只影響 `monthly-overrides/{ym}` 這個 key、不寫路由（issue #45 方案 B）。
- NP 的上限與點數類型顯示「不計」，對應 `quotaCap: null`。
- 公平性點數兩張 4 列查表（當日 × 隔日是否假日）與連值週六加分（點數、天數視窗）依 `PointRules` schema 畫，不自己發明欄位。
- 假日認定不在這頁，放一句說明指向行事曆。

### 3.3 SCREEN 03 資格與約束

端點：`GET·PUT /settings/eligibility-matrix`、`GET·PUT /settings/constraints`。

- 10 × 3 矩陣勾選；儲存後 SCREEN 06 的「可值區域類型」與 SCREEN 02 的「可值類型」欄都會跟著變（由後端推導，前端只需失效快取）。
- 硬約束 7 條唯讀顯示（名稱、代碼、範圍、參數、原語）；只有可停用的那幾條（如 H2）給開關，依 `HardConstraint` schema 的欄位決定。
- 軟約束 7 條：權重 0–100 輸入，0 即停用；範圍顯示身分、區域類型、日類三維；「避開」類只顯示方向。
- S7 權重改動會影響 SCREEN 01 與 04 的公平性欄位顯示，儲存後失效 constraints 快取即可。

### 3.4 SCREEN 05 不可排班日登記

端點：`GET /blocked-days/{ym}`、`PUT·DELETE /blocked-days/{ym}/{staffId}/{date}`、`GET /blocked-days/{ym}/feasibility`、`GET /staff`、`GET /calendars/{year}`、`POST /solver-jobs`。

- 矩陣 34 列 × 30 欄，列依身分組排、欄從行事曆索引拿假日底色。
- 兩個筆刷（登記／清除），點格即寫入；**按住拖過多格連續塗**是筆刷的本意，用 Pointer Events 做，與 SCREEN 01 的拖拉共用 pointer 工具函式。
- 每次寫入回 `BlockedDayMutationResult`，直接更新該人與該日的計數，不整份重抓；409 超過上限時提示剩餘額度。
- 可行性預警兩層照 `FeasibilityReport` 畫：逐日列 `shortages`（總值最危險）；整月三列巢狀供需（`headroom` 負數標紅）。「一般病房 vs 低年級剩餘供給」吃緊時的「R2/R3 優先 ICU 會被犧牲」提示，前端依 `bySupply` 最後一列與倒數第二列的 `headroom` 差就地判斷（ARCHITECTURE §9.1）。這個算法**只在資格是巢狀時才成立**（總值 ⊂ 資深、總值＋ICU ⊂ 資深＋中階），而資格矩陣是使用者可改的；前端不驗巢狀性，只在 `bySupply` 恰好是三層時顯示這句提示，層數不對就不顯示。
- 「帶入求解」按下先一次確認（沒有登記期機制），成功後帶著 `jobId` 導到 SCREEN 04 並開覆蓋層；409 `SOLVER_BUSY` 時用 `details.jobId` 導到正在跑的那一個。

### 3.5 SCREEN 04 變體比較 ＋ 04b 進度覆蓋層

端點：`POST /solver-jobs`、`GET /solver-jobs/{jobId}`、`DELETE /solver-jobs/{jobId}`、`GET /solver-jobs/{jobId}/variants`、`POST /schedules/{ym}/apply-variant`、`realtime.ts` 的 `subscribe`。

- **`GET /solver-jobs/{jobId}` 是真相來源**，推送只是加速：進畫面先 GET 一次（工作可能在訂閱前就結束），再 `subscribe(jobId)`，離開畫面必 `unsubscribe`。收到終態事件後再 GET 一次取最終數字。
- 覆蓋層顯示第 n／N 份、已耗時／上限、候選解數、gap。**gap 只能是數字**，不畫成任何長條；三份變體的狀態列（完成／求解中／等待）由 `variantIndex` 推。
- 中止後仍列出已完成的變體，可套用（§4.8）。
- **失敗狀態**：`status: failed` 時覆蓋層與變體頁都顯示 `failureReason`；程式重啟時後端會把仍在 running 的工作改成 failed、原因「程式重啟中斷」（§4.8），這是最常見的失敗，文案要讓使用者知道重新求解即可。`SOLVER_FAILED` 錯誤碼同樣走這條。
- 契約沒有「列出工作」的端點，重新整理或重開程式會丟 `jobId`：前端把每個月最近一次的 `jobId` 寫進 `localStorage`（鍵含 `ym`；使用者資料夾在 `data/wv2data`，不違反 portable），進 SCREEN 04 時若有就 GET 一次。`GET /solver-jobs/{jobId}` 在工作離開記憶體後從資料庫讀，所以重開程式後也能恢復，**不需要後端加端點**。
- 熱圖（5 列 × 30 欄，色階＝身分組）與逐格差異（兩份 `duties` 依 `cellKey` 比對）全是純函式，進 vitest。
- 指標列 4 個固定（空缺、額度點數公平、同區延續、身分區域偏好），`fairnessPoint` 非 null 且 S7 開著時才顯示第 5 個。空缺 > 0 用醒目但非錯誤的樣式，文案「登記過多時的正常結果」。
- `SolverJob.warnings` 放在標題下的警告列。
- 該月已發布時 apply-variant 回 409 `SCHEDULE_ALREADY_PUBLISHED`：套用按鈕在已發布月份直接停用並說明「已發布的值班表只能逐格改」，不讓使用者撞 409。

### 3.6 SCREEN 06 人員維護

端點：`GET /staff`、`POST /staff`、`PATCH /staff/{id}`、`PATCH /staff/{id}/status`、`DELETE /staff/{id}`。

- 一次全帶，搜尋與身分組／狀態篩選在前端。
- 右側表單：姓名、員編、身分可編；身分組與可值區域類型唯讀（`eligibleAreaTypes`）。
- 刪除回 409（`STAFF_HAS_DUTIES`）時提示改用停用。

### 3.7 列印與匯出（獨立 PR）

- 列印目標是**日 × 人**檢視（案主指定格式），A4 橫式一頁一個月；隱藏導覽、工具列、側欄；假日底色改成可在黑白印表機辨識的樣式。區域 × 日檢視也給一份列印樣式，但驗收以日 × 人為準。
- 匯出用 §3.1 的 `createObjectURL` 流程，`layout` 參數對應兩種版面。

---

## 4. 執行順序：三波，第一波五張平行

設計修訂清單建議 05 → 02/03 → 01 → 04 → 06，那是**設計稿修訂**的順序。
2026-09-12 第一版計畫寫的是逐步序列；2026-09-13 改成**波次**，目標是最快拿到可以 iterate 的原型。

### 讓平行不衝突的兩條規則

1. **基礎 PR（#26）擁有所有共用表面**：路由表（六條全註冊、指向佔位頁）、`PageLayout`、`styles.css` 的 token、
   `src/api/` 的 42 個操作函式、composables、`ConfirmDialog`、`smoke-mock.ts`。這些在第一波期間凍結。
   凍結只約束第一波五張畫面 PR；第二波 #32 列印需要在 `styles.css` 加 `@media print`，允許
   （目前 `body { overflow: hidden }` 會把 A4 列印裁成一螢幕，#32 要處理）。
2. **畫面 PR 只在自己的目錄裡加檔**：`src/pages/<screen>/` 加自己的元件與測試；只 import `src/api/`，不新增；
   只寫 scoped CSS，不改 `styles.css`；不改路由表、不改 `smoke-mock.ts`。

守住這兩條，五張畫面 PR 之間沒有任何共同檔案，合併順序無所謂、不需要 rebase 解衝突。

### 波次

| 波 | issue | 平行度 | 內容 |
|---|---|---|---|
| 0 | #26 基礎 ＋ #25 後端種子 | 2（前後端零重疊） | #26 是唯一序列瓶頸，範圍已擴大成「把共用表面一次做完」。#25 只動 Persistence 與 smoke 的 mock-only 標記 |
| 1 | #27 SCREEN 06、#28 SCREEN 02＋03、#29 SCREEN 05、#30 SCREEN 04＋04b、#31 SCREEN 01 原型 | 5 | 各自從 #26 合併後的 develop 開分支，各自一個 worktree、一個 implementer agent，全部對 MSW mock 開發。**agent 不開 dev server、不做視覺驗證**，只跑 `typecheck`、`test`、`build`；視覺是整合後案主的事 |
| 整合 | — | 序列 | 依 27 → 28 → 29 → 30 → 31 合進 develop，每合一張跑 `typecheck`、`build`、`mock:smoke`。合完就是**原型** |
| 2 | #34 SCREEN 01 寫入流程、#32 列印匯出、原型 iterate 出來的修正 | 2 ＋ n | #34 與 #32 都只動 `src/pages/schedule/` 與 print CSS，可平行；iterate 修正各自開小 PR |
| 3 | #33 固定版 runtime 驗證 | 人工 | 第二波合完跑一次；之後每個里程碑各一次 |

### 原型是什麼、什麼時候能開

第一波五張合進 develop 之後：

- `npm run dev:mock`：六頁全部能走，mock 有 34 人、2026-08 已發布、2026-09 草稿含兩個空缺、假求解器與 SSE。
  **這是第一個可以 iterate 的點**，重點看 SCREEN 01 的 5 列格線比例、SCREEN 05 的筆刷、SCREEN 04b 的覆蓋層。
- #25 合併後：`dotnet run --project src/Scheduler.Api` ＋ `npm run dev`，對真後端走 06 → 02/03 → 05 → 04 → 01，
  真的 CP-SAT、真的可行性預警。

原型**沒有**的東西：發布、匯出、拖拉對調、已發布確認、列印（全在第二波）。
iterate 時發現的版面問題直接開 `[frontend]` issue 掛 `ready-for-agent`，不必等第二波。

### 第一波每個 agent 拿到的東西

- issue 內文（範圍、端點、驗收、目錄規則）
- `docs/design-ref/screen-0X.html` 與 `industry.css` 的絕對路徑（agent 沒有 DesignSync，這是它唯一看得到設計的地方）
- `docs/frontend-plan.md` §1、§3.X
- 完成定義：`npm run typecheck`、`npm test`、`npm run build` 三個都綠、PR 開向 develop、描述列出偏離設計稿的地方

### 種子拍板

2026-09-12 拍板：開 #25 把參考名單種進 `DefaultDataSeeder`。出廠資料庫會帶假名單，交付前要清。

---

## 5. 測試策略

- **vitest**：逐格差異、熱圖分桶、違規 code → 樣式、點數看板分組與利用率、行事曆索引、可行性「R2/R3 被犧牲」判斷、`ErrorCode` 對照表。全是純函式，不掛 DOM。
- **`mock:smoke`／`api:smoke`**：維持是漂移守門。第一波凍結不動，第二波（#34、#32）再把各畫面新用到的端點斷言補進 `smoke-mock.ts`；對真後端只寫「往返一圈不留痕」的段落。
- **元件測試先不做**。密集格線的價值在視覺，Vue Test Utils 測不到；固定版 runtime 的人工驗證比較划算。
- **固定版 runtime 驗證**：`ready-for-human`，第二波合完先跑一次，之後每個里程碑各一次。

---

## 6. 要案主決定或另開 issue 的事

| 事項 | 建議 |
|---|---|
| 真後端的人員種子 | #25 已種參考名單 34 人；#37 已用 `ApiHostOptions.SeedReferenceRoster` 關掉發佈包裡的種子，開發期後端不受影響 |
| 契約沒有「列出／查詢求解工作」端點 | 前端用 `localStorage` 記每月最近一次 `jobId`，重開程式也能恢復（§3.5），**不必加端點**。只有「想看同一個月的歷史求解紀錄」才需要 `GET /solver-jobs?yearMonth=`，目前沒有這個需求，不開 issue |
| 名冊人數 | mock 是 33 位醫師 ＋ 1 位 NP ＝ 34 列；標題列「33 人」指醫師。畫面上顯示 `GET /staff` 的 `counts.active`，不寫死 |
| `mockServiceWorker.js` 進發佈包 | 基礎 PR 順手排除 |
| 字型 | 依 §1：Barlow 隨附、中文用微軟正黑體。若案主堅持 Noto Sans TC，要接受 bundle 多數 MB |
| issue #20 | 基礎 PR 一併修 |

---

## 7. 建議開的 issue

全部前綴 `[frontend]`，2026-09-12 已建到 GitHub，2026-09-13 依 §4 改成波次（#26 與 #25 已標 `ready-for-agent`，#26 合併後 #27–#31 一起標）：

1. #26 基礎：路由、殼、設計 token、資料層、vitest、修 #20、排除 mockServiceWorker
2. #27 SCREEN 06 人員維護
3. #28 SCREEN 02 區域與點數 ＋ SCREEN 03 資格與約束
4. #29 SCREEN 05 不可排班日登記
5. #30 SCREEN 04 變體比較 ＋ 04b 進度覆蓋層
6. #31 SCREEN 01 排班主表原型（三檢視、點數看板、點格指派）
6b. #34 SCREEN 01 寫入流程（發布、匯出、拖拉對調 spike 的 go/no-go、已發布確認）——第二波
7. #32 列印與匯出
8. #33 固定版 runtime 驗證（`ready-for-human`）

另加 #25 `[backend]` 參考名單種子（已拍板要種）。
