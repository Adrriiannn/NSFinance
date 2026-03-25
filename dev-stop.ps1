$ErrorActionPreference = "SilentlyContinue"

Write-Host "`nStopping NSFinance development stack..." -ForegroundColor Cyan

$root = "C:\Users\%USERNAME%\Desktop\Projects\NSFinance"
$dbPath = Join-Path $root "infra\docker"
$statePath = Join-Path $root ".dev-orchestrator"
$adbExe = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"

function Stop-ProcessByPidFile($fileName) {
    $path = Join-Path $statePath $fileName
    if (Test-Path $path) {
        $pidValue = Get-Content $path | Select-Object -First 1
        if ($pidValue) {
            try {
                Stop-Process -Id ([int]$pidValue) -Force
            } catch {}
        }
        Remove-Item $path -Force -ErrorAction SilentlyContinue
    }
}

function Stop-ProcessesByName([string[]]$names) {
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            try { Stop-Process -Id $_.Id -Force } catch {}
        }
    }
}

Write-Host "`nClosing orchestrator-managed console windows..." -ForegroundColor Yellow
Stop-ProcessByPidFile "api.pid"
Stop-ProcessByPidFile "mobile.pid"
Stop-ProcessByPidFile "emulator.pid"

Get-Process powershell -ErrorAction SilentlyContinue | Where-Object {
    $_.MainWindowTitle -match "NSFinance API|NSFinance Mobile"
} | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Get-Process cmd -ErrorAction SilentlyContinue | Where-Object {
    $_.MainWindowTitle -match "NSFinance API|NSFinance Mobile"
} | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Write-Host "`nClosing Android emulator..." -ForegroundColor Yellow
if (Test-Path $adbExe) {
    try {
        $devicesOutput = (& $adbExe devices 2>&1 | Out-String)
        $deviceMatches = [regex]::Matches($devicesOutput, "(emulator-\d+)\s+device")
        foreach ($match in $deviceMatches) {
            $deviceId = $match.Groups[1].Value
            try {
                & $adbExe -s $deviceId emu kill | Out-Null
            } catch {}
        }
        Start-Sleep -Seconds 2
    } catch {}
}

Stop-ProcessesByName @(
    "emulator",
    "qemu-system-x86_64",
    "qemu-system-i386",
    "qemu-system-aarch64",
    "adb"
)

Write-Host "`nStopping leftover API / Expo processes..." -ForegroundColor Yellow
Stop-ProcessesByName @(
    "dotnet",
    "node"
)

if (Test-Path $dbPath) {
    Write-Host "`nStopping Docker services..." -ForegroundColor Yellow
    Push-Location $dbPath
    try {
        docker compose down
    } catch {}
    Pop-Location
}

Write-Host "Closing Docker Desktop..." -ForegroundColor Yellow
Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}
Stop-ProcessesByName @("com.docker.backend", "com.docker.proxy", "dockerd")

if (Test-Path $statePath) {
    Get-ChildItem -Path $statePath -Filter "*.pid" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host "`nAll NSFinance services stopped." -ForegroundColor Green
