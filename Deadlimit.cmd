@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit.lnk"
set "ICON=%ROOT%internal\assets\Deadlimit.ico"
set "TARGET=%ROOT%internal\Deadlimit.cmd"

if not exist "%TARGET%" (
    echo ERROR: internal launcher not found.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$s = $w.CreateShortcut('%SHORTCUT%');" ^
  "$s.TargetPath = '%TARGET%';" ^
  "$s.WorkingDirectory = '%ROOT%';" ^
  "$s.IconLocation = '%ICON%,0';" ^
  "$s.Description = 'Deadlimit';" ^
  "$s.Save()"

ie4uinit.exe -show >nul 2>nul
call "%TARGET%"
