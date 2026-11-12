<#
.SYNOPSIS
    Builds the Tessyn Web SPA, publishes the server for linux-x64 (self-contained) and packs artifacts/tessyn-web.tar.gz.
.PARAMETER SkipClient
    Skip `npm ci` / `npm run build` (use when wwwroot is already fresh).
#>
param(
    [switch]$SkipClient
)

$ErrorActionPreference = 'Stop'

$webRoot = Split-Path -Parent $PSScriptRoot
$clientApp = Join-Path $webRoot 'ClientApp'
$artifacts = Join-Path $webRoot 'artifacts'
$publishDir = Join-Path $artifacts 'tessyn-web'
$archive = Join-Path $artifacts 'tessyn-web.tar.gz'
$project = Join-Path $webRoot 'Tessyn.Web.csproj'

if (-not $SkipClient) {
    Push-Location $clientApp
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path (Join-Path $webRoot 'wwwroot\index.html'))) {
    throw 'wwwroot/index.html is missing: build the client first (omit -SkipClient).'
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force $artifacts | Out-Null

dotnet publish $project -c Release -r linux-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

if (Test-Path $archive) { Remove-Item -Force $archive }
# Use Windows' own bsdtar: when this script is started from Git Bash, GNU tar comes first on PATH and reads "C:\..." as a remote host.
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
if (-not (Test-Path $tar)) { $tar = 'tar' }
& $tar -czf $archive -C $publishDir .
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }

Write-Host "Packed $archive"
