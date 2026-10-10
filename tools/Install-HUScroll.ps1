<#
.SYNOPSIS
    Installs the HUScroll plugin for the installed Greenshot (1.3.x or 1.4.x)
.DESCRIPTION
    Zielmaschine: der Windows-PC, auf dem Greenshot läuft (z. B. dein Notebook). PowerShell 5.1.

    - Finds Greenshot.exe (running process, Program Files, or -GreenshotPath) and reads its version
    - Greenshot 1.3: copies the 1.3 plugin to %APPDATA%\Greenshot\Plugins\Greenshot.Plugin.HUScroll (no admin rights needed)
    - Greenshot 1.4: copies the 1.4 plugin to <Greenshot folder>\Plugins\Greenshot.Plugin.HUScroll
      (1.4 only loads plugins from its own folder; admin rights only when that is below Program Files)
    - PortableApps (App\Greenshot next to the exe): App\Greenshot\Plugins\Greenshot.Plugin.HUScroll
    - Removes the "downloaded from the internet" mark (otherwise .NET may refuse to load the DLL)
    - Closes only this Greenshot (asks before closing it the hard way - unsaved images in the editor would be lost)
      and starts it again
.PARAMETER GreenshotPath
    Path to Greenshot.exe (or its folder), if it isn't found automatically
.PARAMETER Uninstall
    Removes the plugin again
.PARAMETER NoRestart
    Don't close / start Greenshot (the plugin is loaded at the next start)
.PARAMETER Force
    Close Greenshot without asking, even when it doesn't close normally
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1
#>
[CmdletBinding()]
param(
    [string]$GreenshotPath,
    [switch]$Uninstall,
    [switch]$NoRestart,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$pluginFolderName = 'Greenshot.Plugin.HUScroll'
$pluginFileName = 'Greenshot.Plugin.HUScroll.dll'

function Find-GreenshotExe {
    if ($GreenshotPath) {
        if (Test-Path -LiteralPath $GreenshotPath -PathType Leaf) { return (Resolve-Path -LiteralPath $GreenshotPath).Path }
        $candidate = Join-Path $GreenshotPath 'Greenshot.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).Path }
        throw "Greenshot.exe not found at $GreenshotPath"
    }

    $running = Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -First 1
    if ($running) { return $running.Path }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Greenshot\Greenshot.exe'),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Greenshot\Greenshot.exe' }),
        (Join-Path $env:LOCALAPPDATA 'Programs\Greenshot\Greenshot.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }

    throw 'Greenshot.exe not found - start Greenshot or use -GreenshotPath'
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-NeedsAdmin([string]$path) {
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:windir)) {
        if ($root -and $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Stop-ThisGreenshot([string]$exePath) {
    # Only the Greenshot started from this exe (a second one, e.g. a portable test version, is left alone)
    $processes = @(Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath })
    if ($processes.Count -eq 0) { return $false }

    Write-Host 'Closing Greenshot ...'
    foreach ($process in $processes) { [void]$process.CloseMainWindow() }
    foreach ($process in $processes) { [void]$process.WaitForExit(5000) }

    $stillRunning = @($processes | Where-Object { -not $_.HasExited })
    if ($stillRunning.Count -gt 0) {
        if (-not $Force) {
            Write-Warning 'Greenshot did not close (an editor window with an unsaved image may be open).'
            $answer = Read-Host 'Close Greenshot anyway? Unsaved images will be lost. (y/N)'
            if ($answer -notmatch '^(y|yes|j|ja)$') {
                throw 'Cancelled - save your images, close Greenshot and run the installer again.'
            }
        }
        $stillRunning | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
    return $true
}

$exe = Find-GreenshotExe
$greenshotDir = Split-Path -Parent $exe
$versionInfo = (Get-Item -LiteralPath $exe).VersionInfo
# FileVersion can contain a suffix (1.4 continuous builds), the numeric parts are reliable
$version = New-Object Version $versionInfo.FileMajorPart, $versionInfo.FileMinorPart, $versionInfo.FileBuildPart
# PortableApps mode: an App\Greenshot folder next to the exe, plugins are then only searched there
# (the plain "PORTABLE" zip from GitHub has no App\Greenshot folder and behaves like an installation)
$pafDir = Join-Path $greenshotDir 'App\Greenshot'
$isPaf = Test-Path -LiteralPath $pafDir -PathType Container
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

# Check the rights for everything that will be changed BEFORE Greenshot is closed
$toRemove = @(@($target) + $duplicates | Where-Object { Test-Path -LiteralPath $_ })
$needsAdmin = (Test-NeedsAdmin $target) -or (@($toRemove | Where-Object { Test-NeedsAdmin $_ }).Count -gt 0)
if ($needsAdmin -and -not (Test-IsAdmin)) {
    throw "Admin rights needed for $target - start PowerShell as administrator"
}

$source = Join-Path $PSScriptRoot "$variant\$pluginFileName"
if (-not $Uninstall -and -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Plugin file missing: $source" }

$wasRunning = $false
if (-not $NoRestart) { $wasRunning = Stop-ThisGreenshot $exe }

try {
    # Never leave two copies (Greenshot would load both)
    foreach ($folder in $toRemove) {
        Remove-Item -LiteralPath $folder -Recurse -Force
        Write-Host "Removed $folder"
    }

    if (-not $Uninstall) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
        $installed = Join-Path $target $pluginFileName
        Unblock-File -LiteralPath $installed
        Write-Host "HUScroll ($variant) installed: $installed" -ForegroundColor Green
    }
    else {
        Write-Host 'HUScroll removed' -ForegroundColor Green
    }
}
finally {
    # Start Greenshot again even when something failed above
    if (-not $NoRestart -and ($wasRunning -or -not $Uninstall)) {
        Write-Host 'Starting Greenshot ...'
        Start-Process -FilePath $exe
    }
}
