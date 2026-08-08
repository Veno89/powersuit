@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"

echo ==================================================
echo  Powered Suit - Blender Path Setup
echo ==================================================
echo.
echo Find blender.exe in File Explorer.
echo You can drag blender.exe into this window, or paste its full path.
echo.
echo Typical location:
echo C:\Program Files\Blender Foundation\Blender 5.2\blender.exe
echo.

set "BLENDER_INPUT="
set /p "BLENDER_INPUT=Full path to blender.exe: "

if not defined BLENDER_INPUT goto :missing
set "BLENDER_INPUT=%BLENDER_INPUT:"=%"

if not exist "%BLENDER_INPUT%" (
  echo.
  echo ERROR: That file does not exist:
  echo %BLENDER_INPUT%
  goto :fail
)

for %%I in ("%BLENDER_INPUT%") do set "BLENDER_NAME=%%~nxI"
if /I not "%BLENDER_NAME%"=="blender.exe" (
  echo.
  echo ERROR: Please select the actual blender.exe file.
  echo Selected file: %BLENDER_NAME%
  goto :fail
)

"%BLENDER_INPUT%" --version >nul 2>&1
if errorlevel 1 (
  echo.
  echo ERROR: Blender could not be started from that path.
  goto :fail
)

> "blender_path.txt" echo %BLENDER_INPUT%

echo.
echo Blender path saved successfully:
echo %BLENDER_INPUT%
echo.
echo The build and export launchers will reuse this path automatically.
pause
exit /b 0

:missing
echo.
echo No path was entered.

:fail
echo.
echo Blender path was not saved.
pause
exit /b 1
