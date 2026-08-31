# 前端工作區

Vue 3 + TypeScript + Vite。這是你的地盤——`frontend/` 底下的東西你可以隨意改。

**你不需要安裝 .NET，也不需要安裝 WebView2。** 只要 Node.js ≥ 20.19。

---

## 60 秒上手

```bash
npm install
npm run dev:mock      # 走 MSW mock，完全不需要後端
```

開 http://localhost:5173，看到「後端 /api/health: ok (mock)」就對了。

要接真的後端（需要對方先跑起來，或你自己裝 .NET SDK 8）：

```bash
npm run dev           # /api 會 proxy 到 http://localhost:5080
```

`src/App.vue` 目前只是環境檢查畫面，開始實作時整個換掉就好。

## 指令

| 指令 | 用途 |
|---|---|
| `npm run dev` | dev server，`/api` proxy 到 `:5080` 的真後端 |
| `npm run dev:mock` | dev server，`/api` 由 MSW 攔截（不需後端） |
| `npm run build` | 型別檢查 + 建置到 `../src/Scheduler.Shell/wwwroot` |
| `npm run typecheck` | 只跑型別檢查 |
| `npm run api:types` | 由 `../api-contract.yaml` 生成 `src/api/schema.d.ts` |

## 分支

**從 `develop` 開分支，PR 合回 `develop`**（git-flow，`develop` 是預設分支）。
不要直接 commit 到 `develop` 或 `main`。

```bash
git switch develop && git pull
git switch -c feature/<slug>
git push -u origin feature/<slug>
gh pr create        # base 自動是 develop
```

git-flow CLI 是選配，用純 git 指令完全等價。若你要用 CLI，
clone 後要跑一次 `.git/config` 的設定（不隨 repo 進版控），
指令在 [`../CLAUDE.md` 的「分支模型」一節](../CLAUDE.md)。
**不要用 `git flow feature finish`**，它會繞過 PR 直接在本機合併。

## 這個專案跟一般網頁專案不一樣的地方

**這是要塞進 Windows 桌面程式裡的 SPA。** 正式版由一個 WPF 殼用 WebView2 載入，
沒有分頁、沒有網址列、沒有多裝置。設計版面時請以「填滿視窗、內部捲動」為前提，
不要用置中固定寬度的容器。

**核心 UI 是密集的排班格線**，需要色彩編碼、拖拉、以及 A4 列印。
選擇 WebView2 而非原生 Windows 控制項的唯一理由就是這個——CSS Grid 做這件事成本低太多。
列印是驗收項目，不是加分項。

**瀏覽器版本是鎖死的。** `vite.config.ts` 的 `build.target` 對齊隨附的 WebView2
Fixed Version runtime。好處是不需要 polyfill 和 legacy transpile；**代價是你在最新版
Chrome 測過不算數**，交付前必須在真正的 fixed-version runtime 裡驗過一次。
（那一步需要 Windows 打包環境，跟後端的人約時間跑。）

**不要引入 meta-framework。** Next.js / Nuxt / SvelteKit 的 SSR、server actions
在這裡全部用不到，只會帶來一堆需要關掉的預設行為。這是已經評估過並排除的選項，
理由見 [`../docs/ARCHITECTURE.md` §2](../docs/ARCHITECTURE.md#2-已排除方案勿重新提案)。

## 三個不要亂動的檔案

### `src/api/client.ts` — 所有後端呼叫的唯一入口

一律用相對路徑 `fetch('/api/...')`。這個寫法在三種情境下逐字相同：

| 情境 | `/api/...` 怎麼被處理 |
|---|---|
| 你現在（dev） | Vite dev server proxy 到 `:5080`，或 MSW 攔截 |
| 正式版（WebView2） | WPF 殼的 `WebResourceRequested` 攔截，不開 port |
| 未來雲端版 | 同源部署，正常 HTTP |

**不要寫絕對網址、不要自己組 base URL。** 一旦寫死，遷移就得整份重寫，
而「遷移路徑每天都在被使用」正是這個架構的核心價值。

### `src/realtime.ts` — 全專案唯一有平台分支的檔案

求解可能跑很久，進度推送在兩種環境走不同管道：

- 正式版：WebView2 host message（C# 端 `PostWebMessageAsJson`）
- 開發期 / 雲端版：SSE

差異已經封裝在 `subscribe()` 裡。**如果之後又出現平台差異，請塞進這個檔案，不要散出去。**

### `vite.config.ts` — 建置目標與輸出路徑

`build.target` 對齊隨附的 WebView2 版本，`build.outDir` 指向 WPF 殼的 `wwwroot`。
兩者都不是隨便設的，改之前先問。

## API 契約

`../api-contract.yaml` 是你跟後端之間的**唯一**耦合點。

要新端點、要改欄位形狀，**先改契約、談定，再各自實作**。改完之後：

```bash
npm run api:types                  # 重新生成 src/api/schema.d.ts
```

然後同步補上 `src/mocks/handlers.ts` 的對應 handler，你就能繼續往下做，
不必等後端。

> **現況**：契約目前只有 `/api/health`。領域端點（`/api/jobs` 系列）的形狀
> 已在契約檔裡以註解列出草稿，但 schema 尚未定案——那是下一階段的事。
> 在那之前，UI 的骨架、版面、互動都可以先做。

## 目錄

```
src/
├─ main.ts            進入點（啟動時視環境掛上 MSW）
├─ App.vue            環境檢查畫面 —— 實作時整個替換掉
├─ realtime.ts        ★ 平台分支封裝
├─ styles.css         全域樣式
├─ api/
│  ├─ client.ts       ★ 後端呼叫唯一入口
│  ├─ health.ts       端點包裝的範例寫法
│  └─ schema.d.ts     由契約生成（不進版控）
├─ mocks/
│  ├─ index.ts        VITE_USE_MOCK=true 時啟用
│  ├─ browser.ts
│  └─ handlers.ts     ← 契約新增端點時同步補這裡
└─ webview2.d.ts      WebView2 宿主物件型別（只給 realtime.ts 用）
```

## 拖拉互動的提醒

架構文件裡有一句話值得轉述：格線拖拉場景中 React 的 dnd-kit 成熟度領先。
這個 repo 選了 Vue，Vue 生態的對應選項（vuedraggable / vue-draggable-plus / 自刻 Pointer Events）
請你自己評估——如果評估後覺得拖拉體驗撐不住，**這是唯一值得回頭討論換框架的理由**，
現在換的成本還很低（`frontend/` 以外完全不受影響，`vite.config.ts`、`realtime.ts`、
`client.ts` 三個檔案的內容與框架無關，可直接沿用）。

## 延伸閱讀

- [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) — 技術決策全貌
  - §2 已排除方案（提案前先看這裡）
  - §4 WebView2 橋接規格（想知道 `/api` 攔截怎麼運作時看）
  - §8 驗收清單（其中「SPA 子路徑重新整理不會 404」和「A4 列印正常」跟你直接相關）
