# Agent1 Full Test Suite
# Usage: .\test-all.ps1

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Agent1 Full Test Suite" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$dll = "Agent1.Tests\bin\Debug\net8.0\Agent1.Tests.dll"

# Step 1: Build
Write-Host "[1/2] Building..." -ForegroundColor Yellow
dotnet build -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "OK" -ForegroundColor Green

# Step 2: Run all non-DB tests
Write-Host "[2/2] Running tests..." -ForegroundColor Yellow
Write-Host ""

$groups = @(
    @{Name="LlmService (Circuit Breaker)"        ; Filter="LlmServiceTests"},
    @{Name="Architecture Convergence"            ; Filter="ArchitectureConvergenceTests"},
    @{Name="Error Paths"                         ; Filter="ErrorPathTests"},
    @{Name="Circuit Breaker (full)"              ; Filter="CircuitBreakerTests"},
    @{Name="Chemical Substance DB (58 chems)"    ; Filter="ChemicalSubstanceDatabaseTests"},
    @{Name="Compliance Tools"                    ; Filter="ChemicalComplianceToolsTests"},
    @{Name="Intent Router"                       ; Filter="IntentRouterTests"},
    @{Name="Sensitive Data Masker"               ; Filter="SensitiveDataMaskerTests"},
    @{Name="Eval Engine"                         ; Filter="EvalEngineTests"},
    @{Name="Conclusion Verifier"                 ; Filter="ConclusionVerifierTests"},
    @{Name="Tool Service"                        ; Filter="ToolServiceTests"},
    @{Name="Knowledge Base (BM25)"               ; Filter="KnowledgeBaseServiceTests"},
    @{Name="Metrics Collector"                   ; Filter="MetricsCollectorTests"}
)

$totalPass = 0
$totalCount = 0

foreach ($g in $groups) {
    $r = & dotnet vstest $dll --TestCaseFilter:"FullyQualifiedName~$($g.Filter)" 2>&1
    $line = $r | Select-String "通过:|Passed:" | Select-Object -Last 1
    if ($line) {
        if ($line -match "通过:\s+(\d+).*总计:\s+(\d+)") {
            $p = [int]$matches[1]
            $t = [int]$matches[2]
            $totalPass += $p
            $totalCount += $t
        }
        $status = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
        Write-Host "  $status  $($g.Name)" -ForegroundColor $(if ($LASTEXITCODE -eq 0) { "Green" } else { "Red" })
    } else {
        Write-Host "  SKIP  $($g.Name) (no tests found)" -ForegroundColor Yellow
    }
}

# Final summary
Write-Host ""
$r2 = & dotnet vstest $dll --TestCaseFilter:"Category!=Integration" 2>&1
$r2 | Select-String "通过!|Passed!" | ForEach-Object { Write-Host $_ -ForegroundColor Green }
Write-Host ""

Write-Host "For DB integration tests (requires PostgreSQL 16+pgvector):" -ForegroundColor Cyan
Write-Host '  dotnet vstest Agent1.Tests\bin\Debug\net8.0\Agent1.Tests.dll --TestCaseFilter:"Category=Integration"'
Write-Host ""
