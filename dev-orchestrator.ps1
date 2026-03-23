param(
    [switch]$StartDb = $true,
    [switch]$ClearExpoCache = $false,
    [switch]$StartEmulator = $true,
    [switch]$LaunchExpoGo = $true,
    [int]$ApiPort = 5080,
    [string]$Root = "C:\Users\MariusAlbu\Desktop\Projects\NSFinance",
    [string]$PreferredAvdName = "Resizable (Experimental)",
    [string]$EmulatorExe = "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe",
    [string]$AdbExe = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
    [string]$DockerDesktopExe = "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
)

$ErrorActionPreference = "Stop"

try {
    # -----------------------------
    # Paths
    # -----------------------------
    $apiPath = Join-Path $Root "apps\api\src\NSFinance.Api"
    $mobilePath = Join-Path $Root "apps\mobile"
    $dbPath = Join-Path $Root "infra\docker"
    $statePath = Join-Path $Root ".dev-orchestrator"
    $expoLogPath = Join-Path $statePath "expo.log"

    $apiUrl = "http://127.0.0.1:$ApiPort"
    $apiHealthUrl = "$apiUrl/health"

    # -----------------------------
    # Helpers
    # -----------------------------
    function Write-Step([string]$msg) {
        Write-Host "`n=== $msg ===" -ForegroundColor Cyan
    }

    function Test-CommandExists([string]$command) {
        return [bool](Get-Command $command -ErrorAction SilentlyContinue)
    }

    function Ensure-Directory([string]$path) {
        if (-not (Test-Path $path)) {
            New-Item -ItemType Directory -Path $path | Out-Null
        }
    }

    function Save-Pid([string]$name, [int]$processIdValue) {
        Set-Content -Path (Join-Path $statePath "$name.pid") -Value $processIdValue
    }

    function Start-DevWindow([string]$title, [string]$workingDir, [string]$command) {
        $psCommand = @"
`$Host.UI.RawUI.WindowTitle = '$title'
Set-Location '$workingDir'
Write-Host 'Working directory: $workingDir' -ForegroundColor DarkGray
$command
"@

        return Start-Process powershell -PassThru -ArgumentList @(
            "-NoExit",
            "-ExecutionPolicy", "Bypass",
            "-Command", $psCommand
        )
    }

    function Wait-ForHttp200([string]$url, [int]$timeoutSeconds = 60) {
        $start = Get-Date
        do {
            try {
                $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3
                if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
                    return $true
                }
            } catch {
                Start-Sleep -Seconds 2
            }
        } while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds)

        return $false
    }

    function Ensure-DockerReady {
        Write-Step "Ensuring Docker Desktop is running"

        $dockerReady = $false
        try {
            docker info *> $null
            $dockerReady = $true
        } catch {
            $dockerReady = $false
        }

        if (-not $dockerReady) {
            if (-not (Test-Path $DockerDesktopExe)) {
                $altDockerDesktop = "$env:LocalAppData\Programs\Docker\Docker\Docker Desktop.exe"
                if (Test-Path $altDockerDesktop) {
                    $script:DockerDesktopExe = $altDockerDesktop
                }
            }

            if (-not (Test-Path $DockerDesktopExe)) {
                throw "Docker Desktop.exe was not found. Checked: '$DockerDesktopExe'"
            }

            Start-Process $DockerDesktopExe | Out-Null

            $maxAttempts = 90
            for ($i = 1; $i -le $maxAttempts; $i++) {
                Start-Sleep -Seconds 2
                try {
                    docker info *> $null
                    $dockerReady = $true
                    break
                } catch {
                    Write-Host "Docker not ready yet... ($i/$maxAttempts)" -ForegroundColor DarkGray
                }
            }

            if (-not $dockerReady) {
                throw "Docker Desktop started, but Docker engine did not become ready in time."
            }
        }

        Write-Host "Docker is ready." -ForegroundColor Green
    }

    function Resolve-AvdName([string]$preferredName) {
        if (-not (Test-Path $EmulatorExe)) {
            throw "Android emulator executable not found at '$EmulatorExe'"
        }

        $avds = & $EmulatorExe -list-avds 2>$null
        if (-not $avds) {
            throw "No Android Virtual Devices were returned by emulator -list-avds"
        }

        $exact = $avds | Where-Object { $_ -eq $preferredName } | Select-Object -First 1
        if ($exact) { return $exact }

        $normalizedPreferred = ($preferredName -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()
        $match = $avds | Where-Object {
            (($_ -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()) -eq $normalizedPreferred
        } | Select-Object -First 1
        if ($match) { return $match }

        $contains = $avds | Where-Object {
            (($_ -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()) -like "*$normalizedPreferred*" -or
            $normalizedPreferred -like "*$((($_ -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()))*"
        } | Select-Object -First 1
        if ($contains) { return $contains }

        throw "Could not resolve AVD name '$preferredName'. Available AVDs: $($avds -join ', ')"
    }

    function Ensure-AdbReady([int]$timeoutSeconds = 60) {
        if (-not (Test-Path $AdbExe)) {
            throw "adb not found at '$AdbExe'"
        }

        Write-Step "Preparing ADB"

        try {
            & $AdbExe kill-server *> $null
        } catch {}

        Start-Sleep -Seconds 1

        # Start server without waiting on the spawned process forever
        Start-Process -FilePath $AdbExe -ArgumentList 'start-server' -WindowStyle Hidden | Out-Null

        $start = Get-Date
        do {
            Start-Sleep -Seconds 2
            $devicesOutput = (& $AdbExe devices 2>&1 | Out-String)

            if ($devicesOutput -match "List of devices attached") {
                return $true
            }

            Write-Host "Waiting for ADB to respond..." -ForegroundColor DarkGray
        } while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds)

        return $false
    }

    function Wait-ForEmulatorDevice([int]$timeoutSeconds = 90) {
        Write-Host ""
        Write-Host "=== Waiting for emulator device ===" -ForegroundColor Cyan

        $start = Get-Date

        do {
            Start-Sleep -Seconds 2
            $devicesOutput = (& $AdbExe devices 2>&1 | Out-String)

            $match = [regex]::Match($devicesOutput, "(emulator-\d+)\s+device")
            if ($match.Success) {
                $deviceId = $match.Groups[1].Value
                Write-Host "Emulator detected by ADB: $deviceId" -ForegroundColor Green
                return $deviceId
            }

            Write-Host "Waiting for emulator device..." -ForegroundColor DarkGray
        }
        while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds)

        throw "ADB did not detect an emulator device in time."
    }

    function Wait-ForAndroidBoot([string]$deviceId, [int]$timeoutSeconds = 120) {
        if (-not $deviceId) {
            throw "Wait-ForAndroidBoot called without a deviceId."
        }

        Write-Host ""
        Write-Host "=== Waiting for Android boot completion ===" -ForegroundColor Cyan

        $start = Get-Date

        do {
            Start-Sleep -Seconds 2

            $sysBoot = (& $AdbExe -s $deviceId shell getprop sys.boot_completed 2>$null | Out-String).Trim()
            $devBoot = (& $AdbExe -s $deviceId shell getprop dev.bootcomplete 2>$null | Out-String).Trim()
            $bootAnim = (& $AdbExe -s $deviceId shell getprop init.svc.bootanim 2>$null | Out-String).Trim()

            if ($sysBoot -eq "1" -or $devBoot -eq "1" -or $bootAnim -eq "stopped") {
                Write-Host "Android boot completed on $deviceId." -ForegroundColor Green
                return $true
            }

            Write-Host "Waiting for Android to finish booting... sys=$sysBoot dev=$devBoot bootanim=$bootAnim" -ForegroundColor DarkGray
        }
        while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds)

        Write-Warning "Android did not report full boot completion in time. Continuing anyway."
        return $false
    }

    function Wait-ForExpoHttp([int]$port = 8081, [int]$timeoutSeconds = 120) {
    Write-Host ""
    Write-Host "=== Waiting for Expo server ===" -ForegroundColor Cyan

    $start = Get-Date
    $url = "http://127.0.0.1:$port"

    do {
        Start-Sleep -Seconds 2

        try {
            $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) {
                Write-Host "Expo server is responding on $url" -ForegroundColor Green
                return $true
            }
        } catch {
            Write-Host "Waiting for Expo server..." -ForegroundColor DarkGray
        }
    }
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSeconds)

    throw "Expo server did not become reachable on $url in time."
}

    function Launch-ExpoGoOnAndroid([string]$deviceId, [int]$port = 8081) {
    if (-not $deviceId) {
        throw "Launch-ExpoGoOnAndroid called without a deviceId."
    }

    Write-Host ""
    Write-Host "=== Launching App on emulator ===" -ForegroundColor Cyan

    $expoPackage = "host.exp.exponent"

    # Reverse localhost traffic from emulator to host
    & $AdbExe -s $deviceId reverse tcp:$port tcp:$port | Out-Null

    # Expo Go dev URL
    $expoUrl = "exp://127.0.0.1:$port/--/"

    & $AdbExe -s $deviceId shell am start -W -a android.intent.action.VIEW -d $expoUrl $expoPackage | Out-Null

    Write-Host "Expo Go launch intent sent to $deviceId with $expoUrl" -ForegroundColor Green
}

    # -----------------------------
    # Pre-start checks
    # -----------------------------
    Write-Step "Pre-start checks"

    foreach ($path in @($Root, $apiPath, $mobilePath, $dbPath)) {
        if (-not (Test-Path $path)) {
            throw "Required path not found: $path"
        }
    }

    foreach ($cmd in @('dotnet', 'pnpm', 'docker')) {
        if (-not (Test-CommandExists $cmd)) {
            throw "Required command not found on PATH: $cmd"
        }
    }

    Ensure-Directory $statePath

    # -----------------------------
    # Database
    # -----------------------------
    if ($StartDb) {
        Ensure-DockerReady
        Write-Step "Starting database"
        Push-Location $dbPath
        try {
            docker compose up -d
        } finally {
            Pop-Location
        }
    }

    # -----------------------------
    # API
    # -----------------------------
    Write-Step "Starting API"
    $apiCommand = "`$Host.UI.RawUI.BackgroundColor = 'Black'; Clear-Host; Write-Host 'Starting NSFinance API on $apiUrl' -ForegroundColor Green; dotnet run --urls http://0.0.0.0:$ApiPort"
    $apiProcess = Start-DevWindow -title "NSFinance API" -workingDir $apiPath -command $apiCommand
    Save-Pid -name "api" -processIdValue $apiProcess.Id

    Write-Step "Waiting for API contact"
    if (-not (Wait-ForHttp200 -url $apiHealthUrl -timeoutSeconds 90)) {
        Write-Warning "API health endpoint did not return success in time: $apiHealthUrl"
    }

    # -----------------------------
    # Mobile / Expo
    # -----------------------------
    Write-Step "Starting mobile"

$pnpmCmd = (Get-Command "pnpm.cmd" -ErrorAction SilentlyContinue).Source
if (-not $pnpmCmd) {
    throw "pnpm.cmd was not found on PATH."
}

$cmdTitle = 'title NSFinance Mobile'
$cmdCd = "cd /d `"$mobilePath`""

$cmdExpo = if ($ClearExpoCache) {
    "`"$pnpmCmd`" exec expo start --go --localhost --clear"
} else {
    "`"$pnpmCmd`" exec expo start --go --localhost"
}

$mobileCmdLine = "$cmdTitle && $cmdCd && $cmdExpo"

$mobileProcess = Start-Process cmd.exe -PassThru -WorkingDirectory $mobilePath -ArgumentList @('/k', $mobileCmdLine)
Save-Pid -name "mobile" -processIdValue $mobileProcess.Id

    # -----------------------------
    # Emulator
    # -----------------------------
    $deviceId = $null

    if ($StartEmulator) {
        Write-Step "Starting Android emulator"
        $resolvedAvdName = Resolve-AvdName -preferredName $PreferredAvdName
        Write-Host "Using AVD: $resolvedAvdName" -ForegroundColor Green
        # Hide the emulator's companion console window without affecting the emulator UI itself.
        Start-Process $EmulatorExe -ArgumentList @('-avd', $resolvedAvdName, '-no-snapshot-load') -WindowStyle Hidden | Out-Null

        if (-not (Ensure-AdbReady -timeoutSeconds 60)) {
            Write-Warning "ADB readiness check was inconclusive. Continuing to emulator device detection."
        }

        Write-Step "Waiting for emulator device"
        $deviceId = Wait-ForEmulatorDevice -timeoutSeconds 240

        if (-not $deviceId) {
            throw "ADB did not detect an emulator device in time."
        }

        if (-not (Wait-ForAndroidBoot -deviceId $deviceId -timeoutSeconds 300)) {
            Write-Warning "Emulator was detected, but Android did not finish booting in time."
        } else {
            Write-Host "Android boot completed on $deviceId." -ForegroundColor Green
        }
    }

    # -----------------------------
    # Launch in Expo Go
    # -----------------------------
    if ($LaunchExpoGo) {
    if (-not $deviceId) {
        Write-Warning "No emulator device ID is available. Skipping Expo Go launch."
    } else {
        Wait-ForExpoHttp -port 8081 -timeoutSeconds 120
        Launch-ExpoGoOnAndroid -deviceId $deviceId -port 8081
    }
    }

    Write-Host "`nDone." -ForegroundColor Green
}
catch {
    Write-Host "`n[FATAL] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}