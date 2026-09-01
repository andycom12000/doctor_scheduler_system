# 約束預設值

`GET · PUT /api/settings/constraints` 那份文件的**出廠內容**。
ADR-0002 主張「規則只有一份定義」——這就是那一份。程式裡的 seed、
MSW mock、Solver 的單元測試 fixture 都從這裡抄，不得各自發明代碼或數字。

形狀見 `api-contract.yaml` 的 `ConstraintSettings`；詞彙見 `CONTEXT.md`；
每一條對應 `constraints.md` 的哪一句寫在「來源」欄。

幾個原語的資料本體住在別的聚合根，**不複製進 `params`**：
`Eligible` 讀資格矩陣、`Forbidden` 讀不可排班日、`ExactCount` 讀 `Area.requiredPerDay`、
`Budget(quota_point)` 讀 `Rank.quotaCap` 與當月覆寫、`Fairness(quota_point)` 讀上月月結轉。

---

## 硬約束（7 條）

| 代碼 | 名稱 | 原語 | scope | metric | params | 來源 |
|---|---|---|---|---|---|---|
| `H1_AREA_COVERAGE` | 每日每區恰好 1 人 | `ExactCount` | 全體 | — | — | 「每天值班需要5人，每區各一人(hard)」 |
| `H2_ELIGIBILITY` | 身分資格 | `Eligible` | 全體 | — | — | 身分表的 ✓ / ✗ |
| `H3_QUOTA_CAP` | 額度點數上限 | `Budget` | `exemptRankCodes: [NP]` | `quota_point` | — | 「每個身分的點數上限是硬門檻不能超過(hard)」 |
| `H4_MIN_GAP` | 值休休值 | `MinGap` | `exemptRankCodes: [NP]` | — | `{ days: 3 }` | 「值班後至少要休假兩天」；「NP…不會有值班後休假兩天的問題」 |
| `H5_BLOCKED_DAY` | 不可排班日 | `Forbidden` | 全體 | — | — | 「每個人最多有16天絕對不可以排班的日子(hard)」 |
| `H6_NP_MONTHLY_DAYS` | NP 每月天數上限 | `Budget` | `rankCodes: [NP]` | `duty_day` | `{ cap: 20 }` | 「NP…一個月最多20天」 |
| `H7_NP_MAX_CONSECUTIVE` | NP 最多連六 | `MaxConsecutive` | `rankCodes: [NP]` | — | `{ days: 6 }` | 「原則上最多連六」。原文標 soft，案主釐清為**硬**：極端狀況必須支援時最多只能連 6 天 |

`H1` 在**求解器**裡建成極高權重的軟項而非硬約束（登記過多時回「空缺最少」的變體，
而不是 INFEASIBLE）；在**驗證層**仍是硬違規。這是兩邊對同一份定義刻意不同的唯一解讀。

`H4`、`H7` **跨月**：讀上個月最後幾天的值班為固定輸入。

「每人最多登記 16 天」**不在這裡**——它是登記期的驗證規則，求解器看不到 16 這個數字（ADR-0001）。

---

## 軟約束（7 條）

| 代碼 | 名稱 | 原語 | scope | metric | params | 權重 | 來源 |
|---|---|---|---|---|---|---:|---|
| `S1_QUOTA_FAIRNESS` | 額度點數組內公平 | `Fairness` | `exemptRankCodes: [NP]` | `quota_point` | — | 100 | 「目標同種類的人(P_max - P)越接近越好(soft)」；月結轉為起始偏移 |
| `S2_AREA_CONSISTENCY` | 同區延續 | `Consistency` | `exemptRankCodes: [NP]` | — | — | 40 | 「盡量讓同一個人值同一個區域(soft)」 |
| `S3_R2R3_PREFER_ICU` | R2/R3 優先 ICU | `Preference` | `rankCodes: [R2, R3]`, `areaTypeCodes: [ICU]` | — | `{ direction: prefer }` | 50 | 「R2 R3優先ICU，除非一般病房缺人(soft)」 |
| `S4_R4R6_PREFER_CHIEF` | R4~R6 優先總值 | `Preference` | `rankCodes: [R4, R5, R6]`, `areaTypeCodes: [CHIEF]` | — | `{ direction: prefer }` | 50 | 「R4 R5 R6優先總值，除非ICU缺人(soft)」 |
| `S5_NP_LAST_RESORT` | NP 盡量不用 | `Preference` | `rankCodes: [NP]` | — | `{ direction: avoid }` | 60 | 「NP是額外人力，只有在一般病房缺人才會支援」 |
| `S6_NP_AVOID_HOLIDAY` | NP 避開假日 | `Preference` | `rankCodes: [NP]`, `dayKinds: [holiday]` | — | `{ direction: avoid }` | 30 | 「原則上一到五上班六日休假可以調整」 |
| `S7_FAIRNESS_POINT` | 公平性點數組內公平 | `Fairness` | `exemptRankCodes: [NP]` | `fairness_point` | — | **0** | 「公平性點數(實驗性規則)」。預設停用 |

「除非缺人」不需要另外表達——軟約束本來就會在硬約束（覆蓋、額度、資格）擠壓下讓步。

`S3` / `S4` 的權重 50 是一般值。登記少時兩條通常都能滿足；低年級的不可排班日一多，
資深就被拉進 ICU 擠掉 R2/R3，`S3` 隨之被犧牲——這是登記量的函數，不是結構定局，
所以不特別壓低。可行性預警的 `bySupply` 會提前顯示這個壓力。

---

## 度量定義

| metric | 每格的值 | Fairness 比什麼 |
|---|---|---|
| `quota_point` | 平日 1、假日 2（`PointRules.quota`） | 組內**剩餘額度** `cap − 已排 − 月結轉偏移` 的 `max − min` |
| `fairness_point` | 依 `Rank.pointType` 查 `PointRules.fairness.tables`，加連值週六 bonus | 組內累計值的 `max − min` |
| `duty_day` | 每格 1 | （只用於 `Budget`） |

`Consistency` 的分數：每人 `值班數 − 最常值的那一區的值班數`，加總。

---

## 變體的權重乘數（ADR-0003）

實際權重 = 使用者設定的權重 × 乘數。使用者設 0 的約束乘任何數仍為 0。

| 變體 | 立場 | 乘數 |
|---|---|---|
| `v-a` | 重視公平 | `S1: 1.5`, `S2: 0.5` |
| `v-b` | 重視延續性 | `S1: 0.5`, `S2: 1.5` |
| `v-c` | 平衡 | 全部 1 |

多樣性約束：第 N 份與前面每一份至少有 **15 格**（約 10%）不同。這個數字是實作細節，不進契約。
