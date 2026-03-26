@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

set "ROOT=%SCRIPT_DIR%"
if not exist "%ROOT%\apps\api\src\NSFinance.Api" (
  set "ROOT=%SCRIPT_DIR%\.."
)

set "API_PATH=%ROOT%\apps\api\src\NSFinance.Api"
if not exist "%API_PATH%" (
  echo [ERROR] Could not find NSFinance API folder.
  echo Checked: "%API_PATH%"
  pause
  exit /b 1
)

title NSFinance API
cd /d "%API_PATH%"
set ASPNETCORE_ENVIRONMENT=Development
set NSFINANCE_ALLOW_REMOTE_DB_IN_DEVELOPMENT=false
set NSFINANCE_DB_CONNECTION_STRING=
set NSFINTECH_DB_CONNECTION_STRING=
set ConnectionStrings__DefaultConnection=
dotnet run --urls http://0.0.0.0:5080
endlocal
