[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "AndroidBuild.Common.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$mobileRoot = Join-Path $repoRoot "apps\mobile"
$sourceArtifact = Get-Item (Resolve-Path -LiteralPath $InputPath).Path
$extension = $sourceArtifact.Extension.ToLowerInvariant()
if ($extension -notin @(".apk", ".aab")) {
    throw "Android release artifacts must be APK or AAB files."
}

$toolchain = Set-NSFinanceAndroidBuildEnvironment -RequireSigning
$appConfig = Get-Content -LiteralPath (Join-Path $mobileRoot "app.json") -Raw | ConvertFrom-Json
$runtimeConfig = Get-Content -LiteralPath (Join-Path $mobileRoot "runtime.config.json") -Raw | ConvertFrom-Json
$sourceRepoRoot = if ($env:NSFINANCE_SOURCE_REPO_ROOT) {
    $env:NSFINANCE_SOURCE_REPO_ROOT
}
else {
    $repoRoot
}

if ($env:GITHUB_SHA) {
    $commit = $env:GITHUB_SHA
}
elseif ($env:NSFINANCE_SOURCE_COMMIT) {
    $commit = $env:NSFINANCE_SOURCE_COMMIT
}
else {
    $commit = (& git -C $sourceRepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the source commit for the Android artifact."
    }
}
if ([string]::IsNullOrWhiteSpace($commit)) {
    throw "Unable to resolve the source commit for the Android artifact."
}

if ($env:NSFINANCE_SOURCE_DIRTY) {
    $sourceDirty = [bool]::Parse($env:NSFINANCE_SOURCE_DIRTY)
}
else {
    $worktreeState = @(& git -C $sourceRepoRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the source worktree for Android artifact metadata."
    }
    $sourceDirty = $worktreeState.Count -gt 0
}

$shortCommit = $commit.Substring(0, [Math]::Min(8, $commit.Length))
$versionName = [string]$appConfig.expo.version
$versionCode = [string]$appConfig.expo.android.versionCode
$runtimeVersion = [string]$appConfig.expo.android.runtimeVersion
$applicationId = [string]$appConfig.expo.android.package
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$artifactName = "NSFinance-android-$versionName-$versionCode-$shortCommit$extension"
$artifactPath = Join-Path $outputPath $artifactName
Copy-Item -LiteralPath $sourceArtifact.FullName -Destination $artifactPath -Force

$stream = [IO.File]::OpenRead($artifactPath)
try {
    if ($stream.ReadByte() -ne 0x50 -or $stream.ReadByte() -ne 0x4B) {
        throw "The generated Android artifact is not a ZIP-compatible archive."
    }
}
finally {
    $stream.Dispose()
}

$actualCertificateFingerprint = $null
if ($extension -eq ".apk") {
    $apksigner = Join-Path $toolchain.BuildTools "apksigner.bat"
    $signatureOutput = @(& $apksigner verify --verbose --print-certs $artifactPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "APK signature verification failed."
    }

    $certificateLine = $signatureOutput | Where-Object { $_ -match "Signer #1 certificate SHA-256 digest:\s*(.+)$" } | Select-Object -First 1
    if (-not $certificateLine) {
        throw "APK signing certificate fingerprint was not reported."
    }
    [void]($certificateLine -match "Signer #1 certificate SHA-256 digest:\s*(.+)$")
    $actualCertificateFingerprint = Normalize-NSFinanceCertificateFingerprint $Matches[1]

    $aapt2 = Join-Path $toolchain.BuildTools "aapt2.exe"
    $badging = @(& $aapt2 dump badging $artifactPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "APK package metadata inspection failed."
    }

    $packageLine = $badging | Where-Object { $_ -match "^package:" } | Select-Object -First 1
    if (-not $packageLine -or $packageLine -notmatch "name='([^']+)'\s+versionCode='([^']+)'\s+versionName='([^']+)'" ) {
        throw "APK package metadata could not be parsed."
    }
    if ($Matches[1] -ne $applicationId -or $Matches[2] -ne $versionCode -or $Matches[3] -ne $versionName) {
        throw "APK identity or version does not match the checked-in production configuration."
    }
}
else {
    $bundletool = if ($env:NSFINANCE_BUNDLETOOL_PATH) {
        $env:NSFINANCE_BUNDLETOOL_PATH
    }
    else {
        Join-Path $env:LOCALAPPDATA "NSFinance\AndroidTools\bundletool-all-1.18.3.jar"
    }
    if (-not (Test-Path -LiteralPath $bundletool)) {
        throw "Bundletool 1.18.3 is not installed for AAB validation."
    }

    $bundleValidationOutput = @(
        & (Join-Path $toolchain.JavaHome "bin\java.exe") -jar $bundletool validate "--bundle=$artifactPath" 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "AAB structure validation failed."
    }

    $jarsigner = Join-Path $toolchain.JavaHome "bin\jarsigner.exe"
    $jarVerificationOutput = @(& $jarsigner -verify -verbose -certs $artifactPath 2>&1)
    $verifiedMarker = $jarVerificationOutput | Where-Object { $_ -match "^jar verified\.$" } | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or -not $verifiedMarker) {
        throw "AAB signature verification failed."
    }

    $keytool = Join-Path $toolchain.JavaHome "bin\keytool.exe"
    $certificateOutput = @(& $keytool "-J-Duser.language=en" -printcert -jarfile $artifactPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "AAB signing certificate inspection failed."
    }
    $certificateLine = $certificateOutput | Where-Object { $_ -match "SHA256:\s*(.+)$" } | Select-Object -First 1
    if (-not $certificateLine) {
        throw "AAB signing certificate fingerprint was not reported."
    }
    [void]($certificateLine -match "SHA256:\s*(.+)$")
    $actualCertificateFingerprint = Normalize-NSFinanceCertificateFingerprint $Matches[1]
}

if ($actualCertificateFingerprint -ne $env:NSFINANCE_ANDROID_CERT_SHA256) {
    throw "Android artifact signing certificate does not match the protected NSFinance production identity."
}

$artifact = Get-Item -LiteralPath $artifactPath
$hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestPath = "$artifactPath.manifest.json"
$checksumPath = "$artifactPath.sha256"
$manifest = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    buildSystem = "local-gradle"
    platform = "android"
    artifactType = $extension.TrimStart(".")
    applicationId = $applicationId
    appVersion = $versionName
    androidVersionCode = $versionCode
    runtimeVersion = $runtimeVersion
    updateChannel = "production"
    sourceCommit = $commit
    sourceDirty = $sourceDirty
    githubRunId = [string]$env:GITHUB_RUN_ID
    runnerName = [string]$env:RUNNER_NAME
    apiBaseUrl = [string]$runtimeConfig.apiBaseUrl
    signingCertificateSha256 = $actualCertificateFingerprint
    artifactFile = $artifact.Name
    artifactBytes = $artifact.Length
    sha256 = $hash
}

[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($checksumPath, "$hash  $($artifact.Name)`n", [Text.Encoding]::ASCII)

Write-Host "$($extension.TrimStart('.').ToUpperInvariant()) verified and saved to $artifactPath"
Write-Output ([pscustomobject]@{
    Type = $extension.TrimStart(".")
    ArtifactPath = $artifactPath
    ManifestPath = $manifestPath
    ChecksumPath = $checksumPath
    Sha256 = $hash
})
