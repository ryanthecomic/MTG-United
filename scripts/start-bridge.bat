@echo off
REM Starts the Forge bridge (the Unity client can also start it for you).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-bridge.ps1" %*
pause
