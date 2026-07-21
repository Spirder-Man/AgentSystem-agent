#!/usr/bin/env pwsh
# ============================================================
# check-coverage.ps1 — 本地覆盖率回归检测
#
# 用法：.\check-coverage.ps1
# 功能：
#   1. 运行 dotnet test 并收集 coverlet 覆盖率
#   2. 与 TestResults/baseline-coverage.json 对比
#   3. 覆盖率下降 >2% → 报错退出
#   4. 低于阈值 (50%) → 报错退出
#
# 建议：在 git push 前运行，与 CI 行为一致
# ============================================================

param(
    [double]$Threshold = 50.0,
    [double]$MaxRegression = 2.0
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = (Get-Item "$scriptDir/..").FullName

Write-Host "╔══════════════════════════════════════╗"
Write-Host "║  Coverage Regression Check          ║"
Write-Host "╚══════════════════════════════════════╝"
Write-Host ""

# Step 1: Run tests with coverage
Write-Host "[1/3] Running tests with coverlet..."
$resultDir = "$projectRoot/TestResults/coverage-check"
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null

dotnet test "$projectRoot/Agent1.Tests" `
    --filter "Category!=Integration" `
    /p:CollectCoverage=true `
    /p:CoverletOutputFormat=cobertura `
    /p:CoverletOutput="$resultDir/" 2>&1 | Out-Null

$coverageFile = Get-ChildItem -Path $resultDir -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1

if (-not $coverageFile) {
    Write-Host "::error:: Coverage file not found!"
    exit 1
}

# Step 2: Parse coverage
Write-Host "[2/3] Parsing coverage report..."
[xml]$cov = Get-Content $coverageFile.FullName
$lineRate = [math]::Round([double]$cov.coverage.'line-rate' * 100, 2)
$branchRate = [math]::Round([double]$cov.coverage.'branch-rate' * 100, 2)
$linesCovered = $cov.coverage.'lines-covered'
$linesValid = $cov.coverage.'lines-valid'

Write-Host "  Line:   ${lineRate}% (${linesCovered}/${linesValid})"
Write-Host "  Branch: ${branchRate}%"

# Step 3: Compare with baseline
Write-Host "[3/3] Comparing with baseline..."
$baselineFile = "$projectRoot/TestResults/baseline-coverage.json"

if (Test-Path $baselineFile) {
    $baseline = Get-Content $baselineFile | ConvertFrom-Json
    $prevLine = [double]$baseline.lineRate
    $prevBranch = [double]$baseline.branchRate
    $drop = $prevLine - $lineRate

    Write-Host "  Previous: ${prevLine}% → Current: ${lineRate}%"
    
    if ($drop -gt $MaxRegression) {
        Write-Host "::error:: COVERAGE REGRESSION: ${prevLine}% → ${lineRate}% (drop ${drop}% > ${MaxRegression}%)"
        exit 1
    }
    elseif ($drop -gt 0) {
        Write-Host "  ⚠️  Slight drop (${drop}%), within ${MaxRegression}% threshold"
    }
    else {
        Write-Host "  ✅ Coverage stable or improved (+$([math]::Abs($drop))%)"
    }
} else {
    Write-Host "  ⚠️  No baseline found at $baselineFile — skipping regression check"
    # Auto-save current as baseline
    @{
        lineRate = $lineRate
        branchRate = $branchRate
        linesCovered = $linesCovered
        linesValid = $linesValid
        timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    } | ConvertTo-Json | Out-File -Encoding utf8 $baselineFile
    Write-Host "  📝 Saved current as baseline"
}

# Step 4: Threshold check
if ($lineRate -lt $Threshold) {
    Write-Host "::error:: Coverage ${lineRate}% below threshold ${Threshold}%"
    exit 1
}

Write-Host ""
Write-Host "✅ Coverage check passed — safe to push"
