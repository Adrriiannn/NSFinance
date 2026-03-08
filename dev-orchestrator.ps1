param(
    [switch]$StartDb = $true,
    [switch]$ClearExpoCache = $false,
    [switch]$OpenSwagger = $true,
    [switch]$OpenCode = $true,
    [switch]$OpenPostman = $true,
    [switch]$OpenDBeaver = $true,
    [int]$ApiPort = 5080
)

$ErrorActionPreference = "Stop"

# -----------------------------
# Paths
# -----------------------------
$root = "C:\Users\MariusAlbu\Desktop\Projects\NSFinTech"
$apiPath = Join-Path $root "apps\api\src\NSFinTech.Api"
$workerPath = Join-Path $root "apps\worker\src\NSFinTech.Worker"
$mobilePath = Join-Path $root "apps\mobile"
$dbPath = Join-Path $root "infra\docker"
$statePath = Join-Path $root ".dev-orchestrator"

$codeExe = "C:\Users\MariusAlbu\AppData\Local\Programs\Microsoft VS Code\Code.exe"
$postmanExe = "C:\Users\MariusAlbu\AppData\Local\Postman\Postman.exe"
$dbeaverExe = "C:\Users\MariusAlbu\AppData\Local\DBeaver\dbeaver.exe"

$apiUrl = "http://127.0.0.1:$ApiPort"
$apiHealthUrl = "$apiUrl/health"
$swaggerUrl = "$apiUrl/swagger"

# -----------------------------
# Helpers
# -----------------------------
function Write-Step($msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function Test-CommandExists($command) {
    return [bool](Get-Command $command -ErrorAction SilentlyContinue)
}

function Ensure-Directory($path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Save-Pid($name, $processIdValue) {
    Set-Content -Path (Join-Path $statePath "$name.pid") -Value $processIdValue
}

function Get-LanIpv4 {
    try {
        $configs = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object {
                $_.IPAddress -notlike "127.*" -and
                $_.IPAddress -notlike "169.254.*" -and
                $_.PrefixOrigin -ne "WellKnown"
            }

        $preferred = $configs | Select-Object -First 1
        if ($preferred) { return $preferred.IPAddress }
    } catch {}

    try {
        $ipconfigOutput = ipconfig
        $match = $ipconfigOutput | Select-String -Pattern 'IPv4 Address[^\:]*:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)'
        if ($match) {
            return $match.Matches[0].Groups[1].Value
        }
    } catch {}

    return $null
}

function Start-DevWindow($title, $workingDir, $command) {
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

function Wait-ForHttp200($url, $timeoutSeconds = 60) {
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

# -----------------------------
# Pre-flight checks
# -----------------------------
Write-Step "Pre-flight checks"

foreach ($path in @($root, $apiPath, $workerPath, $mobilePath)) {
    if (-not (Test-Path $path)) {
        throw "Path not found: $path"
    }
}

foreach ($cmd in @("dotnet", "pnpm")) {
    if (-not (Test-CommandExists $cmd)) {
        throw "$cmd is not installed or not in PATH."
    }
}

Ensure-Directory $statePath

$lanIp = Get-LanIpv4
if ($lanIp) {
    $phoneApiUrl = "http://${lanIp}:$ApiPort"
    Write-Host "Phone API: $phoneApiUrl" -ForegroundColor Yellow
}

# -----------------------------
# Start DB
# -----------------------------
if ($StartDb) {
    Write-Step "Starting database"
    if (-not (Test-CommandExists "docker")) {
        throw "docker is not installed or not in PATH."
    }
    if (-not (Test-Path $dbPath)) {
        throw "Database docker path not found: $dbPath"
    }

    Push-Location $dbPath
    try {
        docker compose up -d
    } finally {
        Pop-Location
    }
}

# -----------------------------
# Start API
# -----------------------------
Write-Step "Starting API"
$apiCommand = "Write-Host 'Starting API...' -ForegroundColor Green; dotnet run --urls `"http://0.0.0.0:$ApiPort`""
$apiProc = Start-DevWindow -title "NSFinTech API" -workingDir $apiPath -command $apiCommand
Save-Pid "api" $apiProc.Id

# -----------------------------
# Wait for API
# -----------------------------
Write-Step "Waiting for API health"
$apiReady = Wait-ForHttp200 -url $apiHealthUrl -timeoutSeconds 90

if (-not $apiReady) {
    Write-Warning "API did not respond healthy within timeout: $apiHealthUrl"
} else {
    Write-Host "API is healthy: $apiHealthUrl" -ForegroundColor Green

    if ($OpenSwagger) {
        Write-Step "Opening Swagger"
        Start-Process $swaggerUrl | Out-Null
    }
}

# -----------------------------
# Start Worker
# -----------------------------
Write-Step "Starting Worker"
$workerCommand = "Write-Host 'Starting Worker...' -ForegroundColor Yellow; dotnet run"
$workerProc = Start-DevWindow -title "NSFinTech Worker" -workingDir $workerPath -command $workerCommand
Save-Pid "worker" $workerProc.Id

# -----------------------------
# Start Mobile
# -----------------------------
Write-Step "Starting Mobile"
$mobileCommand = if ($ClearExpoCache) {
    "Write-Host 'Starting Mobile (Expo, cache cleared)...' -ForegroundColor Magenta; pnpm exec expo start -c"
} else {
    "Write-Host 'Starting Mobile (Expo)...' -ForegroundColor Magenta; pnpm exec expo start"
}

$mobileProc = Start-DevWindow -title "NSFinTech Mobile" -workingDir $mobilePath -command $mobileCommand
Save-Pid "mobile" $mobileProc.Id

# -----------------------------
# Open apps
# -----------------------------
if ($OpenCode) {
    Write-Step "Opening VS Code"
    if (-not (Test-Path $codeExe)) {
        Write-Warning "VS Code not found at: $codeExe"
    } else {
        $codeProc = Start-Process -FilePath $codeExe -ArgumentList "`"$root`"" -PassThru
        Save-Pid "code" $codeProc.Id
    }
}

if ($OpenPostman) {
    Write-Step "Opening Postman"
    if (-not (Test-Path $postmanExe)) {
        Write-Warning "Postman not found at: $postmanExe"
    } else {
        $postmanProc = Start-Process -FilePath $postmanExe -PassThru
        Save-Pid "postman" $postmanProc.Id
    }
}

if ($OpenDBeaver) {
    Write-Step "Opening DBeaver"
    if (-not (Test-Path $dbeaverExe)) {
        Write-Warning "DBeaver not found at: $dbeaverExe"
    } else {
        $dbeaverProc = Start-Process -FilePath $dbeaverExe -PassThru
        Save-Pid "dbeaver" $dbeaverProc.Id
    }
}

# -----------------------------
# Done
# -----------------------------
Write-Step "Done"
Write-Host "API:      $apiUrl" -ForegroundColor Cyan
Write-Host "Health:   $apiHealthUrl" -ForegroundColor Cyan
Write-Host "Swagger:  $swaggerUrl" -ForegroundColor Cyan
if ($lanIp) {
    Write-Host "Phone API: $phoneApiUrl" -ForegroundColor Cyan
}
Write-Host "Launched API, Worker, Mobile, VS Code, Postman, and DBeaver." -ForegroundColor Green