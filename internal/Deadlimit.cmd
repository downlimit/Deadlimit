@echo off
setlocal EnableExtensions
cd /d "%~dp0.."
dotnet run --project internal\src\Deadlimit -- doctor
echo.
pause
