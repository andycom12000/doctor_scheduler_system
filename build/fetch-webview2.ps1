#Requires -Version 5.1
<#
.SYNOPSIS
    準備隨附的 WebView2 Fixed Version runtime。

.DESCRIPTION
    portable 發佈的必要條件：目標機器不會裝 WebView2 Evergreen runtime，
    因此必須把 Fixed Version（約 180MB）隨程式一起帶走。
    見 docs/ARCHITECTURE.md §1.1 與 §6。

    Microsoft 沒有為 Fixed Version 提供穩定的直接下載網址，也沒有公開 NuGet 套件，
    因此 .cab 必須手動下載一次，之後這支腳本負責驗證版本並解壓到 build/webview2-runtime/。

.PARAMETER CabPath
    已下載的 .cab 檔路徑。省略時在 build/ 下自動尋找。

.EXAMPLE
    pwsh build/fetch-webview2.ps1
    pwsh build/fetch-webview2.ps1 -CabPath ~/Downloads/Microsoft.WebView2.FixedVersionRuntime.141.0.3537.85.x64.cab
#>
[CmdletBinding()]
param(
    [string] $CabPath
)

$ErrorActionPreference = 'Stop'

$buildDir  = $PSScriptRoot
$config    = Get-Content (Join-Path $buildDir 'webview2.json') -Raw | ConvertFrom-Json
$targetDir = Join-Path $buildDir 'webview2-runtime'

if ($config.version -eq 'TODO') {
    throw @"
build/webview2.json 的 version 尚未填寫。

請先到 $($config.downloadPage) 的「Fixed Version」區塊，
選定一個版本下載 $($config.architecture) 的 .cab，
再把版號填入 build/webview2.json（同時填 chromiumMajor），
並確認 frontend/vite.config.ts 的 WEBVIEW2_CHROMIUM_TARGET 與其一致。
"@
}

$expectedName = "Microsoft.WebView2.FixedVersionRuntime.$($config.version).$($config.architecture).cab"

if (-not $CabPath) {
    $CabPath = Join-Path $buildDir $expectedName
}

if (-not (Test-Path $CabPath)) {
    throw @"
找不到 WebView2 Fixed Version 封裝：$CabPath

Microsoft 未提供穩定的直接下載網址，需手動下載一次：
  1. 開啟 $($config.downloadPage)
  2. 在「Fixed Version」區塊選擇版本 $($config.version)、架構 $($config.architecture)
  3. 下載得到的 .cab 放到 build/ 下，或以 -CabPath 指定路徑
  4. 重新執行本腳本

.cab 本身不進版控（見 .gitignore），每台開發機各自準備一次即可。
"@
}

$actualName = Split-Path $CabPath -Leaf
if ($actualName -ne $expectedName) {
    Write-Warning "檔名與 build/webview2.json 指定的版本不符：`n  預期 $expectedName`n  實際 $actualName"
    Write-Warning '若確定要用這個版本，請先更新 build/webview2.json，避免隨附版本與前端建置目標脫鉤。'
}

if (Test-Path $targetDir) {
    Write-Host "清除舊的 $targetDir" -ForegroundColor DarkGray
    Remove-Item $targetDir -Recurse -Force
}
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Write-Host "解壓 $actualName …" -ForegroundColor Cyan
& expand.exe $CabPath -F:* $targetDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "expand.exe 失敗，結束碼 $LASTEXITCODE" }

# .cab 內是一層以版號命名的資料夾，攤平以簡化發佈時的複製。
$inner = Get-ChildItem $targetDir -Directory | Where-Object { $_.Name -like "*$($config.version)*" }
if ($inner.Count -eq 1) {
    Get-ChildItem $inner[0].FullName -Force | Move-Item -Destination $targetDir
    Remove-Item $inner[0].FullName -Recurse -Force
}

$msedge = Join-Path $targetDir 'msedgewebview2.exe'
if (-not (Test-Path $msedge)) {
    throw "解壓後找不到 msedgewebview2.exe，$targetDir 的內容可能不是預期的 Fixed Version 封裝。"
}

$size = [math]::Round((Get-ChildItem $targetDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "完成：$targetDir（$size MB）" -ForegroundColor Green
