@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-stop.ps1"
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
  echo.
  echo [ERROR] Stop script exited with code %EXITCODE%.
  pause
)
endlocal & exit /b %EXITCODE%
