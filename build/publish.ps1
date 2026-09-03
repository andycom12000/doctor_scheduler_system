#Requires -Version 5.1
<#
.SYNOPSIS
    產出資料夾式 portable 發佈包。

.DESCRIPTION
    步驟：
      1. 建置前端（輸出到 src/Scheduler.Shell/wwwroot）
      2. dotnet publish Scheduler.Shell（self-contained、win-x64、不 trim、不單一檔）
      3. app-local 放入 VC++ runtime（msvcp140 / vcruntime140 / vcruntime140_1）
         —— OR-Tools 的 native 程式庫依賴它們，NuGet 套件不附帶，見 docs/ARCHITECTURE.md §9
      4. 複製 WebView2 Fixed Version runtime 到 webview2/
      5. 建立空的 data/
      6. 跑 build/check-native-deps.ps1 確認包內沒有懸空的 native 相依

    產出結構見 docs/ARCHITECTURE.md §3.2。使用者解壓即用，無需安裝任何東西。

.PARAMETER OutputPath
    發佈目錄，預設 publish/HospitalScheduler。

.PARAMETER SkipFrontend
    跳過前端建置（wwwroot 已是最新時可用）。

.PARAMETER VcRedistPath
    含 msvcp140.dll / vcruntime140.dll / vcruntime140_1.dll 的目錄。省略時自動在
    Visual Studio / Build Tools 的 VC\Redist\MSVC\<版本>\x64\Microsoft.VC143.CRT 尋找最新版。

.EXAMPLE
    pwsh build/publish.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [switch] $SkipFrontend,
    [string] $VcRedistPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'publish/HospitalScheduler'
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

# --- 2. .NET ---
Write-Host '[2/6] dotnet publish …' -ForegroundColor Cyan
if (Test-Path $OutputPath) { Remove-Item $OutputPath -Recurse -Force }

dotnet publish (Join-Path $repoRoot 'src/Scheduler.Shell/Scheduler.Shell.csproj') `
    --configuration Release `
    --output $OutputPath `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失敗' }

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

# --- 6. native 相依檢查 ---
Write-Host '[6/6] 檢查 native 相依 …' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'check-native-deps.ps1') -Path $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'native 相依檢查失敗，發佈包在乾淨的 Windows 上跑不起來' }

$size = [math]::Round((Get-ChildItem $OutputPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ''
Write-Host "發佈完成：$OutputPath（$size MB）" -ForegroundColor Green
Write-Host ''
Write-Host '交付前請跑過 docs/ARCHITECTURE.md §8 的驗收清單，尤其是：' -ForegroundColor Yellow
Write-Host '  - 在一台沒有 .NET / WebView2 / VC++ Redist 的乾淨 Windows 上解壓執行' -ForegroundColor Yellow
Write-Host '    （上面的 native 相依檢查是靜態分析，只能證明「沒有懸空的 import」，取代不了這一步）' -ForegroundColor Yellow
Write-Host '  - 確認 %APPDATA% / %LOCALAPPDATA% / 登錄檔沒有任何寫入' -ForegroundColor Yellow
