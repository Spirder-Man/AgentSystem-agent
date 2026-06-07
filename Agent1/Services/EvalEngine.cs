using System.Text.Json;
using System.Text.RegularExpressions;
using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// Task 5: 评测引擎 — 从 Program.cs 提取的 50 条业务评测执行逻辑。
/// 支持 API 调用和 Console 调用两种模式。
/// 依赖 AgentDialog（执行评测）, ILlmService（FC 诊断）, IKnowledgeBaseService（后验证）。
/// </summary>
public class EvalEngine
{
    private readonly AgentDialog _agentDialog;
    private readonly ILlmService _llmService;
    private readonly IKnowledgeBaseService _knowledgeBaseService;

    public EvalEngine(AgentDialog agentDialog, ILlmService llmService, IKnowledgeBaseService knowledgeBaseService)
    {
        _agentDialog = agentDialog;
        _llmService = llmService;
        _knowledgeBaseService = knowledgeBaseService;
    }

    /// <summary>
    /// 执行完整评测流程：加载评测集 → FC 就绪性检查 → 逐条评测 → 生成报告。
    /// 返回 EvalReport 供 Console 或 API 使用。
    /// </summary>
    public async Task<EvalReport> RunComplianceEvalAsync(string? evalSetPath = null)
    {
        var evalConfig = AppConfig.Instance.Evaluation;
        var jsonPath = evalSetPath ?? Path.Combine(AppContext.BaseDirectory, evalConfig.EvalSetPath);

        Console.WriteLine("\n══════════════════════════════════════════════");
        Console.WriteLine("   化工合规AI Agent — 50条业务评测");
        Console.WriteLine("══════════════════════════════════════════════");
        Console.WriteLine($"   模型: {ModelConfig.ModelId}");
        Console.WriteLine($"   评测集: {jsonPath}");
        Console.WriteLine("   评估维度: 工具触发 + 参数提取 + 合规结论");
        Console.WriteLine("══════════════════════════════════════════════\n");

        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"❌ 评测集文件不存在: {jsonPath}");
            return new EvalReport { model = ModelConfig.ModelId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        }

        EvalMode.IsActive = true;

        var json = await File.ReadAllTextAsync(jsonPath);
        EvalSet evalSet;
        try
        {
            evalSet = JsonSerializer.Deserialize<EvalSet>(json) ?? new EvalSet();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 评测集 JSON 解析失败: {ex.Message}");
            EvalMode.IsActive = false;
            return new EvalReport { model = ModelConfig.ModelId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        }

        var cases = evalSet.TestCases;
        Console.WriteLine($"📋 加载评测集: {evalSet.Name} (v{evalSet.Version})");
        Console.WriteLine($"   共 {cases.Count} 条用例\n");

        // Layer 1: FC 就绪性检查
        var llmSvcLayer1 = _llmService as LlmService;
        bool fcReady = false;
        int fcTriggerCount = 0;
        int fcTotalCount = 5;
        string fcDetail = "";

        if (llmSvcLayer1 != null)
        {
            Console.WriteLine("🔍 前置检查: FC 就绪性验证...");
            var (passed, trigCnt, totalCnt, detail) = await llmSvcLayer1.RunFcReadinessCheckAsync();
            fcReady = passed;
            fcTriggerCount = trigCnt;
            fcTotalCount = totalCnt;
            fcDetail = detail;
            Console.WriteLine();
        }

        if (!fcReady)
        {
            Console.WriteLine("❌ FC 管线未就绪 — 5 条诊断用例均未触发任何工具调用。");
            Console.WriteLine("   可能原因: 模型不支持 Function Calling 或 Prompt 未引导工具使用。");
            Console.WriteLine("   建议: 先运行 menu 12「工具调用诊断验证」排查管线问题。");
            Console.WriteLine("   跳过业务评测，避免 50 条全量无效跑测。\n");

            var blockedReport = new EvalReport
            {
                model = ModelConfig.ModelId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                total = 0,
                tool_call_rate = 0,
                parameter_accuracy = 0,
                conclusion_accuracy = 0,
                fc_readiness = new FcReadinessStatus
                {
                    passed = false,
                    trigger_count = fcTriggerCount,
                    total_count = fcTotalCount,
                    detail = fcDetail
                },
                cases = new List<EvalResult>()
            };

            var blockedReportPath = Path.Combine(AppContext.BaseDirectory, evalConfig.OutputReportPath);
            var blockedReportDir = Path.GetDirectoryName(blockedReportPath);
            if (!string.IsNullOrEmpty(blockedReportDir) && !Directory.Exists(blockedReportDir))
                Directory.CreateDirectory(blockedReportDir);
            var blockedJson = JsonSerializer.Serialize(blockedReport, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(blockedReportPath, blockedJson);
            Console.WriteLine($"📄 诊断报告已保存: {blockedReportPath}\n");

            EvalMode.IsActive = false;
            return blockedReport;
        }

        Console.WriteLine($"   ✅ FC 就绪 ({fcTriggerCount}/{fcTotalCount} 触发)，进入业务评测...\n");

        var results = new List<EvalResult>();
        var categoryStats = new Dictionary<string, (int total, int toolOk, int paramOk, int conclusionOk)>();

        for (int i = 0; i < cases.Count; i++)
        {
            var tc = cases[i];
            Console.WriteLine($"━━━ [{tc.Id}] {tc.Category} ({i + 1}/{cases.Count}) ━━━");
            Console.WriteLine($"   查询: \"{tc.Query}\"");
            Console.WriteLine($"   预期工具: {tc.ExpectedTool}");

            var result = new EvalResult
            {
                id = tc.Id,
                category = tc.Category,
                query = tc.Query,
                expected_tool = tc.ExpectedTool
            };

            if (!categoryStats.ContainsKey(tc.Category))
                categoryStats[tc.Category] = (0, 0, 0, 0);
            var cat = categoryStats[tc.Category];
            categoryStats[tc.Category] = (cat.total + 1, cat.toolOk, cat.paramOk, cat.conclusionOk);

            try
            {
                var isInfoQuery = (tc.Intent ?? "").Equals("info_query", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"   意图: {(isInfoQuery ? "信息查询" : "合规判断")}");
                var response = isInfoQuery
                    ? await _agentDialog.ExecuteEvalFastQueryAsync(tc.Query)
                    : await _agentDialog.ExecuteEvalFastAsync(tc.Query);
                result.actual_response = response ?? "";

                var llmSvc = _llmService as LlmService;
                if (llmSvc != null && llmSvc.LastFunctionCalls.Count > 0)
                {
                    var calledTools = llmSvc.LastFunctionCalls.Select(fc => fc.FunctionName).ToList();
                    result.actual_tools = string.Join(",", calledTools);

                    if (calledTools.Contains(tc.ExpectedTool, StringComparer.OrdinalIgnoreCase))
                    {
                        result.tool_match = true;
                        Console.WriteLine($"   ✅ 工具触发: {result.actual_tools}");

                        var matchedCall = llmSvc.LastFunctionCalls
                            .FirstOrDefault(fc => fc.FunctionName.Equals(tc.ExpectedTool, StringComparison.OrdinalIgnoreCase));
                        if (matchedCall != null && tc.ExpectedParams != null)
                        {
                            result.actual_params = matchedCall.Arguments ?? "";
                            result.param_match = CheckParams(matchedCall.Arguments, tc.ExpectedParams);
                            Console.WriteLine($"      {(result.param_match ? "✅" : "⚠️")} 参数: {result.actual_params}");
                        }

                        if (tc.ExpectedConclusion != null)
                        {
                            result.conclusion_match = CheckConclusion(response, tc.ExpectedConclusion, result.tool_match, tc.Category, tc.Intent);
                            var isInfoQ = (tc.Intent ?? "") == "info_query";
                            Console.WriteLine($"      {(result.conclusion_match ? "✅" : "⚠️")} 结论: {(isInfoQ ? $"reg={tc.ExpectedConclusion.ExpectedRegulationNumber ?? "?"}" : $"is_compliant={tc.ExpectedConclusion.IsCompliant}")}");
                        }
                    }
                    else
                    {
                        result.tool_match = false;
                        Console.WriteLine($"   ❌ 工具错误: 预期 {tc.ExpectedTool}, 实际 {result.actual_tools}");
                    }
                }
                else
                {
                    result.tool_match = false;
                    result.actual_tools = "(无工具调用)";
                    Console.WriteLine($"   ❌ 未触发任何工具");
                }

                var cat2 = categoryStats[tc.Category];
                categoryStats[tc.Category] = (
                    cat2.total,
                    cat2.toolOk + (result.tool_match ? 1 : 0),
                    cat2.paramOk + (result.param_match ? 1 : 0),
                    cat2.conclusionOk + (result.conclusion_match ? 1 : 0)
                );

                // Post-hoc KB 反向验证
                if (tc.Intent == "compliance_judgment" && !string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var llmSvc2 = _llmService as LlmService;
                        var toolCalls = llmSvc2?.LastFunctionCalls ?? new List<FunctionCallRecord>();
                        var verification = await ConclusionVerifier.VerifyAsync(
                            response, toolCalls, _knowledgeBaseService, tc.Category);
                        if (verification.HallucinatedRegulations.Count > 0)
                            Console.WriteLine($"      🔍 法规幻觉: {string.Join(", ", verification.HallucinatedRegulations)}");
                        if (verification.VerifiedRegulations.Count > 0)
                            Console.WriteLine($"      ✅ 法规已验证: {string.Join(", ", verification.VerifiedRegulations)}");
                    }
                    catch { /* KB 验证异常不中断评测 */ }
                }
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
                Console.WriteLine($"   ❌ 异常: {ex.Message}");
            }

            results.Add(result);
            await Task.Delay(evalConfig.CaseIntervalMs);
        }

        // ═══ 输出评测报告 ═══
        var total = results.Count;
        var toolOk = results.Count(r => r.tool_match);
        var paramOk = results.Count(r => r.param_match);
        var conclusionOk = results.Count(r => r.conclusion_match);
        var errors = results.Count(r => !string.IsNullOrEmpty(r.error));

        PrintReport(total, toolOk, paramOk, conclusionOk, errors, fcTriggerCount, fcTotalCount, fcReady, categoryStats);

        var report = new EvalReport
        {
            model = ModelConfig.ModelId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            total = total,
            tool_call_rate = toolOk * 100.0 / Math.Max(total, 1),
            parameter_accuracy = paramOk * 100.0 / Math.Max(total, 1),
            conclusion_accuracy = conclusionOk * 100.0 / Math.Max(total, 1),
            fc_readiness = new FcReadinessStatus
            {
                passed = fcReady,
                trigger_count = fcTriggerCount,
                total_count = fcTotalCount,
                detail = fcDetail
            },
            category_breakdown = categoryStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new CategoryMetric
                {
                    total = kvp.Value.total,
                    tool_ok = kvp.Value.toolOk,
                    param_ok = kvp.Value.paramOk,
                    conclusion_ok = kvp.Value.conclusionOk
                }
            ),
            cases = results
        };

        var reportPath = Path.Combine(AppContext.BaseDirectory, evalConfig.OutputReportPath);
        var reportDir = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
            Directory.CreateDirectory(reportDir);
        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(reportPath, reportJson);
        Console.WriteLine($"📄 详细报告已保存: {reportPath}");

        // 模型评级
        var avgScore = (toolOk * 100.0 / Math.Max(total, 1)
                      + paramOk * 100.0 / Math.Max(total, 1)
                      + conclusionOk * 100.0 / Math.Max(total, 1)) / 3.0;
        Console.Write("\n📊 综合评级: ");
        if (avgScore >= 85) Console.WriteLine("★★★ 优秀 — 可投入生产使用");
        else if (avgScore >= 70) Console.WriteLine("★★☆ 良好 — 建议针对性优化后上线");
        else if (avgScore >= 55) Console.WriteLine("★☆☆ 可用 — 需进一步 Prompt/模型调优");
        else Console.WriteLine("☆☆☆ 不合格 — 建议更换模型或架构方案");
        Console.WriteLine($"   (工具触发率 × 参数准确率 × 结论准确率 综合: {avgScore:F1}%)\n");

        EvalMode.IsActive = false;
        return report;
    }

    private static void PrintReport(int total, int toolOk, int paramOk, int conclusionOk, int errors,
        int fcTriggerCount, int fcTotalCount, bool fcReady,
        Dictionary<string, (int total, int toolOk, int paramOk, int conclusionOk)> categoryStats)
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║         业 务 评 测 报 告              ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine($"║  FC 就绪性:   {fcTriggerCount}/{fcTotalCount} → {(fcReady ? "通过 ✅" : "未通过 ❌")}             ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine($"║  总用例数:     {total,3}                        ║");
        Console.WriteLine($"║  成功执行:     {total - errors,3}                        ║");
        Console.WriteLine($"║  异常:         {errors,3}                        ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  核心业务指标:                         ║");
        Console.WriteLine($"║  工具触发率:   {toolOk,3}/{total} = {toolOk * 100.0 / Math.Max(total, 1):F1}%               ║");
        Console.WriteLine($"║  参数准确率:   {paramOk,3}/{total} = {paramOk * 100.0 / Math.Max(total, 1):F1}%               ║");
        Console.WriteLine($"║  结论准确率:   {conclusionOk,3}/{total} = {conclusionOk * 100.0 / Math.Max(total, 1):F1}%               ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  分类细项:                             ║");

        foreach (var kvp in categoryStats)
        {
            var catName = kvp.Key;
            var (catTotal, catTool, catParam, catConcl) = kvp.Value;
            Console.WriteLine($"║  {catName}:");
            Console.WriteLine($"║    工具: {catTool}/{catTotal}  参数: {catParam}/{catTotal}  结论: {catConcl}/{catTotal}");
        }
        Console.WriteLine("╚════════════════════════════════════════╝\n");
    }

    // ═══ 评测辅助方法 ═══

    public static bool CheckParams(string? actualArgs, Dictionary<string, string>? expected)
    {
        if (string.IsNullOrEmpty(actualArgs) || expected == null || expected.Count == 0)
            return false;
        var argsLower = actualArgs.ToLowerInvariant();
        foreach (var kvp in expected)
            if (argsLower.Contains(kvp.Value.ToLowerInvariant()))
                return true;
        return false;
    }

    public static bool CheckConclusion(string? response, EvalConclusion? expected, bool toolTriggered, string? category = null, string? intent = null)
    {
        if (string.IsNullOrEmpty(response) || expected == null)
            return false;
        if (!toolTriggered)
            return false;

        var isInfoQuery = (intent ?? "").Equals("info_query", StringComparison.OrdinalIgnoreCase);

        if (isInfoQuery)
        {
            if (category == "安全距离" && expected.ExpectedDistance.HasValue)
                return CheckSafetyDistanceMatch(response, expected.ExpectedDistance.Value);

            if (!string.IsNullOrEmpty(expected.ExpectedRegulationNumber))
                return CheckRegulationMatch(response, expected.ExpectedRegulationNumber);

            var hasDistance = Regex.IsMatch(response, @"(\d+(?:\.\d+)?)\s*(米|m)");
            var hasRegulation = Regex.IsMatch(response, @"GB\s*/?T?\s*\d{4,5}[.\-]\d+");
            var hasDataInsufficient = response.Contains("数据不足") || response.Contains("未检索到");
            return hasDistance || hasRegulation || hasDataInsufficient;
        }

        // 合规判断路径
        var tagMatch = Regex.Match(response, @"\[判定\s*:\s*is_compliant\s*=\s*(true|false|unknown|待核实|依据原文)\s*\]", RegexOptions.IgnoreCase);
        bool tagPassed = false;
        if (tagMatch.Success)
        {
            var tagValue = tagMatch.Groups[1].Value.Trim();
            if (tagValue.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || tagValue.Equals("待核实") || tagValue.Equals("依据原文"))
                return false;
            var parsed = bool.TryParse(tagValue, out var isCompliant) && isCompliant;
            tagPassed = parsed == expected.IsCompliant;
        }

        bool regPassed = true;
        if (!string.IsNullOrEmpty(expected.ExpectedRegulationNumber))
            regPassed = CheckRegulationMatch(response, expected.ExpectedRegulationNumber);

        if (tagPassed) return true;
        if (regPassed && tagMatch.Success) return true;

        var respLower = response.ToLowerInvariant();
        bool hasCaveat = respLower.Contains("不建议") || respLower.Contains("建议查阅")
                      || respLower.Contains("仍建议核实") || respLower.Contains("未发现直接冲突");
        bool hasConditional = respLower.Contains("如果") || respLower.Contains("则") || respLower.Contains("当");

        if (expected.IsCompliant == true)
            return (respLower.Contains("合规") || respLower.Contains("允许") || respLower.Contains("可以")) && !hasCaveat;
        else if (expected.IsCompliant == false)
            return (respLower.Contains("不合规") || respLower.Contains("不允许")
                 || respLower.Contains("禁止") || respLower.Contains("严禁") || respLower.Contains("禁忌")) && !hasConditional;

        return false;
    }

    public static bool CheckSafetyDistanceMatch(string response, double expectedDistance)
    {
        var match = Regex.Match(response, @"(\d+(?:\.\d+)?)\s*(米|m)");
        if (!match.Success) return false;
        if (double.TryParse(match.Groups[1].Value, out var actualDistance))
        {
            var tolerance = Math.Max(expectedDistance * 0.05, 1.0);
            return Math.Abs(actualDistance - expectedDistance) <= tolerance;
        }
        return false;
    }

    public static bool CheckRegulationMatch(string response, string expectedRegNumber)
    {
        var normalizedResponse = Regex.Replace(response, @"GB\s*/?T?\s*", "GB");
        var normalizedExpected = Regex.Replace(expectedRegNumber, @"GB\s*/?T?\s*", "GB");
        return normalizedResponse.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }
}
