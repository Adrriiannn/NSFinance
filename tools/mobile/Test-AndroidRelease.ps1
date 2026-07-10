[CmdletBinding()]
param(
    [switch]$SkipQualityChecks,
    [switch]$CheckEasProject
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$mobileRoot = Join-Path $repoRoot "apps\mobile"
$easJsonPath = Join-Path $mobileRoot "eas.json"
$appJsonPath = Join-Path $mobileRoot "app.json"
$gradlePropertiesPath = Join-Path $mobileRoot "android\gradle.properties"
$androidStringsPath = Join-Path $mobileRoot "android\app\src\main\res\values\strings.xml"
$androidColorsPath = Join-Path $mobileRoot "android\app\src\main\res\values\colors.xml"
$androidManifestPath = Join-Path $mobileRoot "android\app\src\main\AndroidManifest.xml"
$androidBuildGradlePath = Join-Path $mobileRoot "android\app\build.gradle"
$androidSettingsGradlePath = Join-Path $mobileRoot "android\settings.gradle"

function Assert-Equal {
    param(
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Actual -ne $Expected) {
        throw "$Label must be '$Expected', but is '$Actual'."
    }
}

function Assert-NotBlank {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$Label must be configured."
    }
}

function Resolve-NativeCommand {
    param([Parameter(Mandatory)][string]$Name)

    $candidate = if ($IsWindows) { "$Name.cmd" } else { $Name }
    $command = Get-Command $candidate -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Required command '$Name' is not available on PATH."
    }

    return $command.Source
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Label,
        [switch]$DiscardOutput
    )

    Push-Location $WorkingDirectory
    try {
        if ($DiscardOutput) {
            & $Command @Arguments | Out-Null
        }
        else {
            & $Command @Arguments
        }
        if ($LASTEXITCODE -ne 0) {
            throw "$Label failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$easConfig = Get-Content -Raw $easJsonPath | ConvertFrom-Json
$profileNames = @($easConfig.build.PSObject.Properties.Name)
Assert-Equal $profileNames.Count 1 "EAS build profile count"
Assert-Equal $profileNames[0] "production" "EAS build profile"

$productionProfile = $easConfig.build.production
Assert-Equal $productionProfile.credentialsSource "remote" "EAS Android credential source"
Assert-Equal $productionProfile.channel "production" "EAS channel"
Assert-Equal $productionProfile.android.buildType "apk" "Android artifact type"
Assert-Equal $productionProfile.env.EXPO_PUBLIC_API_BASE_URL "https://api.finance.nsireland.ie" "Public API URL"
Assert-NotBlank $productionProfile.env.EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID "Google web client ID"
Assert-NotBlank $productionProfile.env.EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD "Google Android production client ID"

$appConfig = Get-Content -Raw $appJsonPath | ConvertFrom-Json
Assert-Equal $appConfig.expo.android.package "com.nsfinance.mobile" "Android application ID"
Assert-Equal $appConfig.expo.updates.url "https://u.expo.dev/21986a2d-cbfa-4757-bf6d-04eb6aa4f197" "EAS Update URL"

$imagePickerPlugin = @($appConfig.expo.plugins) |
    Where-Object { $_ -is [array] -and $_.Count -ge 2 -and $_[0] -eq "expo-image-picker" } |
    Select-Object -First 1
if (-not $imagePickerPlugin) {
    throw "Expo ImagePicker must be configured explicitly for production permissions."
}

Assert-Equal ([bool]$imagePickerPlugin[1].cameraPermission) $false "ImagePicker camera permission"
Assert-Equal ([bool]$imagePickerPlugin[1].microphonePermission) $false "ImagePicker microphone permission"
if ("android.permission.SYSTEM_ALERT_WINDOW" -notin @($appConfig.expo.android.blockedPermissions)) {
    throw "The production Android config must block SYSTEM_ALERT_WINDOW."
}

if ($appConfig.expo.android.runtimeVersion -isnot [string]) {
    throw "Android runtimeVersion must be an explicit string until the native update resources are generated from app config."
}

[xml]$nativeStrings = Get-Content -Raw $androidStringsPath
$runtimeNode = @($nativeStrings.resources.string) | Where-Object { $_.name -eq "expo_runtime_version" } | Select-Object -First 1
if (-not $runtimeNode) {
    throw "Android native resources do not define expo_runtime_version."
}

Assert-Equal $runtimeNode.InnerText $appConfig.expo.android.runtimeVersion "Android native runtime version"

$appNameNode = @($nativeStrings.resources.string) | Where-Object { $_.name -eq "app_name" } | Select-Object -First 1
if (-not $appNameNode) {
    throw "Android native resources do not define app_name."
}

Assert-Equal $appNameNode.InnerText $appConfig.expo.name "Android native app name"

[xml]$nativeColors = Get-Content -Raw $androidColorsPath
$activityBackgroundNode = @($nativeColors.resources.color) |
    Where-Object { $_.name -eq "activityBackground" } |
    Select-Object -First 1
if (-not $activityBackgroundNode) {
    throw "Android native resources do not define activityBackground."
}

Assert-Equal $activityBackgroundNode.InnerText $appConfig.expo.backgroundColor "Android native background color"

$manifest = Get-Content -Raw $androidManifestPath
if (-not $manifest.Contains($appConfig.expo.updates.url)) {
    throw "AndroidManifest.xml does not contain the configured EAS Update URL."
}

foreach ($requiredManifestValue in @(
    'android.permission.ACCESS_COARSE_LOCATION',
    'android.permission.ACCESS_FINE_LOCATION',
    'android:name="android.permission.CAMERA" tools:node="remove"',
    'android:name="android.permission.RECORD_AUDIO" tools:node="remove"',
    'android:name="android.permission.SYSTEM_ALERT_WINDOW" tools:node="remove"',
    'android:screenOrientation="portrait"',
    'locale|layoutDirection',
    'android:scheme="nsfinance"',
    'android:scheme="com.nsfinance.mobile"'
)) {
    if (-not $manifest.Contains($requiredManifestValue)) {
        throw "AndroidManifest.xml is missing required production value '$requiredManifestValue'."
    }
}

$gradleProperties = Get-Content -Raw $gradlePropertiesPath
$newArchitectureMatch = [regex]::Match($gradleProperties, "(?m)^newArchEnabled=(true|false)$")
if (-not $newArchitectureMatch.Success) {
    throw "android/gradle.properties does not define newArchEnabled."
}

$nativeNewArchitecture = [bool]::Parse($newArchitectureMatch.Groups[1].Value)
Assert-Equal $nativeNewArchitecture ([bool]$appConfig.expo.newArchEnabled) "Android new architecture setting"

if ($gradleProperties.Contains("EX_DEV_CLIENT_NETWORK_INSPECTOR")) {
    throw "Android Gradle properties still enable the unused network inspector."
}

$settingsGradle = Get-Content -Raw $androidSettingsGradlePath
if (-not $settingsGradle.Contains("rootProject.name = 'NSFinance'")) {
    throw "Android settings.gradle does not use the production project name."
}

$appBuildGradle = Get-Content -Raw $androidBuildGradlePath
if ($appBuildGradle.Contains("checkReleaseBuilds false") -or $appBuildGradle.Contains("abortOnError false")) {
    throw "Android release lint is disabled in app/build.gradle."
}

if ($appBuildGradle -match '(?s)release\s*\{.*?signingConfig\s+signingConfigs\.debug') {
    throw "Android release builds must not use the debug signing configuration."
}

if (-not $SkipQualityChecks) {
    $pnpm = Resolve-NativeCommand "pnpm"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "typecheck") $repoRoot "Mobile type-check"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "lint") $repoRoot "Mobile lint"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "test:node") $repoRoot "Mobile Node tests"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "expo:check") $repoRoot "Expo SDK compatibility"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "expo:doctor") $repoRoot "Expo Doctor"
    Invoke-CheckedCommand $pnpm @("--filter", "@nsfinance/mobile", "exec", "expo", "config", "--type", "public") $repoRoot "Expo public config resolution"
}

if ($CheckEasProject) {
    $eas = Resolve-NativeCommand "eas"
    Invoke-CheckedCommand $eas @("whoami") $mobileRoot "EAS authentication"
    Invoke-CheckedCommand $eas @("config", "--platform", "android", "--profile", "production", "--non-interactive", "--json") $mobileRoot "EAS project resolution" -DiscardOutput
}

if (-not $SkipQualityChecks) {
    & (Join-Path $PSScriptRoot "Test-AndroidTooling.ps1")
}

Write-Host "Android production release checks passed."
