# Claude Design 同步包

送進 Claude Design 專案「醫院護理排班系統UI設計」的定案脈絡，
讓在那邊做重新設計時，Claude 讀到的是定案內容而不是雛型階段的假設。

- **專案 ID**：`84f6e82e-865b-48a3-89a5-184f1e4a4f4e`
- **專案型態**：`PROJECT_TYPE_PROJECT`（一般 Claude Design 專案，**不是** design-system）
- **設計稿**：專案內的 `排班系統 API 註解.dc.html`（canvas，含 TURN 2/3/4）
- **掛載的 design system**：`Industry`（`ab64fa60-5bee-44e0-9c30-f087bf5dd6c1`）

## 送上去的四份檔案

| 專案內路徑 | 來源 | 性質 |
|---|---|---|
| `API_SPEC.md` | `docs/design-sync/API_SPEC.md` | **手寫**。給設計看的契約摘要，非機械衍生 |
| `CONTEXT.md` | 根目錄 `CONTEXT.md` | 原樣複製 |
| `CONSTRAINT_DEFAULTS.md` | `docs/constraint-defaults.md` | 原樣複製 |
| `DESIGN_REVISIONS.md` | `docs/design-revisions.md` | 原樣複製 |

`API_SPEC.md` 取代了專案裡原本那份「護理排班系統 — API 規格」——
舊版是 30 支 API、`/api/v1`、Bearer 認證、角色、`If-Match`、`unitId`、N1–N4、
13 區 51 人、必休／希望休兩層，**與定案的系統完全不符**，留著會把設計帶偏。

## 什麼時候要重送

- `api-contract.yaml` 的端點或形狀變動 → 手動更新 `API_SPEC.md` 再送
- `CONTEXT.md` / `docs/constraint-defaults.md` / `docs/design-revisions.md` 變動 → 重送該檔

`API_SPEC.md` **不會**自動跟著契約走，改契約時要記得回來改它。

## 怎麼送

用 `DesignSync` 工具，順序固定為 `list_files` → `finalize_plan` → `write_files`：

1. `finalize_plan`：`projectId` 如上，`localDir` 指向放齊四份檔案的目錄，
   `writes` 列出四個路徑，`deletes` 給 `[]`（此參數必填，不可省略）
2. `write_files`：帶上一步回傳的 `planId`，每個檔案用 `localPath` 讓工具直接讀檔上傳

**不要動 `排班系統 API 註解.dc.html`。** 那份 canvas 是 data-driven 的
（`<sc-for>` + `{{ props }}`，並以 `props="{...editor:...}"` 宣告屬性面板），
在 Claude Design 的 canvas editor 裡互動修改，比從這裡盲改安全得多。

## 已知的引用缺口

三份文件都引用案主的原始需求檔 `constraints.md`，但**它不在版控裡**。
其內容已被吸收進 `docs/constraint-defaults.md` 的「來源」欄，
所以缺檔不影響同步包的可用性，但外部讀者會看到一個指不到的引用。
