@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit.lnk"
set "UPDATER_SHORTCUT=%ROOT%Deadlimit Updater.lnk"
set "ICON=%ROOT%internal\assets\Deadlimit_v2.ico"
set "ICON_B64=%ROOT%internal\assets\Deadlimit.ico.b64"
set "TARGET=%ROOT%internal\Deadlimit.cmd"
set "UPDATER=%ROOT%DEADLIMIT_LocalUpdater.bat"

if not exist "%TARGET%" (
    echo ERROR: internal launcher not found.
    pause
    exit /b 1
)

if exist "%ICON_B64%" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$raw = Get-Content -Raw -LiteralPath '%ICON_B64%';" ^
      "[IO.File]::WriteAllBytes('%ICON%', [Convert]::FromBase64String($raw))"
)

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
  "  $u.IconLocation = '%ICON%,0';" ^
  "  $u.Description = 'Update Deadlimit';" ^
  "  $u.Save();" ^
  "}"

attrib +h +s "%ROOT%internal" >nul 2>nul
attrib +h +s "%ROOT%.git" >nul 2>nul
attrib +h +s "%ROOT%Deadlimit.cmd" >nul 2>nul
if exist "%UPDATER%" attrib +h +s "%UPDATER%" >nul 2>nul

ie4uinit.exe -ClearIconCache >nul 2>nul
ie4uinit.exe -show >nul 2>nul

call "%TARGET%"
