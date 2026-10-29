@echo off
REM Downloads Forge and builds the engine + bridge. Double-click or run from a terminal.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
pause
