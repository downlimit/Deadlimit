@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"

where git.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: Git was not found. Deadlimit Updater cannot refresh itself.
    pause
    exit /b 1
)

git.exe -C "%ROOT%" rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo ERROR: This folder is not a Git checkout:
    echo %ROOT%
    pause
    exit /b 1
)

echo Loading the latest Deadlimit Updater from origin/main...
git.exe -C "%ROOT%" fetch origin main
if errorlevel 1 (
    echo ERROR: Could not fetch origin/main. The local checkout was not changed.
    pause
    exit /b 1
)

set "WORKER=%TEMP%\DeadlimitUpdater-%RANDOM%-%RANDOM%.ps1"
git.exe -C "%ROOT%" show origin/main:internal/DeadlimitUpdater.ps1 > "%WORKER%"
if errorlevel 1 (
    echo ERROR: Could not load the current updater worker from origin/main.
    del /q "%WORKER%" >nul 2>&1
    pause
    exit /b 1
)

rem Stable bootstrap contract: the downloaded worker resolves the real checkout
rem from this environment variable instead of its temporary script location.
set "DEADLIMIT_UPDATER_ROOT=%ROOT%"
set "DEADLIMIT_UPDATER_BOOTSTRAPPED=1"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WORKER%" %*
set "EXIT_CODE=%ERRORLEVEL%"

del /q "%WORKER%" >nul 2>&1
exit /b %EXIT_CODE%
