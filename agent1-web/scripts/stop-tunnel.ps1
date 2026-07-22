# ============================================================
# stop-tunnel.ps1 — Stop SSH tunnel process
# ============================================================

$existing = Get-Process -Name "SshTunnel" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[TUNNEL] Stopping SshTunnel.exe (PID: $($existing.Id))..."
    $existing | Stop-Process -Force
    Start-Sleep 1

    # Double check
    $stillRunning = Get-Process -Name "SshTunnel" -ErrorAction SilentlyContinue
    if ($stillRunning) {
        Write-Host "[TUNNEL] Force killing..."
        $stillRunning | Stop-Process -Force
    }
    Write-Host "[TUNNEL] Stopped"
} else {
    Write-Host "[TUNNEL] No running SshTunnel process"
}

# Cleanup stderr log
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stderrLog = "$scriptDir\tunnel-stderr.log"
if (Test-Path $stderrLog) {
    Remove-Item $stderrLog -Force -ErrorAction SilentlyContinue
    Write-Host "[TUNNEL] Cleaned stderr log"
}
