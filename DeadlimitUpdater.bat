@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "WORKER=%ROOT%internal\DeadlimitUpdater.ps1"

if not exist "%WORKER%" (
    echo ERROR: Deadlimit Updater worker was not found:
    echo %WORKER%
    pause
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WORKER%" -Root "%ROOT%"
exit /b %ERRORLEVEL%
