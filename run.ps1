# TessynDesktop launcher
# Starts the app, then rebuilds when it closes so changes are ready for the next run.

$projectRoot = $PSScriptRoot
$exe = Join-Path $projectRoot "code\TessynDesktop\bin\Debug\net10.0-windows10.0.17763.0\TessynDesktop.exe"
$sln = Join-Path $projectRoot "code\TessynDesktop.sln"

Write-Host "Starting TessynDesktop..." -ForegroundColor Cyan
Start-Process -FilePath $exe -Wait

Write-Host ""
Write-Host "App closed. Rebuilding..." -ForegroundColor Yellow
dotnet build $sln

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded. Ready for next run." -ForegroundColor Green
} else {
    Write-Host "Build FAILED. Fix errors before next run." -ForegroundColor Red
}
