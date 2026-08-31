# 醫院排班系統 — 技術架構

> 本文件**僅涵蓋技術選型與專案骨架**。領域模型、求解建模、API 契約、UI 規格等專案內容細節由下一階段規劃,本文件刻意不涉及。
>
> 接手者請**先讀完第 2 節「已排除方案」**,避免重新提出已被否決的選項。

---

## 1. 技術決策(已定案)

| 項目 | 決定 |
|---|---|
| Runtime | .NET 8/9,self-contained |
| 殼 | **WPF** |
| UI 渲染 | **WebView2**,Fixed Version 隨附 |
| 前端 | **Vite + SPA**(React / Vue / Angular,由前端開發者選) |
| 求解器 | **Google.OrTools CP-SAT**,in-process |
| 前後端橋接 | **`WebResourceRequested` 攔截**,不開 port |
| 資料儲存 | 程式資料夾旁的 JSON 檔 |
| 發佈形態 | **資料夾式 portable**(解壓即用) |

### 1.1 決策理由

**為何 .NET**:OR-Tools 官方 binding 僅 C++ / Python / Java / .NET。選 .NET 才能在同一 process 內取得全速多執行緒 CP-SAT,無 sidecar、無 port。求解器的語言限制反過來決定了整個 stack,不是 UI 偏好決定的。

**為何 WPF 而非 WinForms**:殼裡幾乎全是 WebView,只需約 50 行 WPF。選 WPF 的唯一理由是 Per-Monitor V2 DPI 行為較佳——目標環境螢幕組合混亂(4K 筆電接 1080p 外接螢幕常見)。

**為何 WebView2 而非原生 XAML UI**:核心 UI 是密集格線,需色彩編碼、拖拉、列印。這在 CSS Grid 成本極低,在 WPF DataGrid 是一場硬仗(虛擬化、拖放、動態欄位、列印分頁全需自理)。同時前端要交給專職 JS 開發者,需標準前端生態。

**為何 Fixed Version WebView2**:portable 的必要條件。Evergreen 模式要求目標機器已安裝 runtime,不符合免安裝前提。代價約 180MB 與自行負責更新。

**為何 `WebResourceRequested` 而非 `AddHostObjectToScript`**:host objects 是 COM proxy 語意,前端得寫 Windows 專屬程式碼,遷移時整份重寫。攔截法讓前端寫標準 `fetch('/api/...')`,**與未來雲端版逐字相同**。

**為何資料夾式而非單一 exe**:`PublishSingleFile` 實為自解壓到 `%TEMP%` 再執行,是 AppLocker / EDR 的頭號攔截特徵,且冷啟動變慢。「免安裝」要的是 xcopy 部署,不是單一檔案。

---

## 2. 已排除方案(勿重新提案)

| 方案 | 排除理由 |
|---|---|
| Blazor(WASM / Server / Hybrid) | 前端需交給 JS 開發者,要標準 JS 框架生態 |
| Tauri / Electron | Rust 與 Node **均無 OR-Tools 官方 binding**,必須 sidecar |
| MAUI / WinUI 3 | 以 MSIX 封裝為主,是「安裝」不是「免安裝」 |
| Photino.NET | Windows 底層仍是 WebView2,依賴一分沒省;`RegisterCustomSchemeHandler` 為同步簽章,長時間求解難處理;fixed-version 隨附支援待驗 |
| Avalonia / 原生 XAML UI | 格線 UI 自行實作成本過高,且無法讓 JS 開發者獨立作業 |
| 純瀏覽器 WASM 求解 | 靜態託管無法設 COOP/COEP → 無 `SharedArrayBuffer` → 單執行緒;wasm32 4GB 記憶體上限 |
| `PublishSingleFile` 單一 exe | 見上,自解壓觸發資安攔截 |
| Timefold Solver | JVM 生態(Python 版亦靠 JPype),portable 情境下體積、process、技術棧全部翻倍 |
| 自寫 metaheuristic | 放棄最佳性保證與「證明無解」的能力 |
| Next.js / Nuxt / SvelteKit 等 meta-framework | SSR / server actions 在此情境全用不到,只會帶來需要關閉的預設行為 |

---

## 3. 專案骨架

本節僅定義 level 1–2 的分層與職責,內部結構待下一階段規劃。

### 3.1 原始碼

```
HospitalScheduler/
├─ HospitalScheduler.sln
├─ src/
│  ├─ Scheduler.Domain/          # 純領域模型,無外部相依
│  ├─ Scheduler.Solver/          # 求解引擎實作(OR-Tools 相依隔離於此)
│  ├─ Scheduler.Application/     # 用例層,transport 無關
│  ├─ Scheduler.Api/             # 開發期用:Minimal API 主機
│  └─ Scheduler.Shell/           # 正式版:WPF + WebView2 殼
├─ frontend/                     # 前端開發者的專屬工作區
├─ tests/
├─ build/                        # 打包腳本、WebView2 fixed runtime
└─ api-contract.yaml             # OpenAPI,前後端唯一耦合點
```

**分層的兩條硬性規則**:

1. `Scheduler.Domain` 與 `Scheduler.Application` **不得引用任何 OR-Tools 型別**。求解器相依只存在於 `Scheduler.Solver`,由介面隔離,保留日後替換引擎的可能。
2. `Scheduler.Api` 與 `Scheduler.Shell` 是**兩個 transport,共用同一組 handler**,不得各自實作業務邏輯。這是「無痛遷移到前後端分離」的技術基礎——遷移時刪掉 `Scheduler.Shell` 即可。

> 規則 1 由 `tests/Scheduler.ArchitectureTests` 自動驗證(csproj 層級的 `PackageReference` 與編譯後的組件相依各檢查一次)。違反時編譯仍會成功,只有測試會擋下來。

### 3.2 發佈產物

```
HospitalScheduler/               # 使用者解壓後的資料夾
├─ HospitalScheduler.exe
├─ *.dll                         # .NET self-contained runtime
├─ runtimes/                     # OR-Tools native libraries
├─ webview2/                     # Fixed Version runtime(~180MB)
├─ wwwroot/                      # 前端 build 產物
└─ data/                         # 所有狀態,含 WebView2 user data
```

**`data/` 全部寫在程式旁邊是 portable 的硬性要求。** 啟動時須檢查該資料夾可寫,不可寫則明確報錯。

由 `build/publish.ps1` 產出。

---

## 4. WebView2 橋接規格

### 4.1 環境初始化

```csharp
var env = await CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: Path.Combine(AppContext.BaseDirectory, "webview2"),
    userDataFolder:          Path.Combine(AppContext.BaseDirectory, "data", "wv2data"));
await webView.EnsureCoreWebView2Async(env);
```

`userDataFolder` **不可省略**。預設會寫入 `%LOCALAPPDATA%`,違反 portable 前提。

### 4.2 靜態資產與 API 攔截

```csharp
var core = webView.CoreWebView2;

core.SetVirtualHostNameToFolderMapping("app.local",
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    CoreWebView2HostResourceAccessKind.Allow);

core.AddWebResourceRequestedFilter("https://app.local/api/*", CoreWebView2WebResourceContext.All);
core.WebResourceRequested += async (s, e) =>
{
    var deferral = e.GetDeferral();            // 支援非同步,長時間運算必須
    var res = await _apiHandler.HandleAsync(Map(e.Request), CancellationToken.None);
    e.Response = core.Environment.CreateWebResourceResponse(
        res.Body, res.Status, res.Reason, res.Headers);
    deferral.Complete();
};

webView.Source = new Uri("https://app.local/index.html");
```

因為 SPA 由 `https://app.local/` 載入,前端寫相對路徑 `fetch('/api/...')` 會自然解析到被攔截的位址。

### 4.3 SPA deep link fallback

`SetVirtualHostNameToFolderMapping` 是檔案對應而非 web server,重新整理在子路徑會 404。需額外攔截 Document 請求:非 `/api/` 且無副檔名者一律回 `index.html`,等同雲端版的 `MapFallbackToFile`。

### 4.4 進度推送

長時間運算的進度回報,C# → JS 用 `core.PostWebMessageAsJson(...)`。前端側只在單一檔案做平台分支,見 `frontend/src/realtime.ts`——**全專案唯一有平台分支的檔案**。

```ts
export function subscribe(jobId: string, onEvent: (e: Progress) => void) {
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', ev => onEvent(ev.data));
  } else {
    new EventSource(`/api/jobs/${jobId}/stream`).onmessage = ev => onEvent(JSON.parse(ev.data));
  }
}
```

### 4.5 除錯

```csharp
core.Settings.AreDevToolsEnabled = true;   // Debug 建置開啟,Release 關閉
```

---

## 5. 開發流程與分工

```
開發期:  Vite dev server ──proxy /api──> dotnet run (Scheduler.Api)
正式版:  WPF + WebView2 攔截 ─────────> 同一組 handler
```

前端開發者**完全不需要安裝 .NET 或 WebView2**。搭配 mock server(MSW 或由 OpenAPI 生成),他連 backend 都不用跑就能開發。兩人的唯一耦合點是 `api-contract.yaml`。

此流程的關鍵好處:**遷移路徑每天都在被實際使用**,不會等到要遷移時才發現有問題。

實際設定見 `frontend/vite.config.ts`:`build.target` 對齊隨附的 WebView2 版本,`build.outDir` 指向 `src/Scheduler.Shell/wwwroot`,`server.proxy` 轉送 `/api` 到 `http://localhost:5080`。

鎖定 Chromium 版本是隨附 fixed-version 的免費好處——不需 polyfill 與 legacy transpile,bundle 更小。**代價是必須在真正的 fixed-version runtime 內測試**,不可只在最新版 Chrome 驗證。

框架由前端開發者選其最熟悉者。唯一例外:若他不熟拖拉互動,選 React(dnd-kit 在格線拖拉場景成熟度領先)。

> **本 repo 已選定 Vue 3 + TypeScript。** 若要換框架,需替換的只有 `frontend/`;`vite.config.ts`、`src/realtime.ts`、`src/api/client.ts` 三個檔案的內容與框架無關,可直接沿用。

---

## 6. 發佈設定

```xml
<PublishSingleFile>false</PublishSingleFile>   <!-- 資料夾式,不要自解壓 -->
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishTrimmed>false</PublishTrimmed>         <!-- OR-Tools 反射會被裁掉 -->
```

實際設定在 `Directory.Build.props`,由 `Scheduler.Shell` 的 `IsPublishable=true` 觸發。

- **不要開 trim,不要開 AOT** — OR-Tools 的 P/Invoke wrapper 兩者皆不保證相容
- WebView2 Fixed Version 需另行下載並複製到 `webview2/`,由 `build/fetch-webview2.ps1` 處理;版本釘選在 `build/webview2.json`

---

## 7. 已知風險與必驗項目

| 項目 | 說明 | 何時驗 |
|---|---|---|
| **VC++ Runtime** | OR-Tools native lib 可能依賴 `vcruntime140`。**必須在一台乾淨的 Windows 實測**,不可只在開發機驗證 | **最優先,第一週** |
| 唯讀路徑 | 程式可能被放在無寫入權限的位置,啟動時需檢查並明確報錯 | 早期 |
| SmartScreen | 未簽章 exe 會觸發「Windows 已保護您的電腦」。單一使用者可點過,但需事先告知案主。OV 憑證約 USD 200–400/年 | 交付前告知 |
| 更新機制 | portable 版無自動更新,第一版接受手動換資料夾 | 已接受 |

---

## 8. 架構驗收檢查清單

- [ ] 在乾淨的 Windows(無 .NET、無 WebView2、無 VC++ Redist)解壓即可執行
- [ ] `%APPDATA%` / `%LOCALAPPDATA%` / 登錄檔無任何寫入
- [ ] 複製整個資料夾到另一台機器,所有資料與設定完整保留
- [ ] 程式放在唯讀路徑時,啟動有明確錯誤訊息而非崩潰
- [ ] 長時間運算期間 UI 不凍結,進度即時更新,可中止
- [ ] SPA 子路徑重新整理不會 404
- [ ] 4K 螢幕與 1080p 外接螢幕間拖曳視窗,DPI 縮放正常
- [ ] 列印輸出正常(A4)
- [ ] 前端 build 產物可直接部署至靜態主機,搭配 `Scheduler.Api` 正常運作(遷移路徑驗證)
