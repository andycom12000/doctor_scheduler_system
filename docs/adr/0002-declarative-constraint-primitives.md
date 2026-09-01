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
