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
  echo Run 00_SET_BLENDER_PATH_WINDOWS.bat again.
  pause
  exit /b 1
)

"%BLENDER%" --version >nul 2>&1
set "BLENDER_VERSION_EXIT=%ERRORLEVEL%"
if not "%BLENDER_VERSION_EXIT%"=="0" (
  echo ERROR: Blender exists but could not be started:
  echo "%BLENDER%"
  pause
  exit /b 1
)

"%BLENDER%" --background --factory-startup --python-exit-code 1 --python-expr "import bpy; assert bpy.app.version[:2] >= (5, 2), 'Blender 5.2 or newer is required'" >nul 2>&1
if errorlevel 1 (
  echo ERROR: Blender 5.2 or newer is required. No project files were changed.
  pause
  exit /b 1
)

echo.
echo Using Blender:
echo "%BLENDER%"
echo.

copy /Y "source\powersuit_source.blend" "powersuit_pipeline.blend" >nul
set "COPY_EXIT=%ERRORLEVEL%"
if not "%COPY_EXIT%"=="0" (
  echo ERROR: Could not reset powersuit_pipeline.blend from the audited source.
  pause
  exit /b 1
)

if exist "renders" rmdir /S /Q "renders"

echo --------------------------------------------------
echo Running scripts\run_build_and_render_pipeline.py
"%BLENDER%" --background "powersuit_pipeline.blend" --python-exit-code 1 --python "scripts\run_build_and_render_pipeline.py"
set "RAW_BLENDER_EXIT=%ERRORLEVEL%"
if not "%RAW_BLENDER_EXIT%"=="0" (
  echo.
  echo Pipeline stopped because Blender failed.
  echo Blender native status: %RAW_BLENDER_EXIT%
  echo Read the first ERROR or Traceback above.
  echo To change Blender, run 00_SET_BLENDER_PATH_WINDOWS.bat.
  pause
  exit /b 1
)

if not exist "renders\validation_report.json" (
  echo ERROR: Blender returned success but renders\validation_report.json is missing.
  pause
  exit /b 1
)

echo.
echo Build and validation renders completed.
echo Inspect all PNG files in renders\aim_validation, renders\rifle_validation,
echo and renders\weapon_animation_validation.
echo Do not run approval/export until the images are genuinely acceptable.
pause
exit /b 0
