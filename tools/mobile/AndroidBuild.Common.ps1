Set-StrictMode -Version Latest

function Resolve-NSFinanceJavaHome {
    $candidates = @(
        $env:JAVA_HOME,
        [Environment]::GetEnvironmentVariable("JAVA_HOME", "User"),
        [Environment]::GetEnvironmentVariable("JAVA_HOME", "Machine"),
        "C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $javaPath = Join-Path $candidate "bin\java.exe"
        if (Test-Path -LiteralPath $javaPath) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "OpenJDK 17 is not installed or JAVA_HOME is not configured."
}

function Resolve-NSFinanceAndroidSdk {
    $candidates = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        [Environment]::GetEnvironmentVariable("ANDROID_HOME", "User"),
        [Environment]::GetEnvironmentVariable("ANDROID_SDK_ROOT", "User"),
        [Environment]::GetEnvironmentVariable("ANDROID_HOME", "Machine"),
        [Environment]::GetEnvironmentVariable("ANDROID_SDK_ROOT", "Machine"),
        (Join-Path $env:LOCALAPPDATA "Android\Sdk")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "platforms\android-36\android.jar")) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Android SDK Platform 36 is not installed or ANDROID_HOME is not configured."
}

function ConvertFrom-NSFinanceProtectedString {
    param([Parameter(Mandatory)][string]$Value)

    $secureValue = ConvertTo-SecureString $Value
    return [Net.NetworkCredential]::new("", $secureValue).Password
}

function Normalize-NSFinanceCertificateFingerprint {
    param([Parameter(Mandatory)][string]$Value)

    return ($Value -replace "[^A-Fa-f0-9]", "").ToLowerInvariant()
}

function Set-NSFinanceAndroidBuildEnvironment {
    [CmdletBinding()]
    param([switch]$RequireSigning)

    $javaHome = Resolve-NSFinanceJavaHome
    $androidSdk = Resolve-NSFinanceAndroidSdk
    $buildTools = Join-Path $androidSdk "build-tools\36.0.0"
    $ndk = Join-Path $androidSdk "ndk\27.1.12297006"

    foreach ($requiredPath in @(
        (Join-Path $buildTools "apksigner.bat"),
        (Join-Path $buildTools "aapt2.exe"),
        (Join-Path $ndk "source.properties"),
        (Join-Path $androidSdk "platform-tools\adb.exe")
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Required Android build component is missing: $requiredPath"
        }
    }

    $env:JAVA_HOME = $javaHome
    $env:ANDROID_HOME = $androidSdk
    $env:ANDROID_SDK_ROOT = $androidSdk
    $env:EXPO_NO_METRO_WORKSPACE_ROOT = "1"
    $env:NODE_ENV = "production"
    $env:GRADLE_USER_HOME = Join-Path $env:USERPROFILE "NFG"
    New-Item -ItemType Directory -Force -Path $env:GRADLE_USER_HOME | Out-Null

    $requiredPathEntries = @(
        (Join-Path $javaHome "bin"),
        (Join-Path $androidSdk "platform-tools"),
        $buildTools,
        (Join-Path $androidSdk "cmdline-tools\latest\bin")
    )
    $existingPathEntries = @($env:PATH -split ";" | Where-Object { $_ })
    $env:PATH = (@($requiredPathEntries + $existingPathEntries) | Select-Object -Unique) -join ";"

    $signingConfigPath = if ($env:NSFINANCE_ANDROID_SIGNING_CONFIG) {
        $env:NSFINANCE_ANDROID_SIGNING_CONFIG
    }
    else {
        Join-Path $env:LOCALAPPDATA "NSFinance\AndroidSigning\signing.dpapi.json"
    }

    if ($RequireSigning) {
        if (-not (Test-Path -LiteralPath $signingConfigPath)) {
            throw "The protected NSFinance Android signing configuration is not installed for this Windows user."
        }

        $signingConfig = Get-Content -LiteralPath $signingConfigPath -Raw | ConvertFrom-Json
        if ([int]$signingConfig.formatVersion -ne 1) {
            throw "The NSFinance Android signing configuration format is unsupported."
        }

        $keystorePath = [string]$signingConfig.keystorePath
        if (-not (Test-Path -LiteralPath $keystorePath)) {
            throw "The NSFinance production keystore is not installed at its protected local path."
        }

        $env:NSFINANCE_ANDROID_KEYSTORE_PATH = (Resolve-Path -LiteralPath $keystorePath).Path
        $env:NSFINANCE_ANDROID_KEYSTORE_PASSWORD = ConvertFrom-NSFinanceProtectedString $signingConfig.keystorePassword
        $env:NSFINANCE_ANDROID_KEY_ALIAS = ConvertFrom-NSFinanceProtectedString $signingConfig.keyAlias
        $env:NSFINANCE_ANDROID_KEY_PASSWORD = ConvertFrom-NSFinanceProtectedString $signingConfig.keyPassword
        $env:NSFINANCE_ANDROID_CERT_SHA256 = Normalize-NSFinanceCertificateFingerprint $signingConfig.sha256CertificateFingerprint
    }

    return [pscustomobject]@{
        JavaHome = $javaHome
        AndroidSdk = $androidSdk
        BuildTools = $buildTools
        Ndk = $ndk
        SigningConfigPath = $signingConfigPath
    }
}

function Clear-NSFinanceAndroidSigningEnvironment {
    foreach ($name in @(
        "NSFINANCE_ANDROID_KEYSTORE_PATH",
        "NSFINANCE_ANDROID_KEYSTORE_PASSWORD",
        "NSFINANCE_ANDROID_KEY_ALIAS",
        "NSFINANCE_ANDROID_KEY_PASSWORD",
        "NSFINANCE_ANDROID_CERT_SHA256"
    )) {
        Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
    }
}
