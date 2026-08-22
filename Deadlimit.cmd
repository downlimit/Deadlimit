@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "ROOT=%~dp0"
set "SHORTCUT=%ROOT%Deadlimit.lnk"
set "ICON=%ROOT%assets\Deadlimit.ico"
set "TARGET=%ROOT%Deadlimit.cmd"

rem Always recreate the shortcut so icon changes are applied.
if exist "%SHORTCUT%" del /q "%SHORTCUT%" >nul 2>nul

if exist "%ICON%" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$w = New-Object -ComObject WScript.Shell;" ^
      "$s = $w.CreateShortcut('%SHORTCUT%');" ^
      "$s.TargetPath = '%TARGET%';" ^
      "$s.WorkingDirectory = '%ROOT%';" ^
      "$s.IconLocation = '%ICON%,0';" ^
      "$s.Description = 'Deadlimit';" ^
      "$s.Save()"
)

if exist "%SHORTCUT%" attrib +h "%TARGET%" >nul 2>nul

rem Ask Explorer to refresh icon resources after shortcut recreation.
ie4uinit.exe -show >nul 2>nul

dotnet run --project src\Deadlimit -- doctor
echo.
pause
