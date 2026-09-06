@echo off
setlocal EnableExtensions

pushd "%~dp0" >nul 2>&1
if errorlevel 1 (
    echo ERROR: Deadlimit Updater could not resolve its repository folder.
    pause
    exit /b 1
)
set "ROOT=%CD%"
popd
set "MANAGER_EXE=%ROOT%\internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.exe"
set "RESTART_MANAGER=0"

tasklist.exe /FI "IMAGENAME eq DeadlimitManager.exe" /NH 2>nul | findstr.exe /I /C:"DeadlimitManager.exe" >nul
if not errorlevel 1 set "RESTART_MANAGER=1"

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
if "%RESTART_MANAGER%"=="1" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WORKER%" -NoWait %*
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%WORKER%" %*
)
set "EXIT_CODE=%ERRORLEVEL%"

del /q "%WORKER%" >nul 2>&1

if "%EXIT_CODE%"=="0" if "%RESTART_MANAGER%"=="1" (
    if exist "%MANAGER_EXE%" (
        start "" "%MANAGER_EXE%"
    ) else (
        echo ERROR: Deadlimit Manager executable was not found after the update:
        echo %MANAGER_EXE%
        set "EXIT_CODE=1"
    )
)

exit /b %EXIT_CODE%
