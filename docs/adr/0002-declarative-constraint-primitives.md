# 約束以宣告式原語表達，領域檢查器與求解器解讀同一份定義

案主的 15 條排班規則收斂成九個原語（`ExactCount`、`Eligible`、`Budget`、`MinGap`、
`MaxConsecutive`、`Forbidden`、`Preference`、`Fairness`、`Consistency`），
以資料的形式存在 `Scheduler.Domain`。領域的違規檢查器與 `Scheduler.Solver` 的
CP-SAT 建模器**各自解讀同一份定義**，而不是各寫一份規則實作。

## Context

分層規則要求 `Scheduler.Domain` 與 `Scheduler.Application` 不得引用 OR-Tools
（保留日後替換求解引擎的可能，由架構測試強制）。

但 `POST /schedules/{ym}/validate` 與 `GET /schedules/{ym}/violations` 回答的是
「給定一份**既有**值班表，哪些格子違規」。這不是求解，也不能走求解器——否則 OR-Tools
的相依會被拉進 Application 層，架構測試會擋下來。

於是同一組規則必然出現在兩個地方：領域的檢查器、求解器的建模器。
**兩份手寫實作遲早會漂移**：改了一邊忘了另一邊，症狀是「驗證說沒問題但求解器排不出來」，
或反過來，而且極難除錯。

## Consequences

- 規則的**身分與參數**（代碼、啟用、數值、權重、適用範圍）是領域知識，屬於 Domain；
  **如何求解**才是 Solver 的事。這條線劃得比「Domain 不 import OR-Tools」更實質。
- `GET · PUT /api/settings/constraints` 讀寫的**就是這份定義本身**，
  不需要第三份「設定 → 規則」的對照表。
- 度量（Metric）成為一等公民：額度點數、公平性點數、值班天數。`Budget` 與 `Fairness`
  對它參數化，因此兩套點數共用同一份程式碼，只是兩個度量定義。
- 適用範圍有三個維度：身分、區域類型、日類。NP 的四條特例（豁免值休休、每月天數上限、
  最多連六、盡量不用）與「NP 避開假日」全部以原語的 `scope` 表達，**不在程式裡寫 if**。
- 「同一份定義」指的是**規則的身分與參數**。`Eligible` 的矩陣、`Forbidden` 的登記、
  `ExactCount` 的人數、`Budget` 的上限各自住在自己的聚合根，由原語去讀，不複製進 `params`。
  預設的 7 硬 / 7 軟在 `docs/constraint-defaults.md`，seed、mock、測試 fixture 都從它抄。
- **兩邊對定義刻意不同的解讀只有一處**：`ExactCount`（每日每區恰好 1 人）在檢查器是硬違規，
  在求解器建成極高權重的軟項——登記過多時要回「空缺最少」的變體，不是 INFEASIBLE。
  這個差異寫在原語旁邊，不是散在建模器裡的特例。
- **代價：約束的表達力被限制在這九個原語裡。** 未來若出現表達不了的規則，
  要先擴充原語集合，而不是在檢查器裡偷加一段特例——那會讓這份 ADR 失效。
- **哪些原語產生逐格違規**：硬約束全部，軟約束只有 `Preference`（每一格本身就偏離）。
  `Fairness` 是身分組層級的分數、`Consistency` 是個人層級的分數——「主區」隨其他格子變動，
  逐格標示會隨編輯翻轉——兩者都不產生違規，只出現在點數看板、候選人與變體指標。
  （2026-09 拍板，見 issue #6；契約 `validate` 的說明同步。）
- **不是每條規則都是原語。** 「同一人同一天最多一格」是**結構規則**（代碼 `X1_STAFF_DOUBLE_BOOKED`）：
  不在約束設定裡、使用者不能停用，由 `ViolationChecker` 直接產生**硬違規**
  （一筆違規、`cellKeys` 含他當天所在的每個 `area:` 格）。它之所以不能靠原語：
  序列原語（`MinGap`／`MaxConsecutive`）以日期為單位、先對日期 `Distinct()`，同日兩格會合併成一天，
  任何身分都抓不到。
  **2026-10-03 案主改判（#68）：寫入不再擋，發布與匯出才擋（列印由前端擋）。** 舊決定是 setDuty／swap 直接拒絕
  （`409 STAFF_ALREADY_ON_DUTY`，#7，`EnsureConsistent` 當最後防線）；改判的理由是排班者連續調整時，
  同人同日兩區常只是中間狀態，寫入硬擋會讓多步調整做不下去。現在 setDuty／swap 照常寫入並回報違規，
  發布與匯出遇到它一律 `409 DOUBLE_BOOKING_PRESENT`（前端列印也擋），`acknowledgeViolations` 略過不了
  （一個人物理上不可能同時在兩區，這份班表必然有錯）。實際保證是：發布、匯出、列印的**當下**一定沒有
  同人同日（發布、匯出由後端擋；列印由前端列印前重新確認違規清單擋，確認不了也不印）；已發布後仍可逐格修改而再出現，此時重新發布、匯出、列印都會被擋，直到排除。
  `SchedulingContext.EnsureConsistent` 因此不再對同人同日擲出，「同一格兩個人」仍擲出。
  求解器端仍以「每人每天最多一格」建模，輸出不會有它，漂移守門不受影響。
- **「同一份定義」有自動化守門，且對外報的數字只有 Domain 一份。** Solver 只回值班清單，
  變體的 `metrics`／`hardViolationCount`／`softScore` 由 Application 拿 `ViolationChecker` 與
  `ScheduleScores` 重算，求解器的目標值只用來搜尋。另加一條測試：Solver 的任一輸出丟給
  `ViolationChecker`，硬違規必須為零，唯一例外是覆蓋（空缺）。情境含 NP 四條、跨月、登記爆量，
  每個 Solver PR 必過。（2026-09-06 拍板，見 `docs/ARCHITECTURE.md` §4.8。）
