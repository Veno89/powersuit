@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"

set "BLENDER="
if defined BLENDER_EXE set "BLENDER=%BLENDER_EXE%"
if defined BLENDER set "BLENDER=%BLENDER:"=%"
if defined BLENDER if not exist "%BLENDER%" set "BLENDER="

if not defined BLENDER if exist "blender_path.txt" set /p "BLENDER=" < "blender_path.txt"
if defined BLENDER set "BLENDER=%BLENDER:"=%"
if defined BLENDER if not exist "%BLENDER%" set "BLENDER="

if not defined BLENDER if exist "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" set "BLENDER=C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

if not defined BLENDER for /f "delims=" %%I in ('where blender.exe 2^>nul') do if not defined BLENDER set "BLENDER=%%I"

if not defined BLENDER call "00_SET_BLENDER_PATH_WINDOWS.bat"
if not defined BLENDER if exist "blender_path.txt" set /p "BLENDER=" < "blender_path.txt"
if defined BLENDER set "BLENDER=%BLENDER:"=%"

if not defined BLENDER (
  echo ERROR: Blender was not found.
  echo Run 00_SET_BLENDER_PATH_WINDOWS.bat and select blender.exe.
  pause
  exit /b 1
)

if not exist "%BLENDER%" (
  echo ERROR: Blender path does not exist:
  echo "%BLENDER%"
  pause
  exit /b 1
)

"%BLENDER%" --background --factory-startup --python-exit-code 1 --python-expr "import bpy; assert bpy.app.version[:2] >= (5, 2), 'Blender 5.2 or newer is required'" >nul 2>&1
if errorlevel 1 (
  echo ERROR: Blender 5.2 or newer is required.
  pause
  exit /b 1
)

if not exist "powersuit_pipeline.blend" (
  echo ERROR: powersuit_pipeline.blend is missing.
  echo Run 01_BUILD_AND_RENDER_WINDOWS.bat first.
  pause
  exit /b 1
)

if not exist "renders\validation_report.json" (
  echo ERROR: renders\validation_report.json is missing.
  echo Run and inspect the validation pipeline first.
  pause
  exit /b 1
)

set "CONFIRM="
set /p "CONFIRM=After inspecting all 32 validation PNGs, type APPROVE to export: "
if /I not "%CONFIRM%"=="APPROVE" (
  echo Approval cancelled. No FBX was exported.
  pause
  exit /b 1
)

echo --------------------------------------------------
echo Running scripts\approve_validation.py
"%BLENDER%" --background "powersuit_pipeline.blend" --python-exit-code 1 --python "scripts\approve_validation.py" -- --approve
set "APPROVE_EXIT=%ERRORLEVEL%"
if not "%APPROVE_EXIT%"=="0" (
  echo ERROR: Validation approval failed with Blender status %APPROVE_EXIT%.
  pause
  exit /b 1
)

echo --------------------------------------------------
echo Running scripts\export_powersuit_with_aim.py
"%BLENDER%" --background "powersuit_pipeline.blend" --python-exit-code 1 --python "scripts\export_powersuit_with_aim.py"
set "EXPORT_EXIT=%ERRORLEVEL%"
if not "%EXPORT_EXIT%"=="0" (
  echo ERROR: FBX export failed with Blender status %EXPORT_EXIT%.
  pause
  exit /b 1
)

echo.
echo Export completed: exports\powersuit_animated_with_aim.fbx
pause
exit /b 0
