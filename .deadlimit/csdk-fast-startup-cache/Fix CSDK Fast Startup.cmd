@echo off
setlocal EnableExtensions DisableDelayedExpansion

chcp 65001 >nul
title Deadlimit - Fix CSDK Fast Startup

set "TOOL_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TOOL_DIR%Fix-CsdkFastStartup.ps1"
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
    echo CSDK fast-startup cache is ready.
) else (
    echo CSDK fast-startup cache repair failed.
)
echo.
pause
exit /b %RESULT%
