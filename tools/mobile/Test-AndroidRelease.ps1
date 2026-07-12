[CmdletBinding()]
param(
    [switch]$SkipQualityChecks,
    [switch]$CheckLocalToolchain
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$mobileRoot = Join-Path $repoRoot "apps\mobile"
$mobilePackagePath = Join-Path $mobileRoot "package.json"
$appJsonPath = Join-Path $mobileRoot "app.json"
$runtimeConfigPath = Join-Path $mobileRoot "runtime.config.json"
$gradlePropertiesPath = Join-Path $mobileRoot "android\gradle.properties"
$androidStringsPath = Join-Path $mobileRoot "android\app\src\main\res\values\strings.xml"
$androidColorsPath = Join-Path $mobileRoot "android\app\src\main\res\values\colors.xml"
$androidManifestPath = Join-Path $mobileRoot "android\app\src\main\AndroidManifest.xml"
$androidRootBuildGradlePath = Join-Path $mobileRoot "android\build.gradle"
$androidBuildGradlePath = Join-Path $mobileRoot "android\app\build.gradle"
$androidSettingsGradlePath = Join-Path $mobileRoot "android\settings.gradle"
$microsoftModuleRoot = Join-Path $mobileRoot "modules\nsfinance-microsoft-auth"
$microsoftModuleBuildGradlePath = Join-Path $microsoftModuleRoot "android\build.gradle"
$microsoftModuleManifestPath = Join-Path $microsoftModuleRoot "android\src\main\AndroidManifest.xml"
$microsoftModuleConfigPath = Join-Path $microsoftModuleRoot "android\src\main\res\raw\nsfinance_msal_config.json"

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

$runtimeConfig = Get-Content -Raw $runtimeConfigPath | ConvertFrom-Json
Assert-Equal $runtimeConfig.apiBaseUrl "https://api.finance.nsireland.ie" "Public API URL"
Assert-Equal $runtimeConfig.turnstilePageBaseUrl "https://api.finance.nsireland.ie" "Turnstile page URL"
Assert-NotBlank $runtimeConfig.googleOAuth.webClientId "Google web client ID"
Assert-NotBlank $runtimeConfig.googleOAuth.androidClientId "Google Android client ID"
Assert-NotBlank $runtimeConfig.microsoftOAuth.clientId "Microsoft client ID"
Assert-Equal $runtimeConfig.microsoftOAuth.authority "https://login.microsoftonline.com/common/v2.0" "Microsoft authority"
Assert-Equal $runtimeConfig.microsoftOAuth.scope "api://$($runtimeConfig.microsoftOAuth.clientId)/access_as_user" "Microsoft delegated API scope"
if ([int]$runtimeConfig.bankingAutoSyncIntervalMinutes -lt 1) {
    throw "Banking auto-sync interval must be at least one minute."
}

$appConfig = Get-Content -Raw $appJsonPath | ConvertFrom-Json
$mobilePackage = Get-Content -Raw $mobilePackagePath | ConvertFrom-Json
Assert-Equal $appConfig.expo.android.package "com.nsfinance.mobile" "Android application ID"
Assert-Equal $appConfig.expo.scheme "nsfinance" "Production application URI scheme"
Assert-Equal $appConfig.expo.updates.url "https://u.expo.dev/21986a2d-cbfa-4757-bf6d-04eb6aa4f197" "EAS Update URL"
if ([int]$appConfig.expo.android.versionCode -lt 1) {
    throw "Android versionCode must be a positive integer."
}

$mobileDependencyNames = @($mobilePackage.dependencies.PSObject.Properties.Name)
foreach ($requiredGoogleDependency in @(
    "react-native-nitro-google-signin",
    "react-native-nitro-modules"
)) {
    if ($requiredGoogleDependency -notin $mobileDependencyNames) {
        throw "Mobile package is missing native Google sign-in dependency '$requiredGoogleDependency'."
    }
}

if ("expo-auth-session" -in $mobileDependencyNames) {
    throw "Mobile package still contains the deprecated browser-based Google AuthSession dependency."
}

if (Test-Path -LiteralPath (Join-Path $mobileRoot "app\oauthredirect.tsx")) {
    throw "Mobile app still contains the obsolete browser OAuth redirect route."
}

$imagePickerPlugin = @($appConfig.expo.plugins) |
    Where-Object { $_ -is [array] -and $_.Count -ge 2 -and $_[0] -eq "expo-image-picker" } |
    Select-Object -First 1
if (-not $imagePickerPlugin) {
    throw "Expo ImagePicker must be configured explicitly for production permissions."
}

if ("./plugins/withMicrosoftMavenRepository" -notin @($appConfig.expo.plugins)) {
    throw "Expo config must retain the Microsoft MSAL Maven repository plugin."
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
    'android:scheme="nsfinance"'
)) {
    if (-not $manifest.Contains($requiredManifestValue)) {
        throw "AndroidManifest.xml is missing required production value '$requiredManifestValue'."
    }
}

if ($manifest.Contains('android:scheme="com.nsfinance.mobile"')) {
    throw "AndroidManifest.xml still exposes the obsolete browser OAuth redirect scheme."
}

$microsoftMavenRepository = "https://pkgs.dev.azure.com/MicrosoftDeviceSDK/DuoSDK-Public/_packaging/Duo-SDK-Feed/maven/v1"
$androidRootBuildGradle = Get-Content -Raw $androidRootBuildGradlePath
if (-not $androidRootBuildGradle.Contains($microsoftMavenRepository)) {
    throw "Android root build.gradle is missing the Microsoft MSAL Maven repository."
}

$microsoftModuleBuildGradle = Get-Content -Raw $microsoftModuleBuildGradlePath
if (-not $microsoftModuleBuildGradle.Contains("com.microsoft.identity.client:msal:8.3.2")) {
    throw "The NSFinance Microsoft module must use the reviewed MSAL Android version."
}

$microsoftModuleManifest = Get-Content -Raw $microsoftModuleManifestPath
foreach ($requiredMicrosoftManifestValue in @(
    'android:name="com.microsoft.identity.client.BrowserTabActivity"',
    'android:scheme="msauth"',
    'android:host="com.nsfinance.mobile"',
    'android:path="/WAXW7GzMd4SdrMXmNycH7iEkZPs="'
)) {
    if (-not $microsoftModuleManifest.Contains($requiredMicrosoftManifestValue)) {
        throw "The NSFinance Microsoft module manifest is missing '$requiredMicrosoftManifestValue'."
    }
}

$microsoftModuleConfig = Get-Content -Raw $microsoftModuleConfigPath | ConvertFrom-Json
Assert-Equal $microsoftModuleConfig.client_id $runtimeConfig.microsoftOAuth.clientId "Microsoft native client ID"
Assert-Equal $microsoftModuleConfig.redirect_uri "msauth://com.nsfinance.mobile/WAXW7GzMd4SdrMXmNycH7iEkZPs%3D" "Microsoft native redirect URI"
Assert-Equal $microsoftModuleConfig.account_mode "MULTIPLE" "Microsoft native account mode"
$microsoftAudience = @($microsoftModuleConfig.authorities) |
    ForEach-Object { $_.audience.type } |
    Select-Object -First 1
Assert-Equal $microsoftAudience "AzureADandPersonalMicrosoftAccount" "Microsoft account audience"

$gradleProperties = Get-Content -Raw $gradlePropertiesPath
$newArchitectureMatch = [regex]::Match($gradleProperties, "(?m)^newArchEnabled=(true|false)\r?$")
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

foreach ($requiredSigningValue in @(
    "NSFINANCE_ANDROID_KEYSTORE_PATH",
    "NSFINANCE_ANDROID_KEYSTORE_PASSWORD",
    "NSFINANCE_ANDROID_KEY_ALIAS",
    "NSFINANCE_ANDROID_KEY_PASSWORD",
    "signingConfig signingConfigs.release"
)) {
    if (-not $appBuildGradle.Contains($requiredSigningValue)) {
        throw "Android app/build.gradle is missing production signing value '$requiredSigningValue'."
    }
}

if ($appBuildGradle.Contains("signingConfigs.debug") -or $appBuildGradle.Contains("EAS injects")) {
    throw "Android app/build.gradle still contains obsolete debug or EAS build signing logic."
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

if ($CheckLocalToolchain) {
    if (-not $IsWindows) {
        throw "The local Android production toolchain check requires Windows."
    }

    . (Join-Path $PSScriptRoot "AndroidBuild.Common.ps1")
    $toolchain = Set-NSFinanceAndroidBuildEnvironment -RequireSigning
    Assert-Equal (Test-Path -LiteralPath (Join-Path $toolchain.JavaHome "bin\java.exe")) $true "OpenJDK 17"
    Assert-Equal (Test-Path -LiteralPath (Join-Path $toolchain.AndroidSdk "platforms\android-36\android.jar")) $true "Android SDK Platform 36"
    Assert-Equal (Test-Path -LiteralPath (Join-Path $toolchain.BuildTools "apksigner.bat")) $true "Android Build Tools 36.0.0"
    Assert-Equal (Test-Path -LiteralPath (Join-Path $toolchain.Ndk "source.properties")) $true "Android NDK 27.1.12297006"
}

Write-Host "Android production release checks passed."
