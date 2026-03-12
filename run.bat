@echo off
setlocal

set EXE=%~dp0code\ClaudeMaximus\bin\Debug\net9.0\ClaudeMaximus.exe
set SLN=%~dp0code\ClaudeMaximus.sln

echo Starting ClaudeMaximus...
start /wait "" "%EXE%"

echo.
echo App closed. Rebuilding...
dotnet build "%SLN%"

if %ERRORLEVEL% equ 0 (
    echo Build succeeded. Ready for next run.
) else (
    echo Build FAILED. Fix errors before next run.
)
