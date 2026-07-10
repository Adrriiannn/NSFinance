[CmdletBinding()]
param(
    [string]$ApkPath,
    [string]$DeviceSerial,
    [switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $latestApk = Get-ChildItem (Join-Path $repoRoot "local-builds") -Filter "*.apk" -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $latestApk) {
        throw "No local APK was found. Supply -ApkPath or run the Android release build first."
    }

    $ApkPath = $latestApk.FullName
}

$resolvedApk = (Resolve-Path $ApkPath).Path
if (-not $resolvedApk.EndsWith(".apk", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The selected artifact is not an APK."
}

$adb = Get-Command "adb.exe" -ErrorAction SilentlyContinue
if (-not $adb) {
    $adb = Get-Command "adb" -ErrorAction SilentlyContinue
}

if (-not $adb -and $env:LOCALAPPDATA) {
    $sdkAdb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
    if (Test-Path $sdkAdb) {
        $adb = Get-Item $sdkAdb
    }
}

if (-not $adb) {
    throw "ADB is not available. Install Android Platform Tools or add adb to PATH."
}

$devices = @(
    @(& $adb.Source devices) |
        Where-Object { $_ -match "^(\S+)\s+device$" } |
        ForEach-Object { $Matches[1] }
)

if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
    if ($devices.Count -ne 1) {
        throw "Connect and authorize exactly one Android device, or supply -DeviceSerial."
    }

    $DeviceSerial = $devices[0]
}
elseif ($DeviceSerial -notin $devices) {
    throw "The requested Android device is not connected and authorized."
}

Write-Host "Installing $resolvedApk"
& $adb.Source -s $DeviceSerial install -r $resolvedApk
if ($LASTEXITCODE -ne 0) {
    throw "ADB failed to install the APK."
}

& $adb.Source -s $DeviceSerial shell pm path com.nsfinance.mobile
if ($LASTEXITCODE -ne 0) {
    throw "The NSFinance package was not found after installation."
}

if ($Launch) {
    & $adb.Source -s $DeviceSerial shell monkey -p com.nsfinance.mobile -c android.intent.category.LAUNCHER 1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The APK installed, but ADB could not launch NSFinance."
    }
}

Write-Host "NSFinance is installed on the selected Android device."
