#Requires -Version 5.1
<#
.SYNOPSIS
    產出資料夾式 portable 發佈包。

.DESCRIPTION
    步驟：
      1. 建置前端（輸出到 src/Scheduler.Shell/wwwroot）
      2. dotnet publish Scheduler.Shell（self-contained、win-x64、不 trim、不單一檔），
         並清掉 Api 的 apphost 殘留與前端開發用檔案（Scheduler.Api.dll 是殼引用的組件，不能刪）
      3. app-local 放入 VC++ runtime（msvcp140 / vcruntime140 / vcruntime140_1）
         —— OR-Tools 的 native 程式庫依賴它們，NuGet 套件不附帶，見 docs/ARCHITECTURE.md §9
      4. 複製 WebView2 Fixed Version runtime 到 webview2/
      5. 建立空的 data/
      6. 跑 build/check-native-deps.ps1 確認包內沒有懸空的 native 相依

    產出結構見 docs/ARCHITECTURE.md §3.2。使用者解壓即用，無需安裝任何東西。

.PARAMETER OutputPath
    發佈目錄，預設 publish/DoctorScheduler-v<版本>，版本取自 Directory.Build.props 的 <Version>。

.PARAMETER UserGuidePath
    使用者說明資料夾，預設 docs/user-guide/（#80）。存在就整份複製到發佈包的「使用者說明/」，
    不存在只印警告（交付版本不該缺，由發佈流程文件的步驟把關）。

.PARAMETER Zip
    發佈完成後再壓成與資料夾同名的 zip（publish/DoctorScheduler-v<版本>.zip）。

.PARAMETER SkipFrontend
    跳過前端建置（wwwroot 已是最新時可用）。

.PARAMETER VcRedistPath
    含 msvcp140.dll / vcruntime140.dll / vcruntime140_1.dll 的目錄。省略時自動在
    Visual Studio / Build Tools 的 VC\Redist\MSVC\<版本>\x64\Microsoft.VC143.CRT 尋找最新版。

.PARAMETER RosterFile
    名冊檔（UTF-8 CSV，可帶 BOM，表頭「員編,姓名,身分」，身分用代碼）的路徑（#82）。
    **名冊檔是真實姓名與員編，不進版控**，永遠由這個參數從 repo 外帶入。先驗證（表頭、員編不重複且非空、
    姓名非空、身分代碼認得；不通過就讓發佈失敗並指出列號），通過才複製到發佈包的 roster/roster.csv，
    第一次啟動時匯入、只匯一次。省略時照常發佈但印警告，第一次啟動名冊會是空的。

.EXAMPLE
    pwsh build/publish.ps1

.EXAMPLE
    pwsh build/publish.ps1 -RosterFile C:\private\roster.csv
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [string] $UserGuidePath,
    [switch] $Zip,
    [switch] $SkipFrontend,
    [string] $VcRedistPath,
    [string] $RosterFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent

# 版本號唯一來源：Directory.Build.props 的 <Version>（exe 檔案版本與前端畫面也從它來）
# 先去掉 XML 註解（props 的說明文字裡就有「<Version>」字樣），vite.config.ts 同樣處理
$propsText = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw -Encoding UTF8
$propsText = [regex]::Replace($propsText, '(?s)<!--.*?-->', '')
if ($propsText -notmatch '<Version>\s*([^<\s]+)\s*</Version>') {
    throw 'Directory.Build.props 找不到 <Version>'
}
$version = $Matches[1]
$packageName = "DoctorScheduler-v$version"
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "publish/$packageName"
}
Write-Host "版本 $version → $packageName" -ForegroundColor Cyan

# --- 0. 名冊檔驗證（#82）---
# 放在最前面：檔案有問題就不必等前端與 dotnet publish 跑完才知道。
# 規則與 Scheduler.Persistence 的 RosterImporter.Parse 一致（程式端啟動時會再驗一次）。
# 身分代碼清單與 DefaultRanks 相同，tests/Scheduler.Persistence.Tests/RosterParseTests 會守住不漂移。
$knownRankCodes = @('PGY1', 'PGY2', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'PTR', 'NP')
if ($RosterFile) {
    if (-not (Test-Path -LiteralPath $RosterFile -PathType Leaf)) { throw "找不到名冊檔：$RosterFile" }
    $rosterText = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $RosterFile).Path, (New-Object System.Text.UTF8Encoding($false, $true)))
    $rosterLines = $rosterText.TrimStart([char]0xFEFF) -split "`n"
    $rosterErrors = New-Object System.Collections.Generic.List[string]
    if (($rosterLines[0].Trim() -replace ' ', '') -ne '員編,姓名,身分') {
        $rosterErrors.Add('第 1 列：表頭必須是「員編,姓名,身分」')
    } else {
        $seenNos = @{}
        $rosterRows = 0
        for ($i = 1; $i -lt $rosterLines.Count; $i++) {
            $n = $i + 1
            $f = @(($rosterLines[$i].TrimEnd("`r") -split ',') | ForEach-Object { $_.Trim() })
            if (-not ($f | Where-Object { $_ -ne '' })) { continue }
            if ($f.Count -ne 3) { $rosterErrors.Add("第 $n 列：欄位數必須是 3（員編,姓名,身分），實際是 $($f.Count)"); continue }
            $rosterRows++
            if ($f[0] -eq '') { $rosterErrors.Add("第 $n 列：員編是空的") }
            elseif ($seenNos.ContainsKey($f[0])) { $rosterErrors.Add("第 $n 列：員編與前面的列重複") }
            else { $seenNos[$f[0]] = $true }
            if ($f[1] -eq '') { $rosterErrors.Add("第 $n 列：姓名是空的") }
            if ($knownRankCodes -cnotcontains $f[2]) { $rosterErrors.Add("第 $n 列：不認得的身分代碼「$($f[2])」（可用：$($knownRankCodes -join ', ')）") }
        }
        if ($rosterRows -eq 0 -and $rosterErrors.Count -eq 0) { $rosterErrors.Add('沒有任何人員列') }
    }
    if ($rosterErrors.Count -gt 0) {
        throw ("名冊檔驗證不過，發佈中止：`n  " + ($rosterErrors -join "`n  "))
    }
    Write-Host "[0/6] 名冊檔驗證通過（$rosterRows 人）" -ForegroundColor Cyan
} else {
    Write-Warning '發佈包不含名冊，第一次啟動名冊會是空的（要帶入請用 -RosterFile）'
}

# --- 1. 前端 ---
if ($SkipFrontend) {
    Write-Host '[1/6] 跳過前端建置' -ForegroundColor DarkGray
} else {
    Write-Host '[1/6] 建置前端 …' -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'frontend')
    try {
        if (-not (Test-Path 'node_modules')) { npm ci }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw '前端建置失敗' }
    } finally {
        Pop-Location
    }
}

$wwwroot = Join-Path $repoRoot 'src/Scheduler.Shell/wwwroot/index.html'
if (-not (Test-Path $wwwroot)) {
    throw "找不到 $wwwroot。前端建置產物缺失，殼會載入空白畫面。"
}

# 略過前端建置時，wwwroot 可能是舊版號的產物：畫面上的版本會和 exe 對不上，直接擋下
if ($SkipFrontend) {
    $jsFiles = Get-ChildItem (Join-Path $repoRoot 'src/Scheduler.Shell/wwwroot/assets') -Filter '*.js' -ErrorAction SilentlyContinue
    $needle = [regex]::Escape($version)
    if (-not ($jsFiles | Select-String -Pattern ('[`"'']' + $needle + '[`"'']') -List)) {
        throw "wwwroot 的 JS 不含目前版本字串 $version（前端是舊版號建的）。請去掉 -SkipFrontend 重建。"
    }
}

# --- 2. .NET ---
Write-Host '[2/6] dotnet publish …' -ForegroundColor Cyan
if (Test-Path $OutputPath) { Remove-Item $OutputPath -Recurse -Force }

# --configuration Release 不只是最佳化：Scheduler.Shell 的參考名單種子旗標（#37）是
# #if DEBUG / #else 編譯期常數，Release 建置才會關掉，發佈包才不會帶 34 人的假名單。
dotnet publish (Join-Path $repoRoot 'src/Scheduler.Shell/Scheduler.Shell.csproj') `
    --configuration Release `
    --output $OutputPath `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失敗' }

# 殼關掉了 ValidateExecutableReferencesMatchSelfContained，Api 專案的 apphost 與其設定檔會跟著掉進輸出；
# 殼是在 process 內 host Api（規則 2），從不執行它。**Scheduler.Api.dll 不能刪**——那是殼引用的組件本體，
# 刪掉會在 OnLoaded 丟 FileNotFoundException（實測）。appsettings.Development.json 也會被 ContentRoot 讀到，
# 使用者機器若設了 ASPNETCORE_ENVIRONMENT=Development 就會疊上去。MSW 的 service worker 是前端開發用檔案。
# pdb 刻意留著：單機版沒有遙測，例外堆疊裡的行號是唯一的現場診斷資訊。
Write-Host '      清掉殘留檔 …' -ForegroundColor DarkGray
$leftovers = @(
    'Scheduler.Api.exe', 'Scheduler.Api.runtimeconfig.json', 'Scheduler.Api.deps.json',
    'appsettings.Development.json',
    'wwwroot/mockServiceWorker.js'
)
foreach ($rel in $leftovers) {
    $p = Join-Path $OutputPath $rel
    if (Test-Path $p) { Remove-Item $p -Force }
}

# 前端建置目標與隨附 runtime 的主版號要一致（CLAUDE.md「改一邊就要改另一邊」），這裡機械性地守住
$wv2 = Get-Content (Join-Path $PSScriptRoot 'webview2.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$viteConfig = Get-Content (Join-Path $repoRoot 'frontend/vite.config.ts') -Raw -Encoding UTF8
if ($viteConfig -notmatch "WEBVIEW2_CHROMIUM_TARGET\s*=\s*'chrome$($wv2.chromiumMajor)'") {
    throw "frontend/vite.config.ts 的 WEBVIEW2_CHROMIUM_TARGET 不是 chrome$($wv2.chromiumMajor)，與 build/webview2.json 脫鉤"
}

# --- 3. VC++ runtime（app-local）---
# OR-Tools 的 native DLL（ortools.dll、google-ortools-native.dll、abseil、protobuf、SCIP、HiGHS…）
# 動態連結 MSVC CRT。開發機裝了 Visual Studio 所以感覺不到，乾淨的 Windows 上會直接
# 「找不到 msvcp140.dll」。Microsoft 允許把這三個檔案放在應用程式目錄隨附（app-local），
# 不需要安裝 vc_redist —— portable 前提得以維持。
Write-Host '[3/6] 放入 VC++ runtime …' -ForegroundColor Cyan
$crtFiles = 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll'
if (-not $VcRedistPath) {
    $candidates = foreach ($pf in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if ($pf) { Get-ChildItem -Path (Join-Path $pf 'Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC143.CRT') -Directory -ErrorAction SilentlyContinue }
    }
    $VcRedistPath = $candidates |
        Sort-Object { [version]($_.FullName -replace '.*\\MSVC\\([\d.]+)\\.*', '$1') } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $VcRedistPath -or ($crtFiles | Where-Object { -not (Test-Path (Join-Path $VcRedistPath $_)) })) {
    throw @"
找不到 VC++ runtime 檔案（$($crtFiles -join ', ')）。

請安裝 Visual Studio 或 Build Tools 的「MSVC v143 - VS 2022 C++ x64/x86 建置工具」元件，
或以 -VcRedistPath 指定含這三個檔案的目錄。沒有它們，OR-Tools 在乾淨的 Windows 上載不起來。
"@
}
foreach ($f in $crtFiles) { Copy-Item (Join-Path $VcRedistPath $f) $OutputPath -Force }
Write-Host "      來源：$VcRedistPath" -ForegroundColor DarkGray

# --- 4. WebView2 Fixed Version ---
Write-Host '[4/6] 複製 WebView2 Fixed Version runtime …' -ForegroundColor Cyan
$runtimeSource = Join-Path $PSScriptRoot 'webview2-runtime'
if (-not (Test-Path (Join-Path $runtimeSource 'msedgewebview2.exe'))) {
    throw @"
找不到 WebView2 Fixed Version runtime：$runtimeSource

請先執行 build/fetch-webview2.ps1。沒有這份 runtime，發佈包在未安裝
WebView2 的機器上無法啟動，portable 前提即不成立。
"@
}
Copy-Item $runtimeSource (Join-Path $OutputPath 'webview2') -Recurse -Force

# --- 5. portable 資料夾 ---
Write-Host '[5/6] 建立 data/ …' -ForegroundColor Cyan
New-Item -ItemType Directory -Path (Join-Path $OutputPath 'data') -Force | Out-Null
# 名冊檔放 roster/roster.csv，與 data/ 並列（不在 data/ 裡：data/ 是執行期狀態，使用者清掉重來時名冊要留著）
if ($RosterFile) {
    New-Item -ItemType Directory -Path (Join-Path $OutputPath 'roster') -Force | Out-Null
    Copy-Item -LiteralPath $RosterFile (Join-Path $OutputPath 'roster/roster.csv') -Force
}

# --- 5b. 使用者說明（#80）---
if (-not $UserGuidePath) { $UserGuidePath = Join-Path $repoRoot 'docs/user-guide' }
if (Test-Path $UserGuidePath) {
    Copy-Item $UserGuidePath (Join-Path $OutputPath '使用者說明') -Recurse -Force
    Write-Host "      使用者說明：$UserGuidePath" -ForegroundColor DarkGray
} else {
    Write-Warning "找不到使用者說明資料夾 $UserGuidePath，發佈包不含使用者說明（交付版本不該缺）"
}

# exe 的產品版本必須等於 <Version>（有人在 csproj 覆寫 Version 時會在這裡被擋下）
$exePath = Join-Path $OutputPath 'HospitalScheduler.exe'
$productVersion = (Get-Item $exePath).VersionInfo.ProductVersion
if ($productVersion -ne $version) {
    throw "HospitalScheduler.exe 的 ProductVersion 是 '$productVersion'，不等於 $version。檢查是否有專案覆寫了 Version。"
}

# --- 6. native 相依檢查 ---
Write-Host '[6/6] 檢查 native 相依 …' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'check-native-deps.ps1') -Path $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'native 相依檢查失敗，發佈包在乾淨的 Windows 上跑不起來' }

if ($Zip) {
    $zipPath = Join-Path (Split-Path $OutputPath -Parent) "$(Split-Path $OutputPath -Leaf).zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "壓縮 → $zipPath" -ForegroundColor Cyan
    Compress-Archive -LiteralPath $OutputPath -DestinationPath $zipPath -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $za = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try { $zipFiles = @($za.Entries | Where-Object { $_.Name -ne '' }).Count } finally { $za.Dispose() }
    $dirFiles = @(Get-ChildItem -LiteralPath $OutputPath -Recurse -File).Count
    if ($zipFiles -ne $dirFiles) { throw "zip 內檔案數 $zipFiles 與資料夾 $dirFiles 不一致" }
    Write-Host "      zip 檔案數 $zipFiles，與資料夾一致" -ForegroundColor DarkGray
}

$size = [math]::Round((Get-ChildItem $OutputPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ''
Write-Host "發佈完成：$OutputPath（$size MB）" -ForegroundColor Green
Write-Host ''
Write-Host '交付前請跑過 docs/ARCHITECTURE.md §10 的驗收清單，尤其是：' -ForegroundColor Yellow
Write-Host '  - 在一台沒有 .NET / WebView2 / VC++ Redist 的乾淨 Windows 上解壓執行' -ForegroundColor Yellow
Write-Host '    （上面的 native 相依檢查是靜態分析，只能證明「沒有懸空的 import」，取代不了這一步）' -ForegroundColor Yellow
Write-Host '  - 確認 %APPDATA% / %LOCALAPPDATA% / 登錄檔沒有任何寫入' -ForegroundColor Yellow
Write-Host '  - 第一次啟動後人員管理畫面的清單：有帶 -RosterFile 應等於名冊檔的人數，沒帶則為空' -ForegroundColor Yellow
Write-Host '    （#37：假名參考名單只在 DEBUG 建置種；#82：名冊由 roster/roster.csv 首次啟動匯入一次）' -ForegroundColor Yellow
if ($RosterFile) {
    Write-Host '  - 發佈包內含真實名冊（roster/roster.csv），交付與保管請依個資規範處理' -ForegroundColor Yellow
}
