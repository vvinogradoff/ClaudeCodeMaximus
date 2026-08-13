# ClaudeMaximus launcher
# Starts the app, then rebuilds when it closes so changes are ready for the next run.

$projectRoot = $PSScriptRoot
$exe = Join-Path $projectRoot "code\ClaudeMaximus\bin\Debug\net9.0\ClaudeMaximus.exe"
$sln = Join-Path $projectRoot "code\ClaudeMaximus.sln"

Write-Host "Starting ClaudeMaximus..." -ForegroundColor Cyan
Start-Process -FilePath $exe -Wait

Write-Host ""
Write-Host "App closed. Rebuilding..." -ForegroundColor Yellow
dotnet build $sln

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded. Ready for next run." -ForegroundColor Green
} else {
    Write-Host "Build FAILED. Fix errors before next run." -ForegroundColor Red
}
