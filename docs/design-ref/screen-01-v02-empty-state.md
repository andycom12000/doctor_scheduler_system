# SCREEN 01 · V02 月份空狀態 — 實作交付說明

（2026-09-16 自 Claude Design 專案 `03_待交付_ClaudeCode/SCREEN-01_V02_月份空狀態.md` 原文複製；
設計稿本體是 `02_設計中/SCREEN-01_排班主表/SCREEN-01_排班主表.dc.html` 的 `#t8` 區塊。
案主拍板：V02 固定不捲動，V01 其他狀態維持可捲；年月工具列的標籤格式這次一起改。）

設計稿：`02_設計中/SCREEN-01_排班主表/SCREEN-01_排班主表.dc.html` → 區塊 `#t8`（標籤「V02 · 月份空狀態」）
相關規格：`API_SPEC.md`（SCREEN 01 端點表）、`CONTEXT.md`、`DESIGN_REVISIONS.md`

---

## 1. 這個畫面是什麼

`GET /api/schedules/{ym}` 回 **404**（該月尚無值班表）時，排班主表要呈現的狀態。

不是空白頁，也不是置中的空狀態卡片。畫面把**該月真正的矩陣骨架先畫出來**（33 人 × 當月日數，全部無內容），訊息面板疊在骨架中央。使用者一眼看到的是「表格已經在這裡，只是還沒排」，求解完成後同一組格線就會被填滿，版面不跳動。

---

## 2. 資料來源

| 用途 | 端點 | 空狀態下的處理 |
|---|---|---|
| 判斷是否為空狀態 | `GET /api/schedules/{ym}` | 404 → 進入本畫面。**404 不可當錯誤吐 toast** |
| 有班表的月份清單（年月切換器） | `GET /api/schedules` | 正常呼叫，用於 ‹ › 與月份下拉 |
| 表頭星期、假日認定 | `GET /api/calendars/{year}` | **必須呼叫**，決定日數、星期字、哪幾列是假日底色 |
| 人員欄（33 人、身分、身分組） | `GET /api/staff` | **必須呼叫**，決定欄數與身分組色帶分段 |
| 「開始求解」 | `POST /api/solver-jobs` | 見 §6 |

空狀態只需要 staff ＋ calendar 兩支即可完整繪製。**不要**打 `point-board`、`violations`、`vacancies`、`candidates`——該月沒有班表，這些都會是 404 或空集合。

---

## 3. 版面骨架（由上而下）

1. **nav**（`.nav` / `.nav-brand`）— 與其他狀態完全相同，不因空狀態改變。
2. **年月工具列** — `‹` ／ 月份標籤（可點開下拉）／ `›`，右側一個 `.tag.tag-outline`「尚無班表」。
   - 空狀態下工具列**不顯示**匯出 Excel／驗證約束／重新求解／重新發布這四顆按鈕（它們需要既有班表）。
3. **矩陣區** — `flex:1`，四邊 `padding:16px`，內容水平垂直居中。
4. 矩陣本身 `width:100%; height:100%`，欄與列都用 `flex:1` 平分（見 §4）。

整頁**不得出現捲軸**（WPF ＋ WebView2 固定視窗）。設計稿上的 16:9 1600×900 ／ 4:3 1280×960 切換只是檢查用的裝置，實作時要讓任何視窗尺寸都能塞滿。

---

## 4. 矩陣格線規則（最關鍵的部分）

軸向：**欄 ＝ 人員（33 欄）、列 ＝ 日期（當月日數，30 或 31）**。排版沿用 V01 已設計的「日 × 人」表，兩者可共用同一個表格元件。

### 固定尺寸（px，其餘全部彈性）

| 元素 | 尺寸 |
|---|---|
| 左側日期欄寬 | `44` |
| 頂部身分組色帶高 | `18` |
| 姓名欄頭高（直書） | `76` |
| 身分列高 | `24` |
| 表頭總高 | `18 + 76 + 24 = 118` |

### 彈性規則

- 每一個人員欄：`flex:1; min-width:0`。每一個日期列：`flex:1; min-height:0`。
- **不要用 `Math.floor(可用寬 / 欄數)` 算格子寬高**。除不盡的零頭若集中到最後一欄／最後一列，右下角就會多出一條白邊。交給 flex 平分，瀏覽器會把餘數吸收掉，四邊留白才等於容器那 16px。
- 結果：16:9 下格子約 47 × 22px，4:3 下約 37 × 24px，皆一頁塞完。

### 線與底色（Industry 設計系統的 token，`dim(n)` ＝ `color-mix(in srgb, var(--color-text) n%, transparent)`）

- 一般格：`border-right: 1px solid dim(7)`、`border-bottom: 1px solid dim(7)`。
- 日期欄格：右框 `dim(14)`、下框 `dim(7)`。
- 姓名欄頭：右框／下框 `dim(8)`；身分列下框 `dim(14)`。
- **身分組交界欄加重左框 `1px solid dim(22)`** — 落在每個身分組的第一個人（現行名冊為第 1、12、19、32 欄）。分段要由 staff 的身分組欄位動態算，不可寫死索引。
- **假日整列**底色 `dim(9)`（日期欄與所有格子同色），星期字改 `var(--color-accent-700)`；平日星期字 `dim(45)`。

### 表頭三列

- **身分組色帶**：`G1 · 11 人`、`G2 · 7 人`、`G3 · 13 人`、`G4 · 2 人`，寬度按組內人數 `flex:<人數>` 分配，底色依序為 accent 12% / 30% / 55% / `dim(12)`，字色分別 `--color-accent-800` / `-900` / `--color-bg` / `dim(70)`。人數與分組**一律由 `/api/staff` 算**。
- **姓名列**：直書（`writing-mode: vertical-rl; text-orientation: upright`），Noto Sans TC 500 11px，`letter-spacing:.06em`。
- **身分列**：PGY1／R2／NP… Barlow Condensed 600 9px，`dim(58)`，左上角格子標「身分」。

### 日期欄

每列「日數字 ＋ 星期小字」：Barlow Condensed 600 11px ＋ Noto Sans TC 400 8.5px，`gap:4px`、`padding-left:5px`。

---

## 5. 遮罩 ＋ 訊息面板

### 5.1 霧化遮罩

矩陣區上方盖一層覆蓋層（`position:absolute; inset:0; pointer-events:none`，與矩陣容器同一個定位父元素）：

```css
backdrop-filter: blur(2.5px);
background: color-mix(in srgb, var(--color-bg) 58%, transparent);
```

- 覆蓋範圍含表頭（身分組色帶、姓名、身分列）與所有格子，**不含 nav 與年月工具列**——這兩列保持清晰可操作。
- `pointer-events:none`，遮罩不接滑鼠事件。
- 若執行環境不支援 `backdrop-filter`（WebView2 舊版），退回純色遮罩 `color-mix(in srgb, var(--color-bg) 72%, transparent)`，不要改用截圖模糊。

### 5.2 面板

`.blueprint` 框（方角、1px `var(--color-accent)` 邊、四角 `+` 記號、底色 `var(--color-bg)` 不透明），**置中於矩陣且四邊貼齊格線交點**。

定位用格數換算，不要用固定 px：

```
colSpan  = round(nCols * 0.36)          // 33 欄 → 12 欄寬
colStart = round(nCols * (1-0.36) / 2)  // 33 欄 → 11
rowSpan  = round(nRows * 0.34)          // 30 列 → 10 列高
rowStart = round(nRows * (1-0.34) / 2)  // 30 列 → 10

left   = 44px + (矩陣寬 - 44px) * colStart / nCols
top    = 118px + (矩陣高 - 118px) * rowStart / nRows
width  = (矩陣寬 - 44px) * colSpan / nCols
height = (矩陣高 - 118px) * rowSpan / nRows
```

內部**垂直堆疊、水平垂直置中**（`flex-direction:column; align-items:center; justify-content:center; gap:11px; text-align:center`），三層：

1. kicker `.k`「SCHEDULE NOT GENERATED」
2. 標題 Barlow Condensed 600 34px「{YYYY} 年 {M} 月尚未產生班表」，月份隨年月切換器即時更新
3. `.btn.btn-primary.blueprint`「開始求解」，`margin-top:4px`、`padding:10px 22px`、字 Barlow Condensed 600 13.5px、`letter-spacing:.05em`

面板內**不放人數／日數／待排指派這類統計數字**，也不加次要按鈕——只有一句話和一個動作。

---

## 6. 互動行為

- **切換年月**：切到有班表的月份 → 正常表格；切到沒有的 → 本畫面。**同一個表格元件、同一組格線**，只是資料為空 ＋ 疊面板，不要做成兩個獨立頁面。
- **開始求解**：`POST /api/solver-jobs` 後導向 SCREEN 04（變體比較）的求解進度覆蓋層。求解中此畫面若仍可見，面板應替換為進度狀態，格線保持不動。
- **格子不可點**：空狀態下矩陣純為骨架，不接受 hover 高亮、不接受指派。游標維持 default（遮罩已是 `pointer-events:none`，別再在格子上綁事件）。
- **鍵盤焦點**：「開始求解」是本畫面唯一的焦點目標，焦點環用設計系統的 `:focus-visible`（2px accent，offset 2px）。

---

## 7. 邊界情況

- **28／29／30／31 日**：列數由行事曆決定，flex 自動重算，面板格數換算同式子，不需另外處理。
- **名冊變動**（休假、離職、7 月輪替）：欄數與身分組分段全部跟著 `/api/staff`，色帶寬度隨之改變。欄數變多時格子會變窄，**不加橫向捲軸**；若欄寬小於 12px 再議（現行 33 人不會觸發）。
- **視窗縮到很小**：格線可以密到看不清，但仍不得出現捲軸；面板最小寬度以標題不換行為準。
- **calendar 或 staff 取不到**：這兩支失敗才是真的錯誤狀態，顯示錯誤而非空狀態面板。

---

## 8. 驗收清單

- [ ] 404 不產生錯誤提示，直接進入空狀態。
- [ ] 任何視窗尺寸下都無捲軸，矩陣四邊留白目視等寬（16px）。
- [ ] 右下角沒有比其他三邊更寬的白邊（＝沒有用 floor 算格子）。
- [ ] 身分組色帶的人數、分段位置、加重欄線都與 `/api/staff` 一致。
- [ ] 假日列底色與星期字色正確，含國定假日（來自行事曆覆寫，不只週六日）。
- [ ] 面板四邊落在格線交點上、且水平垂直置中，切換月份（30↔31 日）後仍然對齊。
- [ ] 遮罩模糊只蓋到矩陣，nav 與年月工具列保持清晰且可點。
- [ ] 求解完成後回到同一畫面，格線位置與空狀態時完全相同。
- [ ] 空狀態下未呼叫 point-board／violations／vacancies／candidates。

---

## 9. 不在這次範圍

- 求解進度與變體比較（SCREEN 04）。
- 沿用上月班表：**已從設計中移除**，不實作。
- 空狀態下的側欄摘要——V01 的右側側欄在此狀態**不顯示**。
