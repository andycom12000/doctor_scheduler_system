# 發佈流程

版本號用 SemVer，tag 格式 `v1.0.0`，**tag 只打在 `main`**。分支模型見 `CLAUDE.md`。

## 版本號只存一個地方

`Directory.Build.props` 的 `<Version>`。其餘都從它來，不要在別處再寫一份：

| 去處 | 怎麼來 |
|---|---|
| 所有 .NET 組件、`HospitalScheduler.exe` 的檔案版本與產品版本 | MSBuild 內建（`Version` → AssemblyVersion／FileVersion／InformationalVersion；已關掉附加 commit hash） |
| 發佈資料夾與 zip 名稱 `DoctorScheduler-v<版本>` | `build/publish.ps1` 用 regex 讀同一個 `<Version>` |
| 畫面頂部導覽右側的 `v1.0.0` | `frontend/vite.config.ts` 建置時讀同一個 `<Version>`，以 `define` 注入 `__APP_VERSION__`；`dev`／`dev:mock` 自動加 `-dev` 後綴 |

沒有動 `api-contract.yaml`：版本由建置注入前端，不經過 API。

## 步驟

前置：`build/webview2-runtime/` 已備妥（`pwsh build/fetch-webview2.ps1`，一次性）。

1. **開分支**：`git switch develop && git pull && git switch -c release/v1.0.0`
2. **改版本號**：只改 `Directory.Build.props` 的 `<Version>`，commit。
3. **確認使用者說明在位**：`docs/user-guide/`（#80）要已進版控。`publish.ps1` 會把它整份複製成
   發佈包的 `使用者說明/`，在壓 zip 之前；資料夾不存在只會印警告，**交付版本不可有這個警告**。
   不要手動往 `publish/` 放檔案：腳本開頭會刪掉整個輸出資料夾。
   **名冊檔（#82）**：交付版本要內建真實名冊時，備好 repo 外的名冊 CSV（UTF-8、表頭 `員編,姓名,身分`、
   身分用代碼），路徑由發佈者在步驟 4 以 `-RosterFile` 指定。**名冊檔不進版控**（含真實姓名與員編），
   不要放進 repo 目錄。腳本在最前面先驗證，不通過就失敗並指出列號；通過才複製成包內 `roster/roster.csv`
   （在壓 zip 之前，所以 zip 內含名冊且納入檔案數檢查）。沒給 `-RosterFile` 會印警告，
   **交付版本不可有這個警告**。zip 含真實名冊，交付與保管依個資規範處理。
4. **打包**：`pwsh build/publish.ps1 -Zip -RosterFile <repo 外的名冊.csv>`
   （沒有 pwsh 7 時用 `powershell -File build/publish.ps1 -Zip -RosterFile ...`）。
   產出 `publish/DoctorScheduler-v1.0.0/` 與同名 zip。腳本會自動檢查：`check-native-deps.ps1`、
   exe 的 ProductVersion 等於 `<Version>`、zip 與資料夾檔案數一致；任何一項不過就失敗。
   `-SkipFrontend` 時還會確認 wwwroot 的 JS 含目前版本字串。
5. **實機驗收**：在乾淨 Windows 上依 #18 清單與 `docs/ARCHITECTURE.md` §10 跑一遍，
   並確認畫面右上角版本號與預期一致。發現問題在 release 分支上修，修完回到步驟 4。
6. **合併**：開兩個 PR，`release/v1.0.0` → `main` 與 `release/v1.0.0` → `develop`
   （不要用 `git flow release finish`，同 feature 分支的理由：要走 PR）。
   兩個 PR 都選「Create a merge commit」，不要 squash 也不要 rebase：tag 打在 `main` 的合併 commit 上，
   且 release 分支的歷史要原樣進 `develop`。
7. **打 tag**：`main` 合併後，在 `main` 上 `git tag v1.0.0 && git push origin v1.0.0`。
8. **交付**：把 zip 交給使用者。zip 的內容應與打 tag 的那個 commit 建出來的一致；
   若 release 分支在驗收後又有改動，要重打包。

## hotfix

從 `main` 開 `hotfix/v1.0.1`，改版本號、打包、驗收，PR 合進 `main` 與 `develop`，在 `main` 打 tag。

## 不在流程內

程式碼簽章、自動更新、版本檢查。
