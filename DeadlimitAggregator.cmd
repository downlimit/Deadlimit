@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit Aggregator.lnk"
set "UPDATER_SHORTCUT=%ROOT%Deadlimit Aggregator Updater.lnk"
set "LEGACY_SHORTCUT=%ROOT%Deadlimit.lnk"
set "LEGACY_UPDATER_SHORTCUT=%ROOT%Updater.lnk"
set "OLD_UPDATER_SHORTCUT=%ROOT%Deadlimit Updater.lnk"
set "ICON=%ROOT%internal\assets\DeadlimitAggregator_128_v4.ico"
set "UPDATER_ICON=%ROOT%internal\assets\DeadlimitAggregatorUpdater_128_v4.ico"
set "PROJECT=%ROOT%internal\src\Deadlimit\Deadlimit.csproj"
set "APP=%ROOT%internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitAggregator.exe"
set "UPDATER=%ROOT%DeadlimitAggregatorUpdater.bat"
set "LEGACY_UPDATER=%ROOT%DEADLIMIT_LocalUpdater.bat"

if not exist "%ICON%" (
    echo ERROR: Deadlimit Aggregator icon not found.
    pause
    exit /b 1
)
if not exist "%UPDATER_ICON%" (
    echo ERROR: Deadlimit Aggregator Updater icon not found.
    pause
    exit /b 1
)
if not exist "%PROJECT%" (
    echo ERROR: Deadlimit Aggregator project file not found.
    pause
    exit /b 1
)

echo Building Deadlimit Aggregator Release executable...
dotnet build "%PROJECT%" -c Release --nologo --verbosity minimal
if errorlevel 1 (
    echo ERROR: Deadlimit Aggregator Release build failed.
    pause
    exit /b 1
)
if not exist "%APP%" (
    echo ERROR: Deadlimit Aggregator executable was not produced:
    echo %APP%
    pause
    exit /b 1
)

if exist "%OLD_UPDATER_SHORTCUT%" del /f /q "%OLD_UPDATER_SHORTCUT%" >nul 2>nul
if exist "%LEGACY_SHORTCUT%" del /f /q "%LEGACY_SHORTCUT%" >nul 2>nul
if exist "%LEGACY_UPDATER_SHORTCUT%" del /f /q "%LEGACY_UPDATER_SHORTCUT%" >nul 2>nul
if exist "%SHORTCUT%" del /f /q "%SHORTCUT%" >nul 2>nul
if exist "%UPDATER_SHORTCUT%" del /f /q "%UPDATER_SHORTCUT%" >nul 2>nul

set "UPDATER_TARGET=%UPDATER%"
if not exist "%UPDATER_TARGET%" if exist "%LEGACY_UPDATER%" set "UPDATER_TARGET=%LEGACY_UPDATER%"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$s = $w.CreateShortcut('%SHORTCUT%');" ^
  "$s.TargetPath = '%APP%';" ^
  "$s.Arguments = '';" ^
  "$s.WorkingDirectory = '%ROOT%';" ^
  "$s.IconLocation = '%ICON%,0';" ^
  "$s.Description = 'Deadlimit Aggregator';" ^
  "$s.Save();" ^
  "if (Test-Path -LiteralPath '%UPDATER_TARGET%') {" ^
  "  $u = $w.CreateShortcut('%UPDATER_SHORTCUT%');" ^
  "  $u.TargetPath = '%UPDATER_TARGET%';" ^
  "  $u.WorkingDirectory = '%ROOT%';" ^
  "  $u.IconLocation = '%UPDATER_ICON%,0';" ^
  "  $u.Description = 'Deadlimit Aggregator Updater';" ^
  "  $u.Save();" ^
  "}"

attrib +h +s "%ROOT%internal" >nul 2>nul
attrib +h +s "%ROOT%.git" >nul 2>nul
if exist "%ROOT%.github" attrib +h +s "%ROOT%.github" >nul 2>nul
attrib +h +s "%ROOT%DeadlimitAggregator.cmd" >nul 2>nul
if exist "%ROOT%Deadlimit.cmd" attrib +h +s "%ROOT%Deadlimit.cmd" >nul 2>nul
if exist "%UPDATER%" attrib +h +s "%UPDATER%" >nul 2>nul
if exist "%LEGACY_UPDATER%" attrib +h +s "%LEGACY_UPDATER%" >nul 2>nul
if exist "%ROOT%src" attrib +h +s "%ROOT%src" >nul 2>nul
if exist "%ROOT%assets" attrib +h +s "%ROOT%assets" >nul 2>nul

ie4uinit.exe -ClearIconCache >nul 2>nul
ie4uinit.exe -show >nul 2>nul

if /I "%~1"=="--refresh-only" exit /b 0

start "" "%APP%"
exit /b 0
