<#
.SYNOPSIS
    Builds TessynDesktop.WPF, copies the output to [Publish-WPF] next to this script and starts it from there,
    so the build output folder is never locked by the running app.
.PARAMETER Configuration
    Build configuration (default Debug).
.PARAMETER NoStart
    Build and copy only.
.NOTES
    Run with: Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass; .\run-wpf.ps1
    A running instance started from [Publish-WPF] is stopped first. Never touches [Publish] (the Avalonia app).
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

Write-Host "Building $project ($Configuration)..."
dotnet build $project -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed; nothing was copied.' }

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
robocopy $output $publish /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

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
