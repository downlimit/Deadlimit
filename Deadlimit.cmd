@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\Deadlimit -- doctor
echo.
pause
