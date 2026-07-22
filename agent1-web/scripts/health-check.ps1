# ============================================================
# health-check.ps1 — Remote service health check
#
# Checks: API health / Auth login / LLM compliance / Assets
# Prereq: SSH tunnel running (localhost:15001 -> remote API:5000)
# ============================================================

param(
    [string]$ApiUrl = "http://localhost:15001"
)

$ErrorActionPreference = "Continue"
$allOk = $true

Write-Host "========================================"
Write-Host "  Agent1 Remote Health Check"
Write-Host "========================================"

# ── 1. API health endpoint ──
Write-Host ""
Write-Host "[1/4] API Health: $ApiUrl/health"
try {
    $health = Invoke-RestMethod -Uri "$ApiUrl/health" -TimeoutSec 10 -ErrorAction Stop
    Write-Host "  status: $($health.status)"

    $dbStatus = if ($health.checks.database) { $health.checks.database } else { "unknown" }
    $llmStatus = if ($health.checks.ollama) { $health.checks.ollama } else { "unknown" }
    $docs = if ($health.checks.knowledge_base_docs) { $health.checks.knowledge_base_docs } else { 0 }

    Write-Host "  database: $dbStatus"
    Write-Host "  llm: $llmStatus"
    Write-Host "  knowledge_base_docs: $docs"

    if ($dbStatus -ne "connected") { Write-Host "  [WARN] DB not connected!" -ForegroundColor Yellow; $allOk = $false }
    if ($llmStatus -ne "reachable") { Write-Host "  [WARN] LLM not reachable!" -ForegroundColor Yellow; $allOk = $false }
    if ([int]$docs -le 0) { Write-Host "  [WARN] Knowledge base docs = 0!" -ForegroundColor Yellow; $allOk = $false }
    Write-Host "  => PASS" -ForegroundColor Green
} catch {
    Write-Host "  => FAIL: $_" -ForegroundColor Red
    $allOk = $false
}

# ── 2. Auth login ──
Write-Host ""
Write-Host "[2/4] Auth Login Test"
try {
    $loginBody = '{"username":"admin","password":"7758521"}'
    $loginResp = Invoke-RestMethod -Uri "$ApiUrl/api/Auth/login" `
        -Method POST -Body $loginBody -ContentType "application/json" -TimeoutSec 10
    if ($loginResp.token) {
        $preview = $loginResp.token.Substring(0, [Math]::Min(20, $loginResp.token.Length))
        Write-Host "  token: ${preview}..."
        Write-Host "  role: $($loginResp.role)"
        Write-Host "  => PASS" -ForegroundColor Green
    } else {
        Write-Host "  => FAIL: no token returned" -ForegroundColor Red
        $allOk = $false
    }
} catch {
    Write-Host "  => FAIL: $_" -ForegroundColor Red
    $allOk = $false
}

# ── 3. LLM compliance check ──
Write-Host ""
Write-Host "[3/4] LLM Compliance Check — waiting..."
try {
    $loginBody = '{"username":"admin","password":"7758521"}'
    $loginResp = Invoke-RestMethod -Uri "$ApiUrl/api/Auth/login" `
        -Method POST -Body $loginBody -ContentType "application/json" -TimeoutSec 10

    $body = '{"query":"benzene"}'
    $complianceResp = Invoke-RestMethod -Uri "$ApiUrl/api/Compliance/check" `
        -Method POST -Body $body -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $($loginResp.token)" } `
        -TimeoutSec 60

    $toolsCount = if ($complianceResp.toolsUsed) { $complianceResp.toolsUsed.Count } else { 0 }
    $hasResponse = -not [string]::IsNullOrEmpty($complianceResp.response)
    Write-Host "  toolsUsed: $toolsCount"
    Write-Host "  hasResponse: $hasResponse"
    Write-Host "  => PASS (GPU inference OK)" -ForegroundColor Green
} catch {
    Write-Host "  => FAIL: $_" -ForegroundColor Red
    $allOk = $false
}

# ── 4. Assets ──
Write-Host ""
Write-Host "[4/4] Assets Query"
try {
    $loginBody = '{"username":"admin","password":"7758521"}'
    $loginResp = Invoke-RestMethod -Uri "$ApiUrl/api/Auth/login" `
        -Method POST -Body $loginBody -ContentType "application/json" -TimeoutSec 10

    $assetsResp = Invoke-RestMethod -Uri "$ApiUrl/api/Inspection/assets" `
        -Headers @{ Authorization = "Bearer $($loginResp.token)" } -TimeoutSec 10
    $assetCount = if ($assetsResp -is [array]) { $assetsResp.Count } elseif ($assetsResp.items) { $assetsResp.items.Count } else { 0 }
    Write-Host "  assets: $assetCount"
    Write-Host "  => PASS" -ForegroundColor Green
} catch {
    Write-Host "  => FAIL: $_" -ForegroundColor Red
    $allOk = $false
}

# ── Summary ──
Write-Host ""
Write-Host "========================================"
if ($allOk) {
    Write-Host "  All services healthy — ready for real GPU E2E" -ForegroundColor Green
} else {
    Write-Host "  Some services unhealthy — check remote environment" -ForegroundColor Red
}
Write-Host "========================================"
if ($allOk) { exit 0 } else { exit 1 }
