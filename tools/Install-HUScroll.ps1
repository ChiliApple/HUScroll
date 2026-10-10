<#
.SYNOPSIS
    Installs the HUScroll plugin for the installed Greenshot (1.3.x or 1.4.x)
.DESCRIPTION
    Zielmaschine: der Windows-PC, auf dem Greenshot läuft (z. B. dein Notebook). PowerShell 5.1.

    - Finds Greenshot.exe (running process, Program Files, or -GreenshotPath) and reads its version
    - Greenshot 1.3: copies the 1.3 plugin to %APPDATA%\Greenshot\Plugins\Greenshot.Plugin.HUScroll (no admin rights needed)
    - Greenshot 1.4: copies the 1.4 plugin to <Greenshot folder>\Plugins\Greenshot.Plugin.HUScroll (admin rights needed,
      1.4 only loads plugins from its own folder)
    - Removes the "downloaded from the internet" mark (otherwise .NET may refuse to load the DLL)
    - Restarts Greenshot
.PARAMETER GreenshotPath
    Path to Greenshot.exe, if it isn't found automatically
.PARAMETER Uninstall
    Removes the plugin again
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1
#>
[CmdletBinding()]
param(
    [string]$GreenshotPath,
    [switch]$Uninstall,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$pluginFolderName = 'Greenshot.Plugin.HUScroll'
$pluginFileName = 'Greenshot.Plugin.HUScroll.dll'

function Find-GreenshotExe {
    if ($GreenshotPath) {
        if (Test-Path $GreenshotPath -PathType Leaf) { return (Resolve-Path $GreenshotPath).Path }
        $candidate = Join-Path $GreenshotPath 'Greenshot.exe'
        if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
        throw "Greenshot.exe not found at $GreenshotPath"
    }

    $running = Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($running -and $running.Path) { return $running.Path }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Greenshot\Greenshot.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Greenshot\Greenshot.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Greenshot\Greenshot.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    throw 'Greenshot.exe not found - start Greenshot or use -GreenshotPath'
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$exe = Find-GreenshotExe
$greenshotDir = Split-Path -Parent $exe
$versionInfo = (Get-Item $exe).VersionInfo
# FileVersion can contain a suffix (1.4 continuous builds), the numeric parts are reliable
$version = New-Object Version $versionInfo.FileMajorPart, $versionInfo.FileMinorPart, $versionInfo.FileBuildPart
# PortableApps mode: an App\Greenshot folder next to the exe, plugins are then only searched there
# (the plain "PORTABLE" zip from GitHub has no App\Greenshot folder and behaves like an installation)
$pafDir = Join-Path $greenshotDir 'App\Greenshot'
$isPaf = Test-Path $pafDir -PathType Container
Write-Host "Greenshot $version found: $exe$(if ($isPaf) { ' (PortableApps)' })"

$appDataTarget = Join-Path $env:APPDATA "Greenshot\Plugins\$pluginFolderName"
$programTarget = Join-Path $greenshotDir "Plugins\$pluginFolderName"
$pafTarget = Join-Path $pafDir "Plugins\$pluginFolderName"

if ($version.Major -eq 1 -and $version.Minor -eq 3) {
    $variant = 'Greenshot-1.3'
    # 1.3 loads from %APPDATA%\Greenshot and from its own folder (both recursive), PortableApps only from App\Greenshot
    $target = if ($isPaf) { $pafTarget } else { $appDataTarget }
    # Copies this Greenshot would load as well (never two copies)
    $duplicates = if ($isPaf) { @() } else { @($programTarget) }
}
elseif ($version.Major -eq 1 -and $version.Minor -ge 4) {
    $variant = 'Greenshot-1.4'
    # 1.4 only loads from its own folder (or App\Greenshot for PortableApps), %APPDATA% is not searched -
    # an installation for Greenshot 1.3 there is left alone
    $target = if ($isPaf) { $pafTarget } else { $programTarget }
    $duplicates = @()
}
else {
    throw "Greenshot $version is not supported (1.3.x or 1.4.x)"
}

$needsAdmin = $target.StartsWith($env:ProgramFiles, [StringComparison]::OrdinalIgnoreCase) -or
    (${env:ProgramFiles(x86)} -and $target.StartsWith(${env:ProgramFiles(x86)}, [StringComparison]::OrdinalIgnoreCase))
if ($needsAdmin -and -not (Test-IsAdmin)) {
    throw "Admin rights needed for $target - start PowerShell as administrator"
}

$wasRunning = [bool](Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue)
if ($wasRunning -and -not $NoRestart) {
    Write-Host 'Stopping Greenshot ...'
    Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Never leave two copies (Greenshot would load both)
foreach ($folder in @($target) + $duplicates) {
    if (Test-Path $folder) {
        Remove-Item $folder -Recurse -Force
        Write-Host "Removed $folder"
    }
}

if (-not $Uninstall) {
    $source = Join-Path $PSScriptRoot "$variant\$pluginFileName"
    if (-not (Test-Path $source)) { throw "Plugin file missing: $source" }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item $source -Destination $target -Force
    $installed = Join-Path $target $pluginFileName
    Unblock-File -Path $installed
    Write-Host "HUScroll ($variant) installed: $installed" -ForegroundColor Green
}
else {
    Write-Host 'HUScroll removed' -ForegroundColor Green
}

if (($wasRunning -or -not $Uninstall) -and -not $NoRestart) {
    Write-Host 'Starting Greenshot ...'
    Start-Process -FilePath $exe
}
