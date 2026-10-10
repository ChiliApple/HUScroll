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
.PARAMETER AllUsers
    Installation for all users of the PC, e.g. as Intune Win32 app (runs as SYSTEM, no user interaction):
    - plugin always goes to <Greenshot folder>\Plugins\Greenshot.Plugin.HUScroll (Greenshot 1.3 and 1.4)
    - Greenshot 1.3: copies in the users' %APPDATA%\Greenshot\Plugins are removed (never two copies)
    - never asks; Greenshot is not closed by force. If Greenshot is running for a signed-in user after the install,
      a short task in that user's session closes it normally and starts it again, so the plugin appears right away
      (if it doesn't close - e.g. an editor with an unsaved image is open - it keeps running and the plugin appears
      at the next start). -NoRestart: never restart.
    - exit code 0 = OK, 1618 = plugin file in use (Greenshot running) - Intune retries later, 1 = error
    - log: %ProgramData%\HUScroll\Install-HUScroll.log
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1
.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1 -AllUsers
    (Intune: install command; uninstall command with -AllUsers -Uninstall;
     detection: file C:\Program Files\Greenshot\Plugins\Greenshot.Plugin.HUScroll\Greenshot.Plugin.HUScroll.dll)
#>
[CmdletBinding()]
param(
    [string]$GreenshotPath,
    [switch]$Uninstall,
    [switch]$NoRestart,
    [switch]$Force,
    [switch]$AllUsers
)

$ErrorActionPreference = 'Stop'
$script:ExitCode = 1

# 32-bit PowerShell on 64-bit Windows (e.g. started by a 32-bit installer service): restart as 64-bit,
# otherwise %ProgramFiles% points to "Program Files (x86)" and Greenshot is not found
if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    $ps64 = Join-Path $env:windir 'sysnative\WindowsPowerShell\v1.0\powershell.exe'
    if (Test-Path -LiteralPath $ps64) {
        $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
        if ($GreenshotPath) { $argList += @('-GreenshotPath', "`"$GreenshotPath`"") }
        foreach ($sw in 'Uninstall', 'NoRestart', 'Force', 'AllUsers') { if ((Get-Variable -Name $sw -ValueOnly)) { $argList += "-$sw" } }
        $p = Start-Process -FilePath $ps64 -ArgumentList $argList -Wait -PassThru -NoNewWindow
        exit $p.ExitCode
    }
}

# -AllUsers (Intune, SYSTEM): no questions, no restart, log file, defined exit codes
trap {
    if ($AllUsers) {
        Write-Host "ERROR: $($_.Exception.Message)"
        try { Stop-Transcript | Out-Null } catch { }
        exit $script:ExitCode
    }
    break
}
$script:UserRestart = $AllUsers -and -not $NoRestart
if ($AllUsers) {
    $NoRestart = $true
    try {
        $logDir = Join-Path $env:ProgramData 'HUScroll'
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        Start-Transcript -LiteralPath (Join-Path $logDir 'Install-HUScroll.log') -Append | Out-Null
    } catch { }
    Write-Host "HUScroll $(if ($Uninstall) { 'uninstall' } else { 'install' }) for all users - account: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
}
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

# -AllUsers: copies in the users' profiles (Greenshot 1.3 also loads from %APPDATA%\Greenshot)
function Get-UserProfilePluginFolders {
    $usersRoot = Split-Path -Parent $env:PUBLIC
    if (-not $usersRoot -or -not (Test-Path -LiteralPath $usersRoot)) { return @() }
    return @(Get-ChildItem -LiteralPath $usersRoot -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Join-Path $_.FullName "AppData\Roaming\Greenshot\Plugins\$pluginFolderName"
    })
}

if ($version.Major -eq 1 -and $version.Minor -eq 3) {
    $variant = 'Greenshot-1.3'
    # 1.3 loads from %APPDATA%\Greenshot and from its own folder (both recursive), PortableApps only from App\Greenshot
    $target = if ($isPaf) { $pafTarget } else { $appDataTarget }
    # Copies this Greenshot would load as well (never two copies)
    $duplicates = if ($isPaf) { @() } else { @($programTarget) }
    if ($AllUsers -and -not $isPaf) {
        # 1.3 loads from its own folder as well (recursive) - one copy there serves all users
        $target = $programTarget
        $duplicates = @(Get-UserProfilePluginFolders)
    }
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

# -AllUsers: running Greenshot (signed-in users) is closed normally IN the user's session with "Greenshot.exe /exit"
# (a service cannot do that itself), the plugin is replaced, then the same task starts Greenshot again.
# Never forced: if Greenshot stays open (editor with unsaved image), it keeps running.
$script:RestartTask = $null
$script:RestartFlag = $null
function Get-GreenshotProcesses { @(Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) }
if ($script:UserRestart -and (Get-GreenshotProcesses).Count) {
    try {
        $dir = Join-Path $env:ProgramData 'HUScroll'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $script:RestartFlag = Join-Path $dir 'restart.flag'
        Remove-Item -LiteralPath $script:RestartFlag -Force -ErrorAction SilentlyContinue
        $restart = Join-Path $dir 'Restart-Greenshot.ps1'
        $code = @'
$exe = '__EXE__'
$flag = '__FLAG__'
$me = (Get-Process -Id $PID).SessionId
function Get-Mine { @(Get-Process -Name 'Greenshot' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -and $_.SessionId -eq $me }) }
if (-not (Get-Mine).Count) { exit 0 }
Start-Process -FilePath $exe -ArgumentList '/exit'
# wait until the installer has replaced the plugin (max. 90 s)
for ($i = 0; $i -lt 90 -and -not (Test-Path -LiteralPath $flag); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 1
if ((Get-Mine).Count) { exit 2 }
Start-Process -FilePath $exe
exit 0
'@
        $code = $code.Replace('__EXE__', ($exe -replace "'", "''")).Replace('__FLAG__', ($script:RestartFlag -replace "'", "''"))
        Set-Content -LiteralPath $restart -Value $code -Encoding UTF8
        $script:RestartTask = 'HUScroll Greenshot restart'
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$restart`""
        # group Users by SID (every Windows language) = runs in the session of each signed-in user
        $principal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited
        Register-ScheduledTask -TaskName $script:RestartTask -Action $action -Principal $principal -Force | Out-Null
        Start-ScheduledTask -TaskName $script:RestartTask
        Write-Host 'Closing Greenshot in the user session (Greenshot.exe /exit) ...'
        for ($i = 0; $i -lt 20 -and (Get-GreenshotProcesses).Count; $i++) { Start-Sleep -Seconds 1 }
        Write-Host "Greenshot processes still running: $((Get-GreenshotProcesses).Count)"
    } catch {
        Write-Host "Closing Greenshot in the user session not possible: $($_.Exception.Message)"
    }
}

try {
    # -AllUsers: Greenshot still open (not closed in the user session) - a loaded plugin file cannot be replaced, Intune retries later (1618)
    if ($AllUsers) {
        foreach ($folder in $toRemove) {
            $dll = Join-Path $folder $pluginFileName
            if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { continue }
            try { $fs = [IO.File]::Open($dll, 'Open', 'ReadWrite', 'None'); $fs.Dispose() }
            catch {
                $script:ExitCode = 1618
                throw "Plugin file in use (Greenshot is running): $dll - nothing changed, retry later"
            }
        }
    }

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
    # -AllUsers: let the task start Greenshot again, then remove it
    if ($script:RestartTask) {
        try {
            Set-Content -LiteralPath $script:RestartFlag -Value 'done' -Encoding ASCII
            for ($i = 0; $i -lt 30; $i++) {
                Start-Sleep -Seconds 1
                if ((Get-ScheduledTask -TaskName $script:RestartTask).State -ne 'Running') { break }
            }
            $info = Get-ScheduledTaskInfo -TaskName $script:RestartTask
            Write-Host "Greenshot restart in the user session: result $($info.LastTaskResult) (0 = restarted, 2 = still open - plugin at next start)"
            Unregister-ScheduledTask -TaskName $script:RestartTask -Confirm:$false
            Remove-Item -LiteralPath $script:RestartFlag, (Join-Path (Split-Path $script:RestartFlag -Parent) 'Restart-Greenshot.ps1') -Force -ErrorAction SilentlyContinue
        } catch { Write-Host "Restart task: $($_.Exception.Message)" }
    }
    # Start Greenshot again even when something failed above
    if (-not $NoRestart -and ($wasRunning -or -not $Uninstall)) {
        Write-Host 'Starting Greenshot ...'
        Start-Process -FilePath $exe
    }
}

if ($AllUsers) {
    try { Stop-Transcript | Out-Null } catch { }
    exit 0
}
