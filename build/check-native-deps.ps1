#Requires -Version 5.1
<#
.SYNOPSIS
    檢查發佈包裡的 native DLL 相依是否都能在包內或作業系統本身找到。

.DESCRIPTION
    解析每個 .dll / .exe 的 PE import table，列出它們依賴的 DLL，然後分三類：
      - 作業系統自帶（kernel32、api-ms-win-*、ucrtbase 等）：忽略
      - 發佈包裡有的：通過
      - 兩者皆非：**失敗** —— 這就是「在乾淨的 Windows 上跑不起來」的來源

    背景：OR-Tools 的 native 程式庫（ortools.dll、google-ortools-native.dll、abseil、
    protobuf、SCIP、HiGHS…）由 MSVC 建置且動態連結 CRT，依賴 msvcp140.dll、
    vcruntime140.dll、vcruntime140_1.dll。這三個檔案 Google.OrTools 的 NuGet 套件
    **不附帶**，開發機因為裝了 Visual Studio / Build Tools 所以看不出來。
    build/publish.ps1 會把它們 app-local 放進發佈包根目錄，這支腳本負責守住這件事。
    見 docs/ARCHITECTURE.md §9。

.PARAMETER Path
    發佈目錄。

.EXAMPLE
    pwsh build/check-native-deps.ps1 -Path publish/HospitalScheduler
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

function Get-PeImports {
    param([string] $File)

    $bytes = [System.IO.File]::ReadAllBytes($File)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { return @() }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { return @() }

    $coff = $peOffset + 4
    $sectionCount = [BitConverter]::ToUInt16($bytes, $coff + 2)
    $optionalSize = [BitConverter]::ToUInt16($bytes, $coff + 16)
    $optional = $coff + 20
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $dataDirectories = $optional + $(if ($magic -eq 0x20B) { 0x70 } else { 0x60 })
    $importRva = [BitConverter]::ToUInt32($bytes, $dataDirectories + 8)   # 第 1 個 data directory = import table
    if ($importRva -eq 0) { return @() }
    # 第 14 個 data directory = CLR header。受管組件只 import mscoree.dll（作業系統自帶），
    # 真正的 native 相依在 P/Invoke 目標裡，那些 DLL 會被個別掃到，所以受管組件直接跳過。
    if ([BitConverter]::ToUInt32($bytes, $dataDirectories + 14 * 8) -ne 0) { return @() }

    $sections = @()
    $sectionTable = $optional + $optionalSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $s = $sectionTable + $i * 40
        $sections += [pscustomobject]@{
            VirtualAddress = [BitConverter]::ToUInt32($bytes, $s + 12)
            Size           = [Math]::Max([BitConverter]::ToUInt32($bytes, $s + 8), [BitConverter]::ToUInt32($bytes, $s + 16))
            RawPointer     = [BitConverter]::ToUInt32($bytes, $s + 20)
        }
    }

    $rvaToOffset = {
        param($rva)
        foreach ($sec in $sections) {
            if ($rva -ge $sec.VirtualAddress -and $rva -lt ($sec.VirtualAddress + $sec.Size)) {
                return $rva - $sec.VirtualAddress + $sec.RawPointer
            }
        }
        throw "RVA 0x$($rva.ToString('X')) 不在任何 section 內：$File"
    }

    $names = @()
    $entry = & $rvaToOffset $importRva
    while ($true) {
        $nameRva = [BitConverter]::ToUInt32($bytes, $entry + 12)
        if ($nameRva -eq 0) { break }
        $nameOffset = & $rvaToOffset $nameRva
        $end = [Array]::IndexOf($bytes, [byte]0, [int]$nameOffset)
        $names += [System.Text.Encoding]::ASCII.GetString($bytes, $nameOffset, $end - $nameOffset)
        $entry += 20
    }
    return $names
}

# Windows 10 / 11 本身就有的東西。api-ms-win-* 與 ucrtbase 是 Universal CRT，
# 自 Windows 10 起是作業系統元件，不需要隨附。
$osProvided = @(
    '^api-ms-win-', '^ext-ms-', '^ucrtbase\.dll$',
    '^kernel32\.dll$', '^kernelbase\.dll$', '^ntdll\.dll$', '^user32\.dll$', '^gdi32\.dll$',
    '^advapi32\.dll$', '^shell32\.dll$', '^ole32\.dll$', '^oleaut32\.dll$', '^shlwapi\.dll$',
    '^ws2_32\.dll$', '^crypt32\.dll$', '^bcrypt\.dll$', '^ncrypt\.dll$', '^secur32\.dll$', '^sspicli\.dll$',
    '^iphlpapi\.dll$', '^winhttp\.dll$', '^wininet\.dll$', '^version\.dll$', '^psapi\.dll$',
    '^dbghelp\.dll$', '^rpcrt4\.dll$', '^comctl32\.dll$', '^comdlg32\.dll$', '^uxtheme\.dll$',
    '^dwmapi\.dll$', '^imm32\.dll$', '^msimg32\.dll$', '^winmm\.dll$', '^powrprof\.dll$',
    '^userenv\.dll$', '^setupapi\.dll$', '^cfgmgr32\.dll$', '^wtsapi32\.dll$', '^mswsock\.dll$',
    '^normaliz\.dll$', '^wldap32\.dll$', '^d3d11\.dll$', '^dxgi\.dll$', '^d2d1\.dll$', '^dwrite\.dll$',
    '^windowscodecs\.dll$', '^propsys\.dll$', '^oleacc\.dll$', '^urlmon\.dll$', '^mpr\.dll$',
    '^netapi32\.dll$', '^wevtapi\.dll$', '^pdh\.dll$', '^activeds\.dll$', '^shcore\.dll$',
    '^msvcrt\.dll$', '^windows\.', '^combase\.dll$', '^oleaut32\.dll$', '^clbcatq\.dll$',
    '^mfplat\.dll$', '^mf\.dll$', '^wintrust\.dll$', '^cabinet\.dll$', '^winspool\.drv$',
    '^d3d9\.dll$', '^opengl32\.dll$', '^hid\.dll$', '^ktmw32\.dll$', '^authz\.dll$', '^fltlib\.dll$',
    # 掃 WebView2 152 的核心檔（msedgewebview2.exe、EmbeddedBrowserWebView.dll…）補上的，一樣是 Windows 10/11 自帶
    '^avrt\.dll$', '^bcp47langs\.dll$', '^bcp47mrm\.dll$', '^bcryptprimitives\.dll$', '^coremessaging\.dll$',
    '^dcomp\.dll$', '^dnsapi\.dll$', '^elscore\.dll$', '^httpapi\.dll$', '^profapi\.dll$', '^rometadata\.dll$',
    '^usp10\.dll$', '^wer\.dll$', '^xmllite\.dll$'
)

$root = Get-Item $Path
# webview2/ 是 Microsoft 原封不動的 Fixed Version 封裝，核心檔照掃（它們的 import 都是 OS 自帶，上面白名單已補齊）。
# 只跳過兩樣 Microsoft 自己就載不起來、也與我們無關的東西（實測 152.0.4191.62）：
#   - undocked_copilot/：Copilot 語音編解碼器硬 import GStreamer（glib、gobject、gstreamer…），封裝內沒附
#   - prefs_enclave_x64.dll：VBS enclave 內載入，import ucrtbase_enclave／vertdll
# runtime 本身的完整性由乾淨 Windows 的實機驗收守（ARCHITECTURE §10）。
$runtimeDir = Join-Path $root.FullName 'webview2'
$skip = @(
    ((Join-Path $runtimeDir 'undocked_copilot') + [IO.Path]::DirectorySeparatorChar),   # 逗號比 + 先結合，要多包一層括號
    (Join-Path $runtimeDir 'prefs_enclave_x64.dll')
)
$files = Get-ChildItem $root.FullName -Recurse -File |
    Where-Object { $_.Extension -in '.dll', '.exe' } |
    Where-Object { $f = $_.FullName; -not ($skip | Where-Object { $f.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) }
$present = @{}
foreach ($f in $files) { $present[$f.Name.ToLowerInvariant()] = $true }

$missing = @{}
foreach ($f in $files) {
    foreach ($dep in (Get-PeImports $f.FullName)) {
        $key = $dep.ToLowerInvariant()
        if ($present.ContainsKey($key)) { continue }
        if ($osProvided | Where-Object { $key -match $_ }) { continue }
        if (-not $missing.ContainsKey($key)) { $missing[$key] = New-Object System.Collections.Generic.List[string] }
        $missing[$key].Add($f.FullName.Substring($root.FullName.Length).TrimStart('\', '/'))
    }
}

if ($missing.Count -eq 0) {
    Write-Host "native 相依檢查通過：$($files.Count) 個 PE 檔（不含 webview2/ 的 Copilot 與 enclave），所有 import 都在包內或作業系統內。" -ForegroundColor Green
    exit 0
}

Write-Host '發佈包缺少以下 native 相依，在沒裝對應 runtime 的機器上會啟動失敗：' -ForegroundColor Red
foreach ($k in ($missing.Keys | Sort-Object)) {
    Write-Host "  $k" -ForegroundColor Red
    foreach ($user in ($missing[$k] | Sort-Object -Unique)) { Write-Host "      ← $user" -ForegroundColor DarkGray }
}
exit 1
