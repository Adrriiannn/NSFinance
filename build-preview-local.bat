@echo off
setlocal

REM ------------------------------------------------------------
REM NSFinance local Android preview build via WSL Ubuntu
REM Place this BAT in the NSFinance repo root
REM ------------------------------------------------------------

set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

set "PROJECT_WIN=%SCRIPT_DIR%"
set "MOBILE_WIN=%PROJECT_WIN%\apps\mobile"
set "OUTPUT_DIR_WIN=%PROJECT_WIN%\local-builds\android"

if not exist "%MOBILE_WIN%" (
    echo [ERROR] Mobile project folder not found:
    echo %MOBILE_WIN%
    pause
    exit /b 1
)

if not exist "%OUTPUT_DIR_WIN%" (
    mkdir "%OUTPUT_DIR_WIN%"
)

for /f "usebackq delims=" %%I in (`wsl -d Ubuntu wslpath "%MOBILE_WIN%"`) do set "MOBILE_WSL=%%I"

if "%MOBILE_WSL%"=="" (
    echo [ERROR] Failed to convert Windows path to WSL path.
    pause
    exit /b 1
)

echo.
echo ============================================
echo Building NSFinance Android preview locally...
echo ============================================
echo.
echo Windows mobile path:
echo %MOBILE_WIN%
echo.
echo WSL mobile path:
echo %MOBILE_WSL%
echo.

wsl -d Ubuntu bash -lic "export NVM_DIR=\"$HOME/.nvm\"; [ -s \"$NVM_DIR/nvm.sh\" ] && . \"$NVM_DIR/nvm.sh\"; cd \"%MOBILE_WSL%\"; echo ==== Linux tool check ====; uname -a; command -v node; node -v; command -v npm; npm -v; command -v npx; npx --version; echo ==========================; npx eas-cli@latest build --platform android --profile preview --local --output ../../local-builds/android/nsfinance-preview-\$(date +%%Y-%%m-%%d-%%H%%M).apk"

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo Build finished successfully.
echo ============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"$dir = '%OUTPUT_DIR_WIN%';" ^
"$latest = Get-ChildItem -Path $dir -Filter *.apk | Sort-Object LastWriteTime -Descending | Select-Object -First 1;" ^
"if ($latest) { explorer.exe /select, $latest.FullName } else { explorer.exe $dir }"

endlocal
exit /b 0