@echo off
rem Builds the Windows release (Builds\MTG United <version>.zip) without opening the editor.
rem Close the Unity editor first: Unity can't build a project that is already open.
setlocal
set UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe
if not exist "%UNITY%" (
  echo Unity 6000.6.7f1 not found at "%UNITY%". Edit this file to point at your Unity.exe.
  pause
  exit /b 1
)
cd /d "%~dp0.."
if not exist Builds mkdir Builds
echo Building MTG United... (a few minutes; the log goes to Builds\build.log)
"%UNITY%" -batchmode -quit -projectPath "%CD%\unity\MTGUnited" -executeMethod MTGUnited.EditorTools.BuildMenu.BuildFromCommandLine -logFile "%CD%\Builds\build.log"
if errorlevel 1 (
  echo The build failed. Look at Builds\build.log for the errors.
) else (
  echo Done. The zip is in the Builds folder.
  start "" "%CD%\Builds"
)
pause
