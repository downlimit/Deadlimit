@echo off
setlocal EnableExtensions
set "DEADLIMIT_PORTABLE_ROOT=%~dp0"
set "DEADLIMIT_PORTABLE_ROOT=%DEADLIMIT_PORTABLE_ROOT:~0,-1%"
set "DEADLIMIT_PORTABLE_SOURCE=%~dp0internal\DeadlimitPortableUpdater.ps1"
set "DEADLIMIT_PORTABLE_WORKER=%TEMP%\DeadlimitPortableUpdater-%RANDOM%-%RANDOM%.ps1"

if not exist "%DEADLIMIT_PORTABLE_SOURCE%" (
    echo ERROR: Deadlimit portable updater worker was not found:
    echo %DEADLIMIT_PORTABLE_SOURCE%
    pause
    exit /b 1
)

copy /y "%DEADLIMIT_PORTABLE_SOURCE%" "%DEADLIMIT_PORTABLE_WORKER%" >nul
if errorlevel 1 (
    echo ERROR: Could not prepare the portable updater worker.
    pause
    exit /b 1
)

pushd "%TEMP%" >nul 2>&1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DEADLIMIT_PORTABLE_WORKER%" -InstallRoot "%DEADLIMIT_PORTABLE_ROOT%" %*
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul 2>&1
del /q "%DEADLIMIT_PORTABLE_WORKER%" >nul 2>&1

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%
