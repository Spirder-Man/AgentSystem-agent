# ============================================================
# start-tunnel.ps1 — Start SSH tunnel to remote Agent1 API
#
# Usage: .\start-tunnel.ps1 [-LocalPort <15001>] [-RemotePort <5000>] [-Stop]
# Prereq: $env:SSH_HOST, $env:SSH_PORT, $env:SSH_USER, $env:SSH_PASSWORD
# ============================================================

param(
    [int]$LocalPort = 15001,
    [int]$RemotePort = 5000,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"

# ── Stop existing tunnel ──
if ($Stop) {
    $existing = Get-Process -Name "SshTunnel" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "[TUNNEL] Stopping SshTunnel (PID: $($existing.Id))..."
        $existing | Stop-Process -Force
        Start-Sleep 1
        Write-Host "[TUNNEL] Stopped"
    } else {
        Write-Host "[TUNNEL] No running tunnel"
    }
    return
}

# ── Read SSH credentials from env ──
$SSH_HOST     = if ($env:SSH_HOST)     { $env:SSH_HOST }     else { $null }
$SSH_PORT     = if ($env:SSH_PORT)     { $env:SSH_PORT }     else { "22" }
$SSH_USER     = if ($env:SSH_USER)     { $env:SSH_USER }     else { "root" }
$SSH_PASSWORD = if ($env:SSH_PASSWORD) { $env:SSH_PASSWORD } else { $null }

if (-not $SSH_HOST) {
    Write-Error "SSH_HOST env var not set. Please run:`n  `$env:SSH_HOST='your-host'`n  `$env:SSH_PASSWORD='your-password'"
    exit 1
}
if (-not $SSH_PASSWORD) {
    Write-Error "SSH_PASSWORD env var not set"
    exit 1
}

# ── Check if tunnel already running ──
$existing = Get-Process -Name "SshTunnel" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[TUNNEL] Tunnel already running (PID: $($existing.Id)), skip"
    Write-Host "[TUNNEL] To restart, run: .\stop-tunnel.ps1 first"
    exit 0
}

# ── Locate SshTunnel.exe ──
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path "$scriptDir\..\.."
$tunnelExe = "$projectRoot\ssh-tunnel\bin\Release\net8.0\SshTunnel.exe"

if (-not (Test-Path $tunnelExe)) {
    Write-Host "[TUNNEL] SshTunnel.exe not found, building..."
    dotnet build "$projectRoot\ssh-tunnel\SshTunnel.csproj" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "SshTunnel build failed"
        exit 1
    }
}

# ── Check local port ──
$portCheck = netstat -ano | Select-String ":$LocalPort " | Select-String "LISTENING"
if ($portCheck) {
    Write-Error "Local port $LocalPort already in use. Free it or use -LocalPort to specify another"
    exit 1
}

# ── Start SSH tunnel ──
Write-Host "[TUNNEL] Starting: localhost:$LocalPort -> $SSH_HOST`:$RemotePort"
Write-Host "[TUNNEL] SSH: $SSH_USER@$SSH_HOST`:$SSH_PORT"

$stderrLog = "$scriptDir\tunnel-stderr.log"
$proc = Start-Process -NoNewWindow -FilePath $tunnelExe `
    -ArgumentList $SSH_HOST, $SSH_PORT, $SSH_USER, $SSH_PASSWORD, $LocalPort, "localhost", $RemotePort `
    -RedirectStandardError $stderrLog `
    -PassThru

Start-Sleep 3

# ── Verify tunnel ──
if ($proc.HasExited) {
    Write-Error "SshTunnel exited (ExitCode: $($proc.ExitCode))"
    if (Test-Path $stderrLog) {
        Write-Host "--- stderr ---"
        Get-Content $stderrLog
    }
    exit 1
}

# Poll health endpoint (max 15s)
$maxRetries = 15
for ($i = 1; $i -le $maxRetries; $i++) {
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:$LocalPort/health" -TimeoutSec 2 -ErrorAction Stop
        Write-Host "[TUNNEL] Ready! health: $($response.status)"
        Write-Host "[TUNNEL] PID: $($proc.Id), localhost:$LocalPort -> $SSH_HOST`:$RemotePort"
        exit 0
    } catch {
        if ($i -eq 1) { Write-Host "[TUNNEL] Waiting for tunnel..." }
        Start-Sleep 1
    }
}

Write-Error "[TUNNEL] Tunnel start timeout (${maxRetries}s). Check if remote API is running"
if (Test-Path $stderrLog) {
    Write-Host "--- stderr ---"
    Get-Content $stderrLog
}
exit 1
