@echo off
setlocal

REM ------------------------------------------------------------
REM NSFinance local Android preview build via WSL
REM ------------------------------------------------------------

set "PROJECT_WIN=%USERPROFILE%\Desktop\Projects\NSFinance"
set "MOBILE_WIN=%PROJECT_WIN%\apps\mobile"
set "OUTPUT_DIR_WIN=%PROJECT_WIN%\local-builds\android"

set "PROJECT_WSL=/mnt/c/Users/%USERNAME%/Desktop/Projects/NSFinance"
set "MOBILE_WSL=%PROJECT_WSL%/apps/mobile"

if not exist "%MOBILE_WIN%" (
    echo [ERROR] Mobile project folder not found:
    echo %MOBILE_WIN%
    exit /b 1
)

if not exist "%OUTPUT_DIR_WIN%" (
    mkdir "%OUTPUT_DIR_WIN%"
)

echo.
echo ============================================
echo Building NSFinance Android preview locally...
echo ============================================
echo.

wsl.exe bash -lc "source ~/.bashrc && cd \"%MOBILE_WSL%\" && npx eas-cli@latest build --platform android --profile preview --local --output ../../local-builds/android/nsfinance-preview-\$(date +%%Y-%%m-%%d-%%H%%M).apk"

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
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