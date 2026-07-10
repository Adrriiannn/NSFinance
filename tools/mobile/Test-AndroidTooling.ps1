[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$localBuildsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "local-builds"))
$testRoot = [IO.Path]::GetFullPath((Join-Path $localBuildsRoot "tooling-self-test-$([Guid]::NewGuid().ToString('N'))"))
if (-not $testRoot.StartsWith("$localBuildsRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Tooling self-test path escaped the repository's local-builds directory."
}

$sourceRoot = Join-Path $testRoot "source"
$outputRoot = Join-Path $testRoot "output"
$fixtureZip = Join-Path $testRoot "fixture.zip"
$fixtureApk = Join-Path $testRoot "fixture.apk"
$responsePath = Join-Path $testRoot "eas-response.json"
$saveScript = Join-Path $PSScriptRoot "Save-EasBuildArtifact.ps1"
$originalGitHubOutput = $env:GITHUB_OUTPUT

try {
    New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
    "NSFinance Android tooling fixture" | Set-Content -Encoding ascii (Join-Path $sourceRoot "fixture.txt")
    Compress-Archive -Path (Join-Path $sourceRoot "*") -DestinationPath $fixtureZip
    Move-Item -LiteralPath $fixtureZip -Destination $fixtureApk

    $sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $repoRoot rev-parse HEAD).Trim() }

    $response = @(
        [ordered]@{
            id = "00000000-0000-0000-0000-000000000001"
            status = "FINISHED"
            platform = "ANDROID"
            buildProfile = "production"
            appVersion = "1.0.0"
            appBuildVersion = "42"
            runtimeVersion = "1.0.0"
            gitCommitHash = $sourceCommit
            artifacts = [ordered]@{
                applicationArchiveUrl = ([Uri]::new($fixtureApk)).AbsoluteUri
            }
        }
    )

    $response | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $responsePath
    $env:GITHUB_OUTPUT = $null
    & $saveScript `
        -InputJson $responsePath `
        -OutputDirectory $outputRoot `
        -FileName "NSFinance-tooling-self-test.apk" `
        -AllowLocalArtifact

    $apk = Get-Item (Join-Path $outputRoot "NSFinance-tooling-self-test.apk")
    $manifestPath = "$($apk.FullName).manifest.json"
    $checksumPath = "$($apk.FullName).sha256"

    if ($apk.Length -le 0 -or -not (Test-Path $manifestPath) -or -not (Test-Path $checksumPath)) {
        throw "Android artifact tooling self-test did not produce all expected files."
    }

    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    if ($manifest.buildId -ne "00000000-0000-0000-0000-000000000001") {
        throw "Android artifact tooling self-test wrote incorrect build metadata."
    }

    if ($manifest.androidVersionCode -ne "42" -or $manifest.sourceCommit -ne $sourceCommit) {
        throw "Android artifact tooling self-test did not preserve authoritative EAS version/source metadata."
    }

    $expectedHash = (Get-FileHash -Algorithm SHA256 $apk.FullName).Hash.ToLowerInvariant()
    if ($manifest.sha256 -ne $expectedHash) {
        throw "Android artifact tooling self-test wrote an incorrect checksum."
    }

    Write-Host "Android artifact tooling self-test passed."
}
finally {
    $env:GITHUB_OUTPUT = $originalGitHubOutput
    if (Test-Path $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
