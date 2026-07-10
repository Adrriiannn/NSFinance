[CmdletBinding()]
param(
    [string]$OutputDirectory = "local-builds\android",
    [switch]$SkipQualityChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$mobileRoot = Join-Path $repoRoot "apps\mobile"
$testScript = Join-Path $PSScriptRoot "Test-AndroidRelease.ps1"
$saveScript = Join-Path $PSScriptRoot "Save-EasBuildArtifact.ps1"
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$responsePath = Join-Path $outputPath "eas-build-response.json"

$easCommandName = if ($IsWindows) { "eas.cmd" } else { "eas" }
$eas = Get-Command $easCommandName -ErrorAction SilentlyContinue
if (-not $eas) {
    throw "EAS CLI is not available on PATH. Install the pinned 20.5.1 release before building."
}

& $testScript -SkipQualityChecks:$SkipQualityChecks -CheckEasProject

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Push-Location $mobileRoot
try {
    Write-Host "Starting the production Android APK build on EAS."
    $response = & $eas.Source build `
        --platform android `
        --profile production `
        --non-interactive `
        --wait `
        --json

    if ($LASTEXITCODE -ne 0) {
        throw "EAS Android build failed with exit code $LASTEXITCODE."
    }

    $response | Set-Content -Encoding utf8 $responsePath
}
finally {
    Pop-Location
}

try {
    & $saveScript -InputJson $responsePath -OutputDirectory $outputPath
}
finally {
    Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
}
