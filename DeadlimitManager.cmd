@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "MANAGER_SHORTCUT=%ROOT%Deadlimit Manager.lnk"
set "UPDATER_SHORTCUT=%ROOT%Deadlimit Updater.lnk"
set "MANAGER_ICON=%ROOT%internal\assets\DeadlimitManager_128_v4.ico"
set "UPDATER_ICON=%ROOT%internal\assets\DeadlimitUpdater_128_v4.ico"
set "PROJECT=%ROOT%internal\src\Deadlimit\Deadlimit.csproj"
set "APP=%ROOT%internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.exe"
set "UPDATER=%ROOT%DeadlimitUpdater.bat"

if not exist "%MANAGER_ICON%" (
    echo ERROR: Deadlimit Manager icon not found.
    pause
    exit /b 1
)
if not exist "%UPDATER_ICON%" (
    echo ERROR: Deadlimit Updater icon not found.
    pause
    exit /b 1
)
if not exist "%PROJECT%" (
    echo ERROR: Deadlimit Manager project file not found.
    pause
    exit /b 1
)
if not exist "%UPDATER%" (
    echo ERROR: Deadlimit Updater entry point not found.
    pause
    exit /b 1
)

echo Building Deadlimit Manager Release executable...
dotnet build "%PROJECT%" -c Release --nologo --verbosity minimal
if errorlevel 1 (
    echo ERROR: Deadlimit Manager Release build failed.
    pause
    exit /b 1
)
if not exist "%APP%" (
    echo ERROR: Deadlimit Manager executable was not produced:
    echo %APP%
    pause
    exit /b 1
)

rem Remove every historical user-facing shortcut name before recreating the two supported shortcuts.
for %%F in (
    "Deadlimit.lnk"
    "Updater.lnk"
    "Deadlimit Updater.lnk"
    "Deadlimit Manager.lnk"
) do if exist "%ROOT%%%~F" del /f /q "%ROOT%%%~F" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$m = $w.CreateShortcut('%MANAGER_SHORTCUT%');" ^
  "$m.TargetPath = '%APP%';" ^
  "$m.Arguments = '';" ^
  "$m.WorkingDirectory = '%ROOT%';" ^
  "$m.IconLocation = '%MANAGER_ICON%,0';" ^
  "$m.Description = 'Deadlimit Manager';" ^
  "$m.Save();" ^
  "$u = $w.CreateShortcut('%UPDATER_SHORTCUT%');" ^
  "$u.TargetPath = '%UPDATER%';" ^
  "$u.Arguments = '';" ^
  "$u.WorkingDirectory = '%ROOT%';" ^
  "$u.IconLocation = '%UPDATER_ICON%,0';" ^
  "$u.Description = 'Deadlimit Updater';" ^
  "$u.Save();"
if errorlevel 1 (
    echo ERROR: Could not create Deadlimit shortcuts.
    pause
    exit /b 1
)

rem Keep the checkout complete, but restore the original two-shortcut Explorer presentation.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$root = [IO.Path]::GetFullPath('%ROOT%');" ^
  "$keep = @('Deadlimit Manager.lnk','Deadlimit Updater.lnk');" ^
  "Get-ChildItem -LiteralPath $root -Force | Where-Object { $keep -notcontains $_.Name } | ForEach-Object {" ^
  "  try { $_.Attributes = $_.Attributes -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System } catch {}" ^
  "};" ^
  "foreach ($name in $keep) {" ^
  "  $path = Join-Path $root $name;" ^
  "  if (Test-Path -LiteralPath $path) {" ^
  "    $item = Get-Item -LiteralPath $path -Force;" ^
  "    $item.Attributes = $item.Attributes -band (-bnot ([IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System))" ^
  "  }" ^
  "}"

ie4uinit.exe -ClearIconCache >nul 2>nul
ie4uinit.exe -show >nul 2>nul

if /I "%~1"=="--refresh-only" exit /b 0

start "" "%APP%"
exit /b 0
