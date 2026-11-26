@echo off
REM Double-click to rebuild. Uses -ExecutionPolicy Bypass for this run only.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
echo.
pause
