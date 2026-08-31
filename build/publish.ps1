#Requires -Version 5.1
<#
.SYNOPSIS
    產出資料夾式 portable 發佈包。

.DESCRIPTION
    步驟：
      1. 建置前端（輸出到 src/Scheduler.Shell/wwwroot）
      2. dotnet publish Scheduler.Shell（self-contained、win-x64、不 trim、不單一檔）
      3. 複製 WebView2 Fixed Version runtime 到 webview2/
      4. 建立空的 data/

    產出結構見 docs/ARCHITECTURE.md §3.2。使用者解壓即用，無需安裝任何東西。

.PARAMETER OutputPath
    發佈目錄，預設 publish/HospitalScheduler。

.PARAMETER SkipFrontend
    跳過前端建置（wwwroot 已是最新時可用）。

.EXAMPLE
    pwsh build/publish.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'publish/HospitalScheduler'
}

# --- 1. 前端 ---
if ($SkipFrontend) {
    Write-Host '[1/4] 跳過前端建置' -ForegroundColor DarkGray
} else {
    Write-Host '[1/4] 建置前端 …' -ForegroundColor Cyan
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
Write-Host '[2/4] dotnet publish …' -ForegroundColor Cyan
if (Test-Path $OutputPath) { Remove-Item $OutputPath -Recurse -Force }

dotnet publish (Join-Path $repoRoot 'src/Scheduler.Shell/Scheduler.Shell.csproj') `
    --configuration Release `
    --output $OutputPath `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失敗' }

# --- 3. WebView2 Fixed Version ---
Write-Host '[3/4] 複製 WebView2 Fixed Version runtime …' -ForegroundColor Cyan
$runtimeSource = Join-Path $PSScriptRoot 'webview2-runtime'
if (-not (Test-Path (Join-Path $runtimeSource 'msedgewebview2.exe'))) {
    throw @"
找不到 WebView2 Fixed Version runtime：$runtimeSource

請先執行 build/fetch-webview2.ps1。沒有這份 runtime，發佈包在未安裝
WebView2 的機器上無法啟動，portable 前提即不成立。
"@
}
Copy-Item $runtimeSource (Join-Path $OutputPath 'webview2') -Recurse -Force

# --- 4. portable 資料夾 ---
Write-Host '[4/4] 建立 data/ …' -ForegroundColor Cyan
New-Item -ItemType Directory -Path (Join-Path $OutputPath 'data') -Force | Out-Null

$size = [math]::Round((Get-ChildItem $OutputPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ''
Write-Host "發佈完成：$OutputPath（$size MB）" -ForegroundColor Green
Write-Host ''
Write-Host '交付前請跑過 docs/ARCHITECTURE.md §8 的驗收清單，尤其是：' -ForegroundColor Yellow
Write-Host '  - 在一台沒有 .NET / WebView2 / VC++ Redist 的乾淨 Windows 上解壓執行' -ForegroundColor Yellow
Write-Host '  - 確認 %APPDATA% / %LOCALAPPDATA% / 登錄檔沒有任何寫入' -ForegroundColor Yellow
