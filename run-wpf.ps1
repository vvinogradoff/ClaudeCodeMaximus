<#
.SYNOPSIS
    Builds TessynDesktop.WPF and the Avalonia daemon host (TessynDesktop), copies the WPF output to [Publish-WPF] and the host output to
    [Publish-WPF]\host next to this script, and starts the WPF client from there, so a build output folder is never locked by a running app.
    The WPF client then starts the host itself (TessynDesktop.exe --background) when no daemon answers, and updates both folders by itself
    whenever a newer build exists under SourceCodesLocation. Run this script once to create the folders (the first switch), or to force a refresh.
.PARAMETER Configuration
    Build configuration (default Debug).
.PARAMETER NoStart
    Build and copy only.
.NOTES
    Run with: Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass; .\run-wpf.ps1
    A running WPF instance started from [Publish-WPF] is stopped first. A running host is left alone (its files are locked): its folder is then
    not refreshed, and the WPF client refreshes it the next time it has to start a host. Never touches [Publish] (the Avalonia app, the rollback).
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repo 'code\TessynDesktop.WPF\TessynDesktop.WPF.csproj'
$publish = Join-Path $repo '[Publish-WPF]'
$exeName = 'TessynDesktop.WPF.exe'
$hostProject = Join-Path $repo 'code\TessynDesktop\TessynDesktop.csproj'
$hostPublish = Join-Path $publish 'host'

Write-Host "Building $project ($Configuration)..."
dotnet build $project -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed; nothing was copied.' }

Write-Host "Building $hostProject ($Configuration)..."
dotnet build $hostProject -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Host build failed; nothing was copied.' }
$hostOutput = Join-Path $repo "code\TessynDesktop\bin\$Configuration\net10.0-windows10.0.17763.0"
if (-not (Test-Path -LiteralPath (Join-Path $hostOutput 'TessynDesktop.exe'))) { throw "Host build output not found in $hostOutput" }

$output = Join-Path $repo "code\TessynDesktop.WPF\bin\$Configuration\net10.0-windows10.0.17763.0"
if (-not (Test-Path -LiteralPath (Join-Path $output $exeName))) { throw "Build output not found in $output" }

# Stop an instance that runs from the publish folder (matched by path, so other copies are left alone).
$running = Get-Process -Name 'TessynDesktop.WPF' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($publish, [System.StringComparison]::OrdinalIgnoreCase) }
foreach ($process in $running) {
    Write-Host "Stopping running instance (pid $($process.Id))..."
    $process.CloseMainWindow() | Out-Null
    if (-not $process.WaitForExit(5000)) { $process.Kill() ; $process.WaitForExit() }
}

Write-Host "Copying to $publish ..."
New-Item -ItemType Directory -Force -Path $publish | Out-Null
# /XD host: the host folder lives inside the run folder and must survive the mirror.
robocopy $output $publish /MIR /XD $hostPublish /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

$runningHost = Get-Process -Name 'TessynDesktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($hostPublish, [System.StringComparison]::OrdinalIgnoreCase) }
if ($runningHost) {
    Write-Host "The background host is running (pid $($runningHost[0].Id)); its folder was not refreshed. Exit it from its tray icon to refresh it."
}
else {
    Write-Host "Copying the daemon host to $hostPublish ..."
    New-Item -ItemType Directory -Force -Path $hostPublish | Out-Null
    robocopy $hostOutput $hostPublish /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy (host) failed with exit code $LASTEXITCODE" }
}

if ($NoStart) {
    Write-Host 'Done (not started).'
    exit 0
}

$info = New-Object System.Diagnostics.ProcessStartInfo
$info.FileName = Join-Path $publish $exeName
$info.WorkingDirectory = $publish
$info.UseShellExecute = $true
[System.Diagnostics.Process]::Start($info) | Out-Null
Write-Host "Started $($info.FileName)"
