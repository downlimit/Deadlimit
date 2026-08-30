@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "LAUNCHER=%ROOT%DeadlimitManager.cmd"
set "APP=%ROOT%internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.exe"

where git >nul 2>nul
if errorlevel 1 (
    echo ERROR: Git was not found. Deadlimit Updater cannot update this checkout.
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

echo Updating the Deadlimit repository from origin/main...
git -C "%ROOT%" fetch origin main
if errorlevel 1 (
    echo ERROR: Could not fetch origin/main.
    pause
    exit /b 1
)

git -C "%ROOT%" merge --ff-only origin/main
if errorlevel 1 (
    echo ERROR: The local checkout cannot be fast-forwarded safely.
    echo Resolve local Git changes before running Deadlimit Updater again.
    pause
    exit /b 1
)

if not exist "%LAUNCHER%" (
    echo ERROR: DeadlimitManager.cmd was not found after update.
    pause
    exit /b 1
)

call "%LAUNCHER%" --refresh-only
if errorlevel 1 exit /b %errorlevel%

if exist "%APP%" start "" "%APP%"
exit /b 0
