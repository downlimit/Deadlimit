@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit.lnk"
set "UPDATER_SHORTCUT=%ROOT%Updater.lnk"
set "OLD_UPDATER_SHORTCUT=%ROOT%Deadlimit Updater.lnk"
set "ICON=%ROOT%internal\assets\Deadlimit_128.ico"
set "UPDATER_ICON=%ROOT%internal\assets\Updater_128.ico"
set "TARGET=%ROOT%internal\Deadlimit.cmd"
set "UPDATER=%ROOT%DEADLIMIT_LocalUpdater.bat"

if not exist "%TARGET%" (
    echo ERROR: internal launcher not found.
    pause
    exit /b 1
)

if exist "%OLD_UPDATER_SHORTCUT%" del /f /q "%OLD_UPDATER_SHORTCUT%" >nul 2>nul
if exist "%SHORTCUT%" del /f /q "%SHORTCUT%" >nul 2>nul
if exist "%UPDATER_SHORTCUT%" del /f /q "%UPDATER_SHORTCUT%" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$s = $w.CreateShortcut('%SHORTCUT%');" ^
  "$s.TargetPath = '%TARGET%';" ^
  "$s.WorkingDirectory = '%ROOT%';" ^
  "$s.IconLocation = '%ICON%,0';" ^
  "$s.Description = 'Deadlimit';" ^
  "$s.Save();" ^
  "if (Test-Path -LiteralPath '%UPDATER%') {" ^
  "  $u = $w.CreateShortcut('%UPDATER_SHORTCUT%');" ^
  "  $u.TargetPath = '%UPDATER%';" ^
  "  $u.WorkingDirectory = '%ROOT%';" ^
  "  $u.IconLocation = '%UPDATER_ICON%,0';" ^
  "  $u.Description = 'Updater';" ^
  "  $u.Save();" ^
  "}"

attrib +h +s "%ROOT%internal" >nul 2>nul
attrib +h +s "%ROOT%.git" >nul 2>nul
attrib +h +s "%ROOT%Deadlimit.cmd" >nul 2>nul
if exist "%UPDATER%" attrib +h +s "%UPDATER%" >nul 2>nul

ie4uinit.exe -ClearIconCache >nul 2>nul
ie4uinit.exe -show >nul 2>nul

call "%TARGET%"
