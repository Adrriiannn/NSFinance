$ErrorActionPreference = "SilentlyContinue"

Write-Host "`nStopping NSFinTech development stack..." -ForegroundColor Cyan

$root = "C:\Users\MariusAlbu\Desktop\Projects\NSFinTech"
$dbPath = Join-Path $root "infra\docker"
$statePath = Join-Path $root ".dev-orchestrator"

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

Write-Host "`nClosing orchestrator PowerShell windows..." -ForegroundColor Yellow
Stop-ProcessByPidFile "api.pid"
Stop-ProcessByPidFile "worker.pid"
Stop-ProcessByPidFile "mobile.pid"

Get-Process powershell | Where-Object {
    $_.MainWindowTitle -match "NSFinTech API|NSFinTech Worker|NSFinTech Mobile"
} | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Write-Host "Closing VS Code / Postman / DBeaver opened by orchestrator..." -ForegroundColor Yellow
Stop-ProcessByPidFile "code.pid"
Stop-ProcessByPidFile "postman.pid"
Stop-ProcessByPidFile "dbeaver.pid"

# Fallbacks in case the launched PID was only a parent/bootstrap process
Get-Process Code -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Get-Process Postman -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Get-Process dbeaver -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Write-Host "Stopping leftover dotnet processes..." -ForegroundColor Yellow
Get-Process dotnet | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Write-Host "Stopping leftover node / Expo processes..." -ForegroundColor Yellow
Get-Process node | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force } catch {}
}

if (Test-Path $dbPath) {
    Write-Host "`nStopping Docker services..." -ForegroundColor Yellow
    Push-Location $dbPath
    try {
        docker compose down
    } catch {}
    Pop-Location
}

Write-Host "`nAll NSFinTech services stopped." -ForegroundColor Green