# 設計稿參考（給實作用的離線副本）

從 Claude Design 專案「醫院護理排班系統UI設計」（`84f6e82e-865b-48a3-89a5-184f1e4a4f4e`）的
`排班系統 API 註解.dc.html` TURN 3／TURN 4 切出，一頁一檔。實作 agent 沒有 DesignSync，
只能讀這裡。

**權威界定與 `docs/design-revisions.md` 相同**：版面、互動、資訊架構以這裡為準；
欄位、數字、術語以 `api-contract.yaml`、`CONTEXT.md`、`docs/constraint-defaults.md` 為準。

| 檔案 | 內容 |
|---|---|
| `screen-01.html` | 排班主表：三個檢視分頁、年月切換、工具列、點數看板、身分組容量利用率、違規側欄、圖例 |
| `screen-02.html` | 區域與點數：區域類型／區域、10 身分表（R6 本月覆寫）、額度點數、公平性點數兩張查表、連值週六加分 |
| `screen-03.html` | 資格與約束：10 × 3 資格矩陣、7 硬約束、7 軟約束 |
| `screen-04.html` | 變體比較：短編號、規模、警告列、變體卡（立場、乘數、指標、熱圖）、逐格差異、套用 |
| `screen-04b.html` | 求解進度覆蓋層：第 n／N 份、耗時、候選解、gap（數字）、三份狀態、中止 |
| `screen-05.html` | 不可排班日登記：筆刷矩陣、底部兩列、登記概況、可行性預警兩層、帶入求解 |
| `screen-06.html` | 人員維護：左清單、右表單、兩態狀態 |
| `canvas-data.js` | canvas 的資料綁定與示範資料（`{{ }}` 與 `<sc-for>` 的來源），看示範值用 |
| `industry.css` | 掛載的 Industry design system 的 token 與元件類別，`@import` 字型已拿掉 |

## 怎麼讀

- 每個檔案的 `<style>` 是 canvas 共用的樣式，直接用瀏覽器開可以看到骨架（資料綁定不會渲染，
  `{{ x }}` 會原樣顯示；對照 `canvas-data.js` 裡的 `x` 看示範值）。
- 畫面左右兩側的 `.an` 是 API 拉線註解：左＝讀取來源、右＝動作。那些註解已經對齊定案契約。
- `industry.css` 只拿 `:root` 變數搬進 `frontend/src/styles.css`，元件類別在 SFC 裡用 scoped CSS 重寫，
  不要整份 import。

## 更新

Claude Design 那邊的 canvas 改了就重切：用 DesignSync `get_file` 抓 canvas，
依 `<div class="anw">` 切塊（順序 06、01、02、03、04、04b、05），inline `<script>` 抽成 `canvas-data.js`。
