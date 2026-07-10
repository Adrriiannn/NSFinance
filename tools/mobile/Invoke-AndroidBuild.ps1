[CmdletBinding()]
param(
    [string]$OutputDirectory = "local-builds\android",
    [ValidateSet("Apk", "Aab", "Both")][string]$ArtifactFormat = "Both",
    [switch]$SkipQualityChecks,
    [switch]$Install,
    [switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)

function Invoke-DirectoryMirror {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [string[]]$ExcludedDirectories = @(),
        [string[]]$ExcludedFiles = @()
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $arguments = @(
        $Source,
        $Destination,
        "/MIR",
        "/R:2",
        "/W:1",
        "/COPY:DAT",
        "/DCOPY:DAT",
        "/NFL",
        "/NDL",
        "/NJH",
        "/NJS",
        "/NP"
    )
    if ($ExcludedDirectories.Count -gt 0) {
        $arguments += "/XD"
        $arguments += $ExcludedDirectories
    }
    if ($ExcludedFiles.Count -gt 0) {
        $arguments += "/XF"
        $arguments += $ExcludedFiles
    }

    & robocopy.exe @arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -gt 7) {
        throw "Unable to synchronize '$Source' to the Android build workspace (robocopy exit code $exitCode)."
    }
}

function Enter-AndroidBuildWorkspaceLock {
    param([Parameter(Mandatory)][string]$LockPath)

    $deadline = [DateTime]::UtcNow.AddMinutes(30)
    $announcedWait = $false
    while ($true) {
        try {
            return [IO.FileStream]::new(
                $LockPath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None,
                1,
                [IO.FileOptions]::DeleteOnClose
            )
        }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for the shared Android build workspace."
            }
            if (-not $announcedWait) {
                Write-Host "Another NSFinance Android build is active; waiting for the shared workspace."
                $announcedWait = $true
            }
            Start-Sleep -Seconds 5
        }
    }
}

function Invoke-InShortBuildWorkspace {
    $workspaceParent = if ($env:NSFINANCE_ANDROID_BUILD_WORKSPACE) {
        [IO.Path]::GetFullPath($env:NSFINANCE_ANDROID_BUILD_WORKSPACE)
    }
    else {
        Join-Path $env:USERPROFILE "NFB"
    }
    $workspaceRoot = [IO.Path]::GetFullPath((Join-Path $workspaceParent "w"))
    $expectedPrefix = $workspaceParent.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
    if (-not $workspaceRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Android build workspace must remain inside its configured parent directory."
    }

    New-Item -ItemType Directory -Force -Path $workspaceRoot | Out-Null
    foreach ($fileName in @("package.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".gitignore")) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $fileName) -Destination (Join-Path $workspaceRoot $fileName) -Force
    }
    $npmConfigPath = Join-Path $repoRoot ".npmrc"
    if (Test-Path -LiteralPath $npmConfigPath) {
        Copy-Item -LiteralPath $npmConfigPath -Destination (Join-Path $workspaceRoot ".npmrc") -Force
    }

    $sourceMobile = Join-Path $repoRoot "apps\mobile"
    $workspaceMobile = Join-Path $workspaceRoot "apps\mobile"
    $excludedMobileDirectories = @(
        foreach ($relativePath in @(
            "node_modules",
            ".expo",
            ".tmp",
            "android\.gradle",
            "android\.cxx",
            "android\build",
            "android\app\.cxx",
            "android\app\build"
        )) {
            Join-Path $sourceMobile $relativePath
            Join-Path $workspaceMobile $relativePath
        }
    )
    $excludedMobileFiles = @(
        (Join-Path $sourceMobile "android\local.properties"),
        (Join-Path $workspaceMobile "android\local.properties")
    )
    Invoke-DirectoryMirror $sourceMobile $workspaceMobile $excludedMobileDirectories $excludedMobileFiles
    Invoke-DirectoryMirror (Join-Path $repoRoot "tools\mobile") (Join-Path $workspaceRoot "tools\mobile")

    $pnpm = (Get-Command "pnpm.cmd" -ErrorAction Stop).Source
    Push-Location $workspaceRoot
    try {
        & $pnpm install --frozen-lockfile --prefer-offline
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to install the synchronized Android workspace dependencies."
        }
    }
    finally {
        Pop-Location
    }

    $commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Unable to resolve the source commit before staging the Android build."
    }
    $worktreeState = @(& git -C $repoRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the source worktree before staging the Android build."
    }

    $previousEnvironment = @{}
    foreach ($name in @(
        "NSFINANCE_BUILD_WORKSPACE_ACTIVE",
        "NSFINANCE_SOURCE_REPO_ROOT",
        "NSFINANCE_SOURCE_COMMIT",
        "NSFINANCE_SOURCE_DIRTY"
    )) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    }

    try {
        $env:NSFINANCE_BUILD_WORKSPACE_ACTIVE = "1"
        $env:NSFINANCE_SOURCE_REPO_ROOT = $repoRoot
        $env:NSFINANCE_SOURCE_COMMIT = $commit
        $env:NSFINANCE_SOURCE_DIRTY = ($worktreeState.Count -gt 0).ToString()

        $stagedScript = Join-Path $workspaceRoot "tools\mobile\Invoke-AndroidBuild.ps1"
        & $stagedScript `
            -OutputDirectory $outputPath `
            -ArtifactFormat $ArtifactFormat `
            -SkipQualityChecks:$SkipQualityChecks `
            -Install:$Install `
            -Launch:$Launch
    }
    finally {
        foreach ($entry in $previousEnvironment.GetEnumerator()) {
            if ($null -eq $entry.Value) {
                Remove-Item -LiteralPath "Env:$($entry.Key)" -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
            }
        }
    }
}

if (-not $IsWindows) {
    throw "The NSFinance local Android production builder is configured for its Windows runner."
}
if (
    -not $env:NSFINANCE_BUILD_WORKSPACE_ACTIVE -and
    ($repoRoot -match "\s" -or $env:NSFINANCE_FORCE_PERSISTENT_BUILD_WORKSPACE -eq "1")
) {
    $workspaceParent = if ($env:NSFINANCE_ANDROID_BUILD_WORKSPACE) {
        [IO.Path]::GetFullPath($env:NSFINANCE_ANDROID_BUILD_WORKSPACE)
    }
    else {
        Join-Path $env:USERPROFILE "NFB"
    }
    New-Item -ItemType Directory -Force -Path $workspaceParent | Out-Null
    $workspaceLock = Enter-AndroidBuildWorkspaceLock (Join-Path $workspaceParent ".android-build.lock")
    try {
        Write-Host "Synchronizing NSFinance into the persistent short-path Android build workspace."
        Invoke-InShortBuildWorkspace
    }
    finally {
        $workspaceLock.Dispose()
    }
    return
}

. (Join-Path $PSScriptRoot "AndroidBuild.Common.ps1")

$mobileRoot = Join-Path $repoRoot "apps\mobile"
$androidRoot = Join-Path $mobileRoot "android"
$testScript = Join-Path $PSScriptRoot "Test-AndroidRelease.ps1"
$saveScript = Join-Path $PSScriptRoot "Save-AndroidBuildArtifact.ps1"
$installScript = Join-Path $PSScriptRoot "Install-AndroidApk.ps1"
$gradle = Join-Path $androidRoot "gradlew.bat"

if (-not (Test-Path -LiteralPath $gradle)) {
    throw "The Android Gradle wrapper is missing."
}
if ($Launch -and -not $Install) {
    throw "-Launch requires -Install."
}
if ($Install -and $ArtifactFormat -eq "Aab") {
    throw "AAB files cannot be installed directly; select Apk or Both."
}

$startedAt = [DateTime]::UtcNow
$results = @()
$toolchain = $null
try {
    $toolchain = Set-NSFinanceAndroidBuildEnvironment -RequireSigning
    & $testScript -SkipQualityChecks:$SkipQualityChecks -CheckLocalToolchain

    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
    $tasks = switch ($ArtifactFormat) {
        "Apk" { "app:assembleRelease" }
        "Aab" { "app:bundleRelease" }
        "Both" { "app:assembleRelease"; "app:bundleRelease" }
    }
    $tasks = @($tasks)

    Write-Host "Building NSFinance Android $ArtifactFormat artifacts locally with Gradle."
    Push-Location $androidRoot
    try {
        & $gradle @tasks --build-cache --parallel
        if ($LASTEXITCODE -ne 0) {
            throw "The Android production Gradle build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    if ($ArtifactFormat -in @("Apk", "Both")) {
        $apkSource = Join-Path $androidRoot "app\build\outputs\apk\release\app-release.apk"
        if (-not (Test-Path -LiteralPath $apkSource)) {
            throw "Gradle completed without producing the expected APK."
        }
        $results += & $saveScript -InputPath $apkSource -OutputDirectory $outputPath
    }

    if ($ArtifactFormat -in @("Aab", "Both")) {
        $aabSource = Join-Path $androidRoot "app\build\outputs\bundle\release\app-release.aab"
        if (-not (Test-Path -LiteralPath $aabSource)) {
            throw "Gradle completed without producing the expected AAB."
        }
        $results += & $saveScript -InputPath $aabSource -OutputDirectory $outputPath
    }

    $apkResult = $results | Where-Object { $_.Type -eq "apk" } | Select-Object -First 1
    $aabResult = $results | Where-Object { $_.Type -eq "aab" } | Select-Object -First 1

    if ($Install) {
        & $installScript -ApkPath $apkResult.ArtifactPath -Launch:$Launch
    }

    $appConfig = Get-Content -LiteralPath (Join-Path $mobileRoot "app.json") -Raw | ConvertFrom-Json
    if ($env:GITHUB_OUTPUT) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "artifact_directory=$outputPath"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "app_version=$($appConfig.expo.version)"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version_code=$($appConfig.expo.android.versionCode)"
        if ($apkResult) {
            Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "apk_path=$($apkResult.ArtifactPath)"
            Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "apk_sha256=$($apkResult.Sha256)"
        }
        if ($aabResult) {
            Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "aab_path=$($aabResult.ArtifactPath)"
            Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "aab_sha256=$($aabResult.Sha256)"
        }
    }

    $duration = [DateTime]::UtcNow - $startedAt
    Write-Host "NSFinance Android production build completed in $([Math]::Round($duration.TotalMinutes, 2)) minutes."
}
finally {
    Clear-NSFinanceAndroidSigningEnvironment
}
