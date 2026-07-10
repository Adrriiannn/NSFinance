[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputJson,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$FileName,
    [switch]$AllowLocalArtifact
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$mobileRoot = Join-Path $repoRoot "apps\mobile"
$inputPath = (Resolve-Path $InputJson).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)

function Find-EasBuild {
    param($Node)

    if ($null -eq $Node -or $Node -is [string] -or $Node -is [ValueType]) {
        return $null
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [pscustomobject]) {
        foreach ($item in $Node) {
            $match = Find-EasBuild $item
            if ($match) {
                return $match
            }
        }

        return $null
    }

    $idProperty = $Node.PSObject.Properties["id"]
    $artifactsProperty = $Node.PSObject.Properties["artifacts"]
    if ($idProperty -and $artifactsProperty) {
        return $Node
    }

    foreach ($property in $Node.PSObject.Properties) {
        $match = Find-EasBuild $property.Value
        if ($match) {
            return $match
        }
    }

    return $null
}

function Get-OptionalPropertyValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($property) {
        return $property.Value
    }

    return $null
}

$buildResponse = Get-Content -Raw $inputPath | ConvertFrom-Json
$build = Find-EasBuild $buildResponse
if (-not $build) {
    throw "The EAS response does not contain a build artifact."
}

if ($build.PSObject.Properties["status"] -and $build.status -ne "FINISHED") {
    throw "EAS build '$($build.id)' has status '$($build.status)', not 'FINISHED'."
}

$reportedPlatform = Get-OptionalPropertyValue $build "platform"
if ($reportedPlatform -and $reportedPlatform -ne "ANDROID") {
    throw "EAS build '$($build.id)' is for '$reportedPlatform', not Android."
}

$reportedProfile = Get-OptionalPropertyValue $build "buildProfile"
if ($reportedProfile -and $reportedProfile -ne "production") {
    throw "EAS build '$($build.id)' used profile '$reportedProfile', not 'production'."
}

$artifactUrl = $null
foreach ($propertyName in @("applicationArchiveUrl", "buildUrl", "url")) {
    $property = $build.artifacts.PSObject.Properties[$propertyName]
    if ($property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        $artifactUrl = [string]$property.Value
        break
    }
}

if (-not $artifactUrl) {
    throw "EAS build '$($build.id)' does not expose an application archive URL."
}

$appConfig = Get-Content -Raw (Join-Path $mobileRoot "app.json") | ConvertFrom-Json
$easConfig = Get-Content -Raw (Join-Path $mobileRoot "eas.json") | ConvertFrom-Json
$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $repoRoot rev-parse HEAD).Trim() }
$easCommit = [string](Get-OptionalPropertyValue $build "gitCommitHash")
if ($env:GITHUB_SHA -and $easCommit -and $easCommit -ne $env:GITHUB_SHA) {
    throw "EAS built commit '$easCommit', but the workflow expected '$($env:GITHUB_SHA)'."
}

$shortCommit = $commit.Substring(0, [Math]::Min(8, $commit.Length))
$version = [string]$appConfig.expo.version
$builtAppVersion = [string](Get-OptionalPropertyValue $build "appVersion")
$builtVersionCode = [string](Get-OptionalPropertyValue $build "appBuildVersion")
$builtRuntimeVersion = [string](Get-OptionalPropertyValue $build "runtimeVersion")

if ([string]::IsNullOrWhiteSpace($builtAppVersion)) {
    $builtAppVersion = $version
}

if ([string]::IsNullOrWhiteSpace($builtRuntimeVersion)) {
    $builtRuntimeVersion = [string]$appConfig.expo.android.runtimeVersion
}

if ([string]::IsNullOrWhiteSpace($FileName)) {
    $FileName = "NSFinance-android-$version-$shortCommit.apk"
}

if (-not $FileName.EndsWith(".apk", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The Android artifact filename must end in .apk."
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$apkPath = Join-Path $outputPath $FileName
Write-Host "Downloading the completed EAS Android artifact."

$artifactUri = $null
if (-not [Uri]::TryCreate($artifactUrl, [UriKind]::Absolute, [ref]$artifactUri)) {
    throw "EAS returned an invalid application archive URL."
}

if ($artifactUri.IsFile) {
    if (-not $AllowLocalArtifact) {
        throw "Local artifact URLs are accepted only by the tooling self-test."
    }

    Copy-Item -LiteralPath $artifactUri.LocalPath -Destination $apkPath -Force
}
elseif ($artifactUri.Scheme -eq "https") {
    Invoke-WebRequest -Uri $artifactUri -OutFile $apkPath -UseBasicParsing
}
else {
    throw "EAS application archives must use HTTPS."
}

$stream = [IO.File]::OpenRead($apkPath)
try {
    $firstByte = $stream.ReadByte()
    $secondByte = $stream.ReadByte()
}
finally {
    $stream.Dispose()
}

if ($firstByte -ne 0x50 -or $secondByte -ne 0x4B) {
    Remove-Item -LiteralPath $apkPath -Force
    throw "The downloaded artifact is not a valid APK/ZIP archive."
}

$apk = Get-Item $apkPath
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $apkPath).Hash.ToLowerInvariant()
$manifestPath = "$apkPath.manifest.json"
$checksumPath = "$apkPath.sha256"

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    buildId = [string]$build.id
    buildProfile = "production"
    channel = [string]$easConfig.build.production.channel
    platform = "android"
    applicationId = [string]$appConfig.expo.android.package
    appVersion = $builtAppVersion
    androidVersionCode = $builtVersionCode
    runtimeVersion = $builtRuntimeVersion
    sourceCommit = $commit
    githubRunId = [string]$env:GITHUB_RUN_ID
    apiBaseUrl = [string]$easConfig.build.production.env.EXPO_PUBLIC_API_BASE_URL
    artifactFile = $apk.Name
    artifactBytes = $apk.Length
    sha256 = $hash
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $manifestPath
"$hash  $($apk.Name)" | Set-Content -Encoding ascii $checksumPath

if ($env:GITHUB_OUTPUT) {
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "artifact_path=$apkPath"
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "manifest_path=$manifestPath"
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "checksum_path=$checksumPath"
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "build_id=$($build.id)"
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "sha256=$hash"
    Add-Content -Encoding utf8 $env:GITHUB_OUTPUT "artifact_name=$($apk.Name)"
}

Write-Host "APK saved to $apkPath"
Write-Host "SHA-256: $hash"
