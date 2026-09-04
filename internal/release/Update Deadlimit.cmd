@echo off
setlocal EnableExtensions
set "DEADLIMIT_PORTABLE_ROOT=%~dp0"
set "DEADLIMIT_PORTABLE_ROOT=%DEADLIMIT_PORTABLE_ROOT:~0,-1%"
set "DEADLIMIT_PORTABLE_WORKER=%~dp0internal\DeadlimitPortableUpdater.ps1"

if not exist "%DEADLIMIT_PORTABLE_WORKER%" (
    echo ERROR: Deadlimit portable updater worker was not found:
    echo %DEADLIMIT_PORTABLE_WORKER%
    pause
    exit /b 1
)

pushd "%TEMP%" >nul 2>&1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DEADLIMIT_PORTABLE_WORKER%" -InstallRoot "%DEADLIMIT_PORTABLE_ROOT%" %*
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul 2>&1

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%
