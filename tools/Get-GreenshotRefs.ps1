<#
.SYNOPSIS
    Downloads the Greenshot portable builds HUScroll is compiled against into .\lib
.DESCRIPTION
    Zielmaschine: Build-PC / GitHub Actions Runner (Windows), PowerShell 5.1 oder 7.
    The plugins reference Greenshot.Base.dll etc. of the exact Greenshot version they are built for.
    The binaries are not part of the repository, this script fetches them from the official Greenshot releases.
.EXAMPLE
    .\tools\Get-GreenshotRefs.ps1
#>
[CmdletBinding()]
param(
    [string]$Greenshot13Tag = 'v1.3.323',
    [string]$Greenshot13Asset = 'Greenshot-PORTABLE-1.3.323-RELEASE.zip',
    [string]$Greenshot14Tag = 'v1.4.324-g60f4188093',
    [string]$Greenshot14Asset = 'Greenshot-PORTABLE-1.4.324-g60f4188093-UNSTABLE-UNSIGNED.zip',
    # SHA-256 of the downloads, checked before use (update together with the tags)
    [string]$Greenshot13Sha256 = 'a67345869cd6eee249208bde51348390acf1cdd92b59ea760db8eec530faed24',
    [string]$Greenshot14Sha256 = '07632e2ecc56f0717c77c8df38c1a9d5608fc1c0df506aa97d5592006d6fe11f',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$lib = Join-Path $root 'lib'
New-Item -ItemType Directory -Path $lib -Force | Out-Null

$targets = @(
    @{ Name = 'greenshot-1.3'; Tag = $Greenshot13Tag; Asset = $Greenshot13Asset; Sha256 = $Greenshot13Sha256 },
    @{ Name = 'greenshot-1.4'; Tag = $Greenshot14Tag; Asset = $Greenshot14Asset; Sha256 = $Greenshot14Sha256 }
)

foreach ($target in $targets) {
    $destination = Join-Path $lib $target.Name
    $marker = Join-Path $destination 'Greenshot.Base.dll'
    if ((Test-Path $marker) -and -not $Force) {
        Write-Host "$($target.Name): already present ($destination)"
        continue
    }

    $url = "https://github.com/greenshot/greenshot/releases/download/$($target.Tag)/$($target.Asset)"
    $zip = Join-Path $lib $target.Asset
    Write-Host "$($target.Name): downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($hash -ne $target.Sha256) {
        Remove-Item -LiteralPath $zip -Force
        throw "$($target.Asset): SHA-256 $hash does not match the expected $($target.Sha256)"
    }

    if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $destination -Force
    Remove-Item $zip -Force

    if (-not (Test-Path $marker)) {
        throw "Greenshot.Base.dll not found in $destination - did the release layout change?"
    }
    $version = (Get-Item $marker).VersionInfo.FileVersion
    Write-Host "$($target.Name): Greenshot.Base.dll $version"
}
