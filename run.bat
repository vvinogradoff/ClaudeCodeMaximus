@echo off
setlocal

set EXE=%~dp0code\TessynDesktop\bin\Debug\net9.0\TessynDesktop.exe
set SLN=%~dp0code\TessynDesktop.sln

echo Starting TessynDesktop...
start /wait "" "%EXE%"

echo.
echo App closed. Rebuilding...
dotnet build "%SLN%"

if %ERRORLEVEL% equ 0 (
    echo Build succeeded. Ready for next run.
) else (
    echo Build FAILED. Fix errors before next run.
)
