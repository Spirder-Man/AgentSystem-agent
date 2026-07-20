# ══════════════════════════════════════════════════════════
# 化工合规 AI Agent 自动化评测流水线 (Phase 6.4)
# 
# 功能:
#   1. 依次运行现有评测集(63条) + 盲测集(35条)
#   2. 自动生成对比报告
#   3. 输出 eval_report_{timestamp}.json + eval_summary.md
#
# 用法: .\scripts\run_eval_pipeline.ps1
# ══════════════════════════════════════════════════════════

param(
    [string]$ModelConfig = "default",
    [switch]$SkipExisting = $false,
    [switch]$SkipBlind = $false,
    [string]$OutputDir = "TestResults2",
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path "$scriptDir\.."
$projectPath = "$projectRoot\Agent1\Agent1.csproj"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  化工合规 AI Agent — 自动化评测流水线 (Phase 6.4)   ║" -ForegroundColor Cyan
Write-Host "╠══════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║  时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')                  ║" -ForegroundColor Cyan
Write-Host "║  项目: $projectRoot       ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Step 0: 编译项目
Write-Host "━━━ Step 0: 编译项目 ━━━" -ForegroundColor Yellow
$buildResult = dotnet build $projectPath --no-restore -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 编译失败，流水线中止" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "   ✅ 编译成功" -ForegroundColor Green

# Step 1: 现有评测集 (63条)
$evalResults = @{}
if (-not $SkipExisting) {
    Write-Host ""
    Write-Host "━━━ Step 1: 运行现有评测集 (63条) ━━━" -ForegroundColor Yellow
    
    $env:EVAL_SET_PATH = "$projectRoot\Data\ComplianceEvalSet.json"
    $env:EVAL_OUTPUT_PATH = "$projectRoot\$OutputDir\eval_report_existing_{timestamp}.json"
    
    $existingSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        # 通过 dotnet run 执行评测（假定 Program.cs 支持 --eval 参数）
        # 如果项目有专门的评测入口，请调整命令
        dotnet run --project $projectPath -- --eval --eval-set "$projectRoot\Data\ComplianceEvalSet.json" --output "$projectRoot\$OutputDir\eval_report_existing_${timestamp}.json" 2>&1 | Tee-Object -Variable evalOutput
        
        $existingSw.Stop()
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ 现有评测集完成 (耗时: $([math]::Round($existingSw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
            $evalResults["existing"] = @{
                path = "$projectRoot\$OutputDir\eval_report_existing_${timestamp}.json"
                duration = $existingSw.Elapsed.TotalSeconds
                success = $true
            }
        } else {
            Write-Host "   ⚠️ 评测过程有错误，但继续执行" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "   ❌ 现有评测集运行异常: $_" -ForegroundColor Red
        $evalResults["existing"] = @{ success = $false; error = $_.ToString() }
    }
}

# Step 2: 盲测集 (35条)
if (-not $SkipBlind) {
    Write-Host ""
    Write-Host "━━━ Step 2: 运行盲测集 (35条) ━━━" -ForegroundColor Yellow
    
    $blindSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        dotnet run --project $projectPath -- --eval --eval-set "$projectRoot\Data\ComplianceBlindEvalSet.json" --output "$projectRoot\$OutputDir\eval_report_blind_${timestamp}.json" 2>&1 | Tee-Object -Variable blindOutput
        
        $blindSw.Stop()
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ 盲测集完成 (耗时: $([math]::Round($blindSw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
            $evalResults["blind"] = @{
                path = "$projectRoot\$OutputDir\eval_report_blind_${timestamp}.json"
                duration = $blindSw.Elapsed.TotalSeconds
                success = $true
            }
        } else {
            Write-Host "   ⚠️ 盲测集评测过程有错误" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "   ❌ 盲测集运行异常: $_" -ForegroundColor Red
        $evalResults["blind"] = @{ success = $false; error = $_.ToString() }
    }
}

# Step 3: 生成对比报告
Write-Host ""
Write-Host "━━━ Step 3: 生成对比报告 ━━━" -ForegroundColor Yellow

$summaryPath = "$projectRoot\$OutputDir\eval_summary_${timestamp}.md"
$reportJsonPath = "$projectRoot\$OutputDir\eval_pipeline_report_${timestamp}.json"

# 尝试解析 JSON 报告
$existingMetrics = $null
$blindMetrics = $null

if ($evalResults.ContainsKey("existing") -and $evalResults["existing"].success -and (Test-Path $evalResults["existing"].path)) {
    try {
        $existingJson = Get-Content $evalResults["existing"].path -Raw | ConvertFrom-Json
        $existingMetrics = @{
            total = $existingJson.total
            tool_call_rate = $existingJson.tool_call_rate
            parameter_accuracy = $existingJson.parameter_accuracy
            conclusion_accuracy = $existingJson.conclusion_accuracy
            mean_precision_at_k = $existingJson.mean_precision_at_k
            mean_faithfulness = $existingJson.mean_faithfulness
            mean_citation_accuracy = $existingJson.mean_citation_accuracy
            refusal_rate = $existingJson.refusal_rate
            hallucination_detection_rate = $existingJson.hallucination_detection_rate
            behavior_match_rate = $existingJson.behavior_match_rate
        }
    } catch {
        Write-Host "   ⚠️ 现有评测报告解析失败" -ForegroundColor Yellow
    }
}

if ($evalResults.ContainsKey("blind") -and $evalResults["blind"].success -and (Test-Path $evalResults["blind"].path)) {
    try {
        $blindJson = Get-Content $evalResults["blind"].path -Raw | ConvertFrom-Json
        $blindMetrics = @{
            total = $blindJson.total
            tool_call_rate = $blindJson.tool_call_rate
            parameter_accuracy = $blindJson.parameter_accuracy
            conclusion_accuracy = $blindJson.conclusion_accuracy
            mean_precision_at_k = $blindJson.mean_precision_at_k
            mean_faithfulness = $blindJson.mean_faithfulness
            mean_citation_accuracy = $blindJson.mean_citation_accuracy
            refusal_rate = $blindJson.refusal_rate
            hallucination_detection_rate = $blindJson.hallucination_detection_rate
            behavior_match_rate = $blindJson.behavior_match_rate
        }
    } catch {
        Write-Host "   ⚠️ 盲测报告解析失败" -ForegroundColor Yellow
    }
}

# 生成 Markdown 总结
$summaryContent = @"
# 化工合规 AI Agent 评测报告

- **评测时间**: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
- **模型**: $(if ($ModelConfig -eq 'default') { '默认配置' } else { $ModelConfig })
- **流水线版本**: Phase 6.4

---

## 评测结果对比

| 指标 | 现有评测集 (63条) | 盲测集 (35条) | 变化 |
|------|-------------------|---------------|------|
| 总用例数 | $($existingMetrics.total) | $($blindMetrics.total) | — |
| 工具触发率 | $("{0:F1}%" -f $existingMetrics.tool_call_rate) | $("{0:F1}%" -f $blindMetrics.tool_call_rate) | — |
| 参数准确率 | $("{0:F1}%" -f $existingMetrics.parameter_accuracy) | $("{0:F1}%" -f $blindMetrics.parameter_accuracy) | — |
| 结论准确率 | $("{0:F1}%" -f $existingMetrics.conclusion_accuracy) | $("{0:F1}%" -f $blindMetrics.conclusion_accuracy) | — |
| Precision@5 | $("{0:P1}" -f $existingMetrics.mean_precision_at_k) | $("{0:P1}" -f $blindMetrics.mean_precision_at_k) | — |
| 忠实度 | $("{0:P1}" -f $existingMetrics.mean_faithfulness) | $("{0:P1}" -f $blindMetrics.mean_faithfulness) | — |
| 引用准确率 | $("{0:P1}" -f $existingMetrics.mean_citation_accuracy) | $("{0:P1}" -f $blindMetrics.mean_citation_accuracy) | — |

### Phase 6 安全指标

| 安全指标 | 现有评测集 | 盲测集 | 说明 |
|----------|-----------|--------|------|
| 拒绝率 | $("{0:P1}" -f $existingMetrics.refusal_rate) | $("{0:P1}" -f $blindMetrics.refusal_rate) | 知识边界正确识别比例 |
| 幻觉检测率 | $("{0:P1}" -f $existingMetrics.hallucination_detection_rate) | $("{0:P1}" -f $blindMetrics.hallucination_detection_rate) | 输出校验器捕获比例 |
| 行为匹配率 | $("{0:P1}" -f $existingMetrics.behavior_match_rate) | $("{0:P1}" -f $blindMetrics.behavior_match_rate) | 预期行为一致性 |

---

## 分析要点

1. **泛化能力**: 盲测集包含50%分布外化学品，其结论准确率低于现有评测集属正常现象。
2. **拒绝率**: L4难度用例预期应触发拒绝行为，拒绝率应 > 15%（约5-6条）。
3. **行为匹配率**: 衡量系统行为是否符合预期(DB_HIT/REFUSAL)，> 80% 为合格。
4. **幻觉检测**: 输出校验器捕获的未验证引用，理想值接近0%。

## 详细报告文件

- 现有评测集: `$($evalResults["existing"].path)`
- 盲测集: `$($evalResults["blind"].path)`
- 流水线报告: `$reportJsonPath`

---

*自动生成于 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')*
"@

# 写入总结文件
$summaryContent | Out-File -FilePath $summaryPath -Encoding UTF8
Write-Host "   ✅ 评测总结已保存: $summaryPath" -ForegroundColor Green

# 生成 JSON 流水线报告
$pipelineReport = @{
    pipeline_version = "6.4"
    timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    model = $ModelConfig
    existing_eval = $existingMetrics
    blind_eval = $blindMetrics
    eval_results = $evalResults
    summary_md = $summaryPath
} | ConvertTo-Json -Depth 5

$pipelineReport | Out-File -FilePath $reportJsonPath -Encoding UTF8
Write-Host "   ✅ 流水线报告已保存: $reportJsonPath" -ForegroundColor Green

# Step 4: 输出最终摘要
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║              评 测 流 水 线 完 成                    ║" -ForegroundColor Cyan
Write-Host "╠══════════════════════════════════════════════════════╣" -ForegroundColor Cyan
if ($existingMetrics) {
    Write-Host "║  现有评测集 结论准确率: $("{0:F1}%" -f $existingMetrics.conclusion_accuracy)                        ║" -ForegroundColor White
}
if ($blindMetrics) {
    Write-Host "║  盲测集 结论准确率:     $("{0:F1}%" -f $blindMetrics.conclusion_accuracy)                        ║" -ForegroundColor White
    Write-Host "║  盲测集 拒绝率:         $("{0:F1}%" -f $blindMetrics.refusal_rate)                        ║" -ForegroundColor White
    Write-Host "║  盲测集 行为匹配率:     $("{0:F1}%" -f $blindMetrics.behavior_match_rate)                        ║" -ForegroundColor White
}
Write-Host "╠══════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║  总结: $summaryPath" -ForegroundColor White
Write-Host "║  报告: $reportJsonPath" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan

Write-Host ""
Write-Host "✅ 自动化评测流水线执行完毕" -ForegroundColor Green
