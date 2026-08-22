@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit.lnk"
set "ICON=%ROOT%assets\Deadlimit.ico"
set "TARGET=%ROOT%Deadlimit.cmd"

if not exist "%SHORTCUT%" if exist "%ICON%" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$w = New-Object -ComObject WScript.Shell;" ^
      "$s = $w.CreateShortcut('%SHORTCUT%');" ^
      "$s.TargetPath = '%TARGET%';" ^
      "$s.WorkingDirectory = '%ROOT%';" ^
      "$s.IconLocation = '%ICON%,0';" ^
      "$s.Description = 'Deadlimit';" ^
      "$s.Save()"

    if exist "%SHORTCUT%" attrib +h "%TARGET%" >nul 2>nul
)

dotnet run --project src\Deadlimit -- doctor
echo.
pause
