@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

set "ROOT=%SCRIPT_DIR%"
if not exist "%ROOT%\apps\mobile" (
  set "ROOT=%SCRIPT_DIR%\.."
)

set "MOBILE_PATH=%ROOT%\apps\mobile"
if not exist "%MOBILE_PATH%" (
  echo [ERROR] Could not find NSFinance mobile folder.
  echo Checked: "%MOBILE_PATH%"
  pause
  exit /b 1
)

title NSFinance Mobile
cd /d "%MOBILE_PATH%"
set EXPO_PUBLIC_API_BASE_URL=
pnpm exec expo start --go
endlocal
