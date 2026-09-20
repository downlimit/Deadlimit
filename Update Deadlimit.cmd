@echo off
setlocal EnableExtensions
set "DEADLIMIT_ROOT=%~dp0"
set "DEADLIMIT_ROOT=%DEADLIMIT_ROOT:~0,-1%"

if not exist "%DEADLIMIT_ROOT%\.git" (
    echo ERROR: Deadlimit is not installed as a Git checkout:
    echo %DEADLIMIT_ROOT%
    echo Re-run Install-Deadlimit.cmd to migrate this installation.
    pause
    exit /b 1
)

rem Relaunch is opt-in and inherited only by updater processes started from Deadlimit Manager.
rem A manual updater launch (including the Deadlimit Updater shortcut) defaults to -NoLaunch.
set "DEADLIMIT_UPDATER_DEFAULT_ARGS="
set "DEADLIMIT_HAS_NO_LAUNCH="
for %%A in (%*) do if /I "%%~A"=="-NoLaunch" set "DEADLIMIT_HAS_NO_LAUNCH=1"
if /I not "%DEADLIMIT_UPDATE_RELAUNCH%"=="1" if not defined DEADLIMIT_HAS_NO_LAUNCH set "DEADLIMIT_UPDATER_DEFAULT_ARGS=-NoLaunch"

call "%DEADLIMIT_ROOT%\DeadlimitUpdater.bat" %* %DEADLIMIT_UPDATER_DEFAULT_ARGS%
exit /b %ERRORLEVEL%
