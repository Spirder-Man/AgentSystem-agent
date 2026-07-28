# Agent1 CPU Mode Full Test Runner
# === UTF-8 Encoding Enforcement (Fixes Chinese mojibake in pipeline capture) ===
# Must execute BEFORE any dotnet test call to prevent GBK/UTF-8 mismatch
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # Fix L2: pipeline decode + L4: TRX child inherit
[Console]::InputEncoding  = [System.Text.Encoding]::UTF8   # Fix L2: stdin encode
$OutputEncoding = [System.Text.Encoding]::UTF8            # Fix L2: PowerShell native pipe encoding
$env:DOTNET_CLI_UI_LANGUAGE = "zh-CN"                     # Fix: dotnet CLI locale
# =====================================================================
$ErrorActionPreference = "Continue"

$env:DB_PASSWORD = "7758521"
$env:ALERT_EMAIL_PASSWORD = "dlyayiibtlwldefb"
$env:ALERT_RECIPIENT_EMAILS = "lcy.050801@qq.com"
$env:KNOWLEDGE_BASE_PATH = "knowledgebase"

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logDir = Join-Path "d:\桌面\agent\项目\Agent1\logs\test-results" "${timestamp}_cpu_fallback"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Agent1 CPU Fallback Mode Full Test" -ForegroundColor Cyan
Write-Host "  Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "  Console Encoding: $([Console]::OutputEncoding.EncodingName)" -ForegroundColor DarkGray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "[Config] GpuEmbeddingEnabled=false, GpuSearchEnabled=false, RerankerEnabled=false" -ForegroundColor Yellow
Write-Host "[Config] GpuFallbackEnabled=true" -ForegroundColor Yellow
Write-Host ""

$dll = "Agent1.Tests\bin\Debug\net8.0\Agent1.Tests.dll"
$logFile = Join-Path $logDir "full-test-output.log"
$summaryFile = Join-Path $logDir "summary.txt"

Write-Host "[Phase 2] Running full test suite..." -ForegroundColor Green

$sw = [System.Diagnostics.Stopwatch]::StartNew()

$groups = @(
    @{Name="LlmService (CircuitBreaker+Thinking)"; Filter="LlmServiceTests"},
    @{Name="Circuit Breaker"; Filter="CircuitBreakerTests"},
    @{Name="Error Paths & Fallback"; Filter="ErrorPathTests"},
    @{Name="Knowledge Base Service"; Filter="KnowledgeBaseServiceTests"},
    @{Name="Knowledge Pipeline"; Filter="KnowledgePipelineTests"},
    @{Name="Compliance Tools"; Filter="ChemicalComplianceToolsTests"},
    @{Name="Tool Service"; Filter="ToolServiceTests"},
    @{Name="Conclusion Verifier"; Filter="ConclusionVerifierTests"},
    @{Name="Eval Engine"; Filter="EvalEngineTests"},
    @{Name="Intent Router"; Filter="IntentRouterTests"},
    @{Name="Sensitive Data Masker"; Filter="SensitiveDataMaskerTests"},
    @{Name="Metrics Collector"; Filter="MetricsCollectorTests"},
    @{Name="App Config"; Filter="AppConfigTests"},
    @{Name="Model Config"; Filter="ModelConfigTests"},
    @{Name="Architecture Convergence"; Filter="ArchitectureConvergenceTests"},
    @{Name="AI Inference"; Filter="AiInferenceTests"},
    @{Name="Business Orchestration"; Filter="BusinessOrchestrationTests"},
    @{Name="API and Middleware"; Filter="ApiAndMiddlewareTests"},
    @{Name="Memory System"; Filter="MemorySystemTests"},
    @{Name="Observability"; Filter="ObservabilityTests"},
    @{Name="Database Integration"; Filter="DatabaseIntegrationTests"},
    @{Name="API Integration"; Filter="ApiIntegrationTests"},
    @{Name="Models Core"; Filter="ModelsCoreTests"},
    @{Name="Infrastructure Services"; Filter="InfrastructureServicesTests"},
    @{Name="Chemical Database"; Filter="ChemicalDatabaseTests"},
    @{Name="Chemical Substance DB"; Filter="ChemicalSubstanceDatabaseTests"},
    @{Name="Query Cache"; Filter="QueryCacheServiceTests"}
)

$totalPass = 0
$totalFail = 0
$totalCount = 0

foreach ($g in $groups) {
    Write-Host -NoNewline "  [$($g.Name)] "
    $output = & dotnet test $dll --no-build --verbosity normal --filter "FullyQualifiedName~$($g.Filter)" 2>&1
    $output | Out-File -Append -FilePath $logFile -Encoding UTF8
    
    $summaryLine = $output | Select-String "Passed!|Failed!|通过!|失败!" | Select-Object -Last 1
    if ($summaryLine) {
        $text = $summaryLine.Line
        if ($text -match "通过.*?(\d+).*?总计.*?(\d+)") {
            $p = [int]$matches[1]; $t = [int]$matches[2]
            $totalPass += $p; $totalCount += $t
            $f = $t - $p
            $totalFail += $f
            if ($f -eq 0) { Write-Host "PASS ($p/$t)" -ForegroundColor Green }
            else { Write-Host "FAIL ($p PASS, $f FAIL / $t)" -ForegroundColor Red }
        } elseif ($text -match "Passed.*?(\d+).*?Total.*?(\d+)") {
            $p = [int]$matches[1]; $t = [int]$matches[2]
            $totalPass += $p; $totalCount += $t
            $f = $t - $p
            $totalFail += $f
            if ($f -eq 0) { Write-Host "PASS ($p/$t)" -ForegroundColor Green }
            else { Write-Host "FAIL ($p PASS, $f FAIL / $t)" -ForegroundColor Red }
        }
    } elseif ($output -match "no test matches|未找到匹配") {
        Write-Host "SKIP (no tests)" -ForegroundColor Yellow
    } else {
        Write-Host "? (unparseable)" -ForegroundColor Gray
    }
}

$sw.Stop()

Write-Host ""
Write-Host "[Summary] Final global run..." -ForegroundColor Green
$fullOutput = & dotnet test $dll --no-build --verbosity normal 2>&1
$fullOutput | Out-File -Append -FilePath $logFile -Encoding UTF8
$fullOutput | Out-File -FilePath (Join-Path $logDir "summary-output.log") -Encoding UTF8

$fullLines = $fullOutput | Select-String "Passed!|Failed!|Skipped!|Total|Duration|通过!|失败!|跳过!|总计|耗时" | ForEach-Object { $_.Line }
$fullLines | Out-File -FilePath $summaryFile -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Test Report" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Duration: $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor White

$fullLines | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
Write-Host ""
Write-Host "Logs saved to: $logDir" -ForegroundColor Cyan

# Generate structured report
$reportContent = @"
Agent1 CPU Fallback Mode Full Test Report
==========================================
Test Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Duration: $($sw.Elapsed.TotalSeconds.ToString('F1'))s
Config: GpuEmbeddingEnabled=false, GpuSearchEnabled=false, RerankerEnabled=false, GpuFallbackEnabled=true

Test Module Results:
$($groups | ForEach-Object { "  $($_.Name): $($_.Filter)" } | Out-String)

Log Files:
  Full log: $logFile
  Summary:  $($summaryFile)
"@

$reportContent | Out-File -FilePath (Join-Path $logDir "report.txt") -Encoding UTF8
