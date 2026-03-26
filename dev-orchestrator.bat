@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%dev-orchestrator.ps1"

if not exist "%PS1%" (
  echo [ERROR] Could not find PowerShell script:
  echo         %PS1%
  echo Make sure dev-orchestrator.bat and dev-orchestrator.ps1 are in the same folder.
  pause
  exit /b 1
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
  echo.
  echo [ERROR] Orchestrator exited with code %EXITCODE%.
  pause
)
exit /b %EXITCODE%
