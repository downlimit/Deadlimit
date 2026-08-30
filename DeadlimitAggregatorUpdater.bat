@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "LEGACY_UPDATER=%ROOT%DEADLIMIT_LocalUpdater.bat"
set "LAUNCHER=%ROOT%DeadlimitAggregator.cmd"
set "APP=%ROOT%internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitAggregator.exe"

rem Preserve the existing workstation updater behavior during migration when it is present.
if not exist "%LEGACY_UPDATER%" goto :native_update
call "%LEGACY_UPDATER%" %*
exit /b %errorlevel%

:native_update
where git >nul 2>nul
if errorlevel 1 (
    echo ERROR: Git was not found. Deadlimit Aggregator Updater cannot update this checkout.
    pause
    exit /b 1
)

git -C "%ROOT%" rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo ERROR: This folder is not a Git checkout:
    echo %ROOT%
    pause
    exit /b 1
)

echo Updating Deadlimit Aggregator from origin/main...
git -C "%ROOT%" fetch origin main
if errorlevel 1 (
    echo ERROR: Could not fetch origin/main.
    pause
    exit /b 1
)

git -C "%ROOT%" merge --ff-only origin/main
if errorlevel 1 (
    echo ERROR: The local checkout cannot be fast-forwarded safely.
    echo Resolve local Git changes before running Deadlimit Aggregator Updater again.
    pause
    exit /b 1
)

if not exist "%LAUNCHER%" (
    echo ERROR: DeadlimitAggregator.cmd was not found after update.
    pause
    exit /b 1
)

call "%LAUNCHER%" --refresh-only
if errorlevel 1 exit /b %errorlevel%

if exist "%APP%" start "" "%APP%"
exit /b 0
