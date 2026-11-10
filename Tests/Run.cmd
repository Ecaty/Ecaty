@echo off
setlocal
set "WORLD_TEST_SHELL=pwsh"
where pwsh >nul 2>nul
if errorlevel 1 set "WORLD_TEST_SHELL=powershell"
"%WORLD_TEST_SHELL%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -Suite "%~1"
set "WORLD_TEST_EXIT=%errorlevel%"
if not defined WORLD_TEST_NO_OPEN if exist "%~dp0Results\Latest.html" start "" "%~dp0Results\Latest.html"
if not "%WORLD_TEST_EXIT%"=="0" (
  echo.
  echo Tests failed. Open Tests\Results\Latest.html for the error and stage results.
  if not defined WORLD_TEST_NO_PAUSE pause
)
exit /b %WORLD_TEST_EXIT%
