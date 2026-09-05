@echo off
setlocal EnableExtensions
set "DEADLIMIT_ROOT=%~dp0"
set "DEADLIMIT_ROOT=%DEADLIMIT_ROOT:~0,-1%"

if exist "%DEADLIMIT_ROOT%\.git" (
    call "%DEADLIMIT_ROOT%\DeadlimitUpdater.bat" %*
    exit /b
)

set "DEADLIMIT_PORTABLE_SOURCE=%DEADLIMIT_ROOT%\internal\DeadlimitPortableUpdater.ps1"
set "DEADLIMIT_PORTABLE_WORKER=%TEMP%\DeadlimitPortableUpdater-%RANDOM%-%RANDOM%.ps1"

if not exist "%DEADLIMIT_PORTABLE_SOURCE%" (
    echo ERROR: Deadlimit updater worker was not found:
    echo %DEADLIMIT_PORTABLE_SOURCE%
    pause
    exit /b 1
)

copy /y "%DEADLIMIT_PORTABLE_SOURCE%" "%DEADLIMIT_PORTABLE_WORKER%" >nul
if errorlevel 1 (
    echo ERROR: Could not prepare the Deadlimit updater worker.
    pause
    exit /b 1
)

pushd "%TEMP%" >nul 2>&1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DEADLIMIT_PORTABLE_WORKER%" -InstallRoot "%DEADLIMIT_ROOT%" %*
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul 2>&1
del /q "%DEADLIMIT_PORTABLE_WORKER%" >nul 2>&1

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%
