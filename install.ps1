<#
    RGBA Channel Editor - Paint.NET plugin installer

    Usage:
        double-click install.bat
        or: powershell -ExecutionPolicy Bypass -File install.ps1

    Optional:
        -PaintNetDir "D:\App\Paint.NET"   specify the Paint.NET folder manually
        -Uninstall                        remove the installed dll

    What it does:
        1. finds ChannelEditor.dll next to this script (also checks bin\Release)
        2. scans for Paint.NET (registry uninstall entries / running process /
           file association / common paths)
        3. checks that it is a .NET 9+ build (Paint.NET 5.1+)
        4. closes a running Paint.NET if needed
        5. copies the dll into the Effects folder and removes any duplicate
           copy in the other plugin folder (so the plugin loads only once)

    NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 decodes
    script files with the ANSI codepage when there is no BOM, so non-ASCII text
    here would be mangled on some machines.
#>
param(
    [string]$PaintNetDir = "",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$DllName = "ChannelEditor.dll"

function Write-Step { param([string]$m) Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Write-Ok   { param([string]$m) Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Info { param([string]$m) Write-Host "       $m" -ForegroundColor DarkGray }
function Write-WarnMsg { param([string]$m) Write-Host "  [!]  $m" -ForegroundColor Yellow }
function Write-Fail { param([string]$m) Write-Host "  [X]  $m" -ForegroundColor Red }

Write-Host ""
Write-Host " RGBA Channel Editor - Paint.NET plugin installer" -ForegroundColor White
Write-Host " ---------------------------------------------------" -ForegroundColor DarkGray

# ---------------------------------------------------------------- find dll
Write-Step "Locating $DllName"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dllCandidates = @(
    (Join-Path $scriptDir $DllName),
    (Join-Path $scriptDir "bin\Release\net9.0-windows\$DllName"),
    (Join-Path $scriptDir "bin\Debug\net9.0-windows\$DllName")
)

$sourceDll = $null
foreach ($c in $dllCandidates) {
    if (Test-Path $c) { $sourceDll = $c; break }
}

if (-not $sourceDll) {
    Write-Fail "$DllName not found"
    Write-Info "Put $DllName next to this script, or build first:"
    Write-Info "    dotnet build ChannelEditor.csproj -c Release"
    exit 1
}

Write-Ok "Found: $sourceDll"
Write-Info ("Size: " + (Get-Item $sourceDll).Length + " bytes")

# ---------------------------------------------------------------- find paint.net
Write-Step "Scanning for Paint.NET"

function Find-PaintNetCandidateDirs {
    $found = New-Object System.Collections.Generic.List[string]

    # 1) registry uninstall entries
    $uninstallRoots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    )
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
            $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $props) { return }
            $name = $props.DisplayName
            if (-not $name) { return }
            if ($name -notmatch "Paint\.NET") { return }
            if ($props.InstallLocation) {
                $found.Add($props.InstallLocation.TrimEnd('\'))
            }
            if ($props.DisplayIcon) {
                $icon = $props.DisplayIcon.Trim('"')
                if ($icon -and (Test-Path $icon)) {
                    $found.Add((Split-Path -Parent $icon))
                }
            }
        }
    }

    # 2) running process
    Get-Process paintdotnet -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Path) { $found.Add((Split-Path -Parent $_.Path)) }
    }

    # 3) file association
    foreach ($key in @(
        "HKLM:\SOFTWARE\Classes\PaintDotNet.Document\shell\open\command",
        "HKCU:\SOFTWARE\Classes\PaintDotNet.Document\shell\open\command")) {
        if (-not (Test-Path $key)) { continue }
        $def = (Get-ItemProperty $key -ErrorAction SilentlyContinue).'(default)'
        if ($def -and ($def -match '"([^"]+\.exe)"')) {
            $found.Add((Split-Path -Parent $Matches[1]))
        }
    }

    # 4) common install locations
    $common = @(
        (Join-Path $env:ProgramFiles "Paint.NET"),
        (Join-Path $env:ProgramFiles "paint.net"),
        (Join-Path $env:LOCALAPPDATA "Programs\Paint.NET")
    )
    if (${env:ProgramFiles(x86)}) {
        $common += (Join-Path ${env:ProgramFiles(x86)} "Paint.NET")
    }
    foreach ($c in $common) {
        if ($c) { $found.Add($c) }
    }

    return $found
}

$paintNet = $null

if ($PaintNetDir) {
    $manual = $PaintNetDir.TrimEnd('\')
    if (Test-Path (Join-Path $manual "paintdotnet.exe")) {
        $paintNet = $manual
        Write-Ok "Using the directory given on the command line"
    } else {
        Write-Fail "paintdotnet.exe not found in: $manual"
        exit 1
    }
} else {
    $seen = @{}
    foreach ($dir in (Find-PaintNetCandidateDirs)) {
        if (-not $dir) { continue }
        $dir = $dir.TrimEnd('\')
        if ($seen.ContainsKey($dir.ToLowerInvariant())) { continue }
        $seen[$dir.ToLowerInvariant()] = $true
        if (Test-Path (Join-Path $dir "paintdotnet.exe")) {
            $paintNet = $dir
            break
        }
    }
}

if (-not $paintNet) {
    Write-Info "Not in the registry, no running process, no common path."
    Write-Info "Scanning local disks for paintdotnet.exe (a few seconds)..."

    $drives = @()
    try {
        $drives = @(Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" -ErrorAction SilentlyContinue |
            ForEach-Object { $_.DeviceID + "\" })
    } catch { }
    if ($drives.Count -eq 0) { $drives = @("C:\") }

    # system drive: only the two Program Files folders (scanning all of C: is too slow)
    # other drives: scan the whole drive to depth 2, which covers layouts like D:\App\Paint.NET
    $scanRoots = @()
    foreach ($root in $drives) {
        if ($root -eq "C:\") {
            $scanRoots += ($root + "Program Files")
            $scanRoots += ($root + "Program Files (x86)")
        } else {
            $scanRoots += $root
        }
    }

    foreach ($root in $scanRoots) {
        if ($paintNet) { break }
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Write-Host "       scanning $root ..." -ForegroundColor DarkGray
        try {
            $hit = Get-ChildItem -LiteralPath $root -Filter "paintdotnet.exe" -Recurse -Depth 2 -File -ErrorAction SilentlyContinue -Force |
                Select-Object -First 1
            if ($hit) { $paintNet = $hit.DirectoryName }
        } catch { }
    }

    if ($paintNet) {
        Write-Ok "Found by scanning: $paintNet"
    }
}

if (-not $paintNet) {
    Write-Fail "Paint.NET was not found"
    Write-Info "If it is installed, point the installer at it manually:"
    Write-Info '    install.bat -PaintNetDir "D:\App\Paint.NET"'
    exit 1
}

Write-Ok "Paint.NET directory: $paintNet"

# ---------------------------------------------------------------- version check
$runtimeConfig = Join-Path $paintNet "paintdotnet.runtimeconfig.json"
if (Test-Path $runtimeConfig) {
    $json = Get-Content -Raw $runtimeConfig
    if ($json -notmatch "net(9|10|11)\.0") {
        Write-WarnMsg "This Paint.NET does not look like a .NET 9+ build (5.1+). The plugin may not load."
    } else {
        Write-Ok "Version check passed (.NET 9+ / Paint.NET 5.1+)"
    }
} else {
    Write-WarnMsg "paintdotnet.runtimeconfig.json not found, skipping version check"
}

# ---------------------------------------------------------------- target folder
$effectsInProgram = Join-Path $paintNet "Effects"
$effectsInUser = Join-Path $env:LOCALAPPDATA "paint.net\Effects"

function Test-DirWritable {
    param([string]$dir)
    try {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $probe = Join-Path $dir ("_wtest_" + [Guid]::NewGuid().ToString("N") + ".tmp")
        [System.IO.File]::WriteAllText($probe, "x")
        Remove-Item $probe -Force -ErrorAction SilentlyContinue
        return $true
    } catch {
        return $false
    }
}

$targetDir = $null
if (Test-DirWritable $effectsInProgram) {
    $targetDir = $effectsInProgram
    Write-Ok "Install target: <Paint.NET dir>\Effects"
} elseif (Test-DirWritable $effectsInUser) {
    $targetDir = $effectsInUser
    Write-Ok "Paint.NET folder is read-only, using the per-user plugin folder instead"
} else {
    Write-Fail "Neither plugin folder is writable"
    Write-Info "Try running install.bat as administrator"
    exit 1
}

$targetFile = Join-Path $targetDir $DllName

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    Write-Step "Uninstall"
    $removed = 0
    foreach ($dir in @($effectsInProgram, $effectsInUser)) {
        $f = Join-Path $dir $DllName
        if (Test-Path $f) {
            Remove-Item $f -Force
            Write-Ok "Removed: $f"
            $removed++
        }
    }
    if ($removed -eq 0) {
        Write-Info "$DllName is not installed anywhere"
    } else {
        Write-Info "Restart Paint.NET and the plugin is gone."
    }
    exit 0
}

# ---------------------------------------------------------------- close process
Write-Step "Checking for a running Paint.NET"

$running = @(Get-Process paintdotnet -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-WarnMsg "Paint.NET is running ($($running.Count) process(es)); it must be closed to replace the dll."
    $answer = Read-Host "  Close it now? (Y/n)"
    if ($answer -eq "n" -or $answer -eq "N") {
        Write-Fail "Cancelled. Close Paint.NET and run this again."
        exit 1
    }
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Ok "Paint.NET closed"
} else {
    Write-Ok "Paint.NET is not running"
}

# ---------------------------------------------------------------- drop duplicates
Write-Step "Removing duplicate copies (so the plugin loads only once)"

foreach ($dir in @($effectsInProgram, $effectsInUser)) {
    if ($dir -eq $targetDir) { continue }
    $f = Join-Path $dir $DllName
    if (Test-Path $f) {
        Remove-Item $f -Force
        Write-Ok "Removed duplicate: $f"
    }
}

# ---------------------------------------------------------------- install
Write-Step "Installing"

Copy-Item $sourceDll $targetFile -Force

$installed = Get-Item $targetFile
if ($installed.Length -ne (Get-Item $sourceDll).Length) {
    Write-Fail "Size mismatch after copy, install may have failed"
    exit 1
}

Write-Ok "Installed to: $targetFile"
Write-Info ("Size: " + $installed.Length + " bytes")

Write-Host ""
Write-Host " Done. Restart Paint.NET." -ForegroundColor Green
Write-Host " Two new icons show up in the top-right icon row, left of the hammer (Tools / F5):" -ForegroundColor Gray
Write-Host "   [4-color square] = split channels    [gradient square] = merge channels" -ForegroundColor Gray
Write-Host ""
exit 0
