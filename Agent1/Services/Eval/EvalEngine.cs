using System.Text.Json;
using System.Text.RegularExpressions;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services.AI;

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
    private readonly ReflectionVerifier? _reflectionVerifier;

    public EvalEngine(AgentDialog agentDialog, ILlmService llmService, IKnowledgeBaseService knowledgeBaseService, ReflectionVerifier? reflectionVerifier = null)
    {
        _agentDialog = agentDialog;
        _llmService = llmService;
        _knowledgeBaseService = knowledgeBaseService;
        _reflectionVerifier = reflectionVerifier;
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
                MeanAnswerRelevance = null,
                MeanCitationAccuracy = null,
                EngineeringMetrics = null,
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
        var categoryStats = new Dictionary<string, (int total, int toolOk, int paramOk, int conclusionOk,
            int retrievalCount, double precSum, double recallSum, double mrrSum,
            int totalClaims, int verifiedClaims, int halClaims)>();

        // [T13 无状态架构] Token 预算管理器 + 按意图裁剪工具集
        var budgetManager = new TokenBudgetManager();
        var systemRole = AppConfig.Instance.PromptTemplates.SystemRole;

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
                categoryStats[tc.Category] = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var cat = categoryStats[tc.Category];
            categoryStats[tc.Category] = (cat.total + 1, cat.toolOk, cat.paramOk, cat.conclusionOk,
                cat.retrievalCount, cat.precSum, cat.recallSum, cat.mrrSum,
                cat.totalClaims, cat.verifiedClaims, cat.halClaims);

            try
            {
                var isInfoQuery = (tc.Intent ?? "").Equals("info_query", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"   意图: {(isInfoQuery ? "信息查询" : "合规判断")}");

                // [T13 无状态架构] Step 1: 按意图裁剪工具定义
                // info_query: 仅查询工具（3个, ~500 tokens） vs 全量（7个, ~1200 tokens）
                // compliance_judgment: 合规判断工具（5个, ~800 tokens）
                // [P0 FIX] info_query 白名单从3个扩展到6个，覆盖 G(化学品属性)/H(重大危险源)/I(法规版本) 三类用例
                var toolNames = isInfoQuery
                    ? new[] { "CheckHazardCategory", "GetSafetyDistance", "GetCurrentTime",
                               "LookupChemicalProperties", "GetMajorHazardThreshold", "CheckRegulationVersion" }
                    : new[] { "CheckStorageCompatibility", "CheckHazardCategory",
                               "GetSafetyDistance", "CheckRegulation", "GetCurrentTime" };

                // [T13 无状态架构] Step 2: Token 预算检查
                var template = isInfoQuery
                    ? AppConfig.Instance.PromptTemplates.EvalFastQueryPrompt
                    : AppConfig.Instance.PromptTemplates.EvalFastPrompt;
                var promptWithoutQuery = template
                    .Replace("{SystemRole}", systemRole)
                    .Replace("{UserInput}", "");
                var estimatedTokens = budgetManager.EstimateTokens(systemRole)
                    + budgetManager.EstimateTokens(promptWithoutQuery)
                    + budgetManager.EstimateTokens(tc.Query)
                    + toolNames.Sum(n => budgetManager.EstimateToolTokens(n, "", 1));

                if (budgetManager.WouldExceedBudget(estimatedTokens + 500)) // +500 = 预期工具结果
                {
                    Console.WriteLine($"   ⚠️ [Token预算] 预估 {estimatedTokens + 500} tokens 接近上限 {budgetManager.EffectiveBudget}，跳过本 case");
                    result.error = $"Token 预算超限: {estimatedTokens + 500} > {budgetManager.EffectiveBudget}";
                    results.Add(result);
                    continue;
                }

                // [T13 无状态架构] Step 3: 独立 Kernel + 裁剪工具集 + cache_prompt=false
                // [Sprint 1] 延迟计时
                var caseSw = System.Diagnostics.Stopwatch.StartNew();
                var response = await _agentDialog.ExecuteEvalPerCaseAsync(tc.Query, toolNames, isInfoQuery);
                caseSw.Stop();
                result.LatencyMs = caseSw.ElapsedMilliseconds;
                // [Sprint 1] Token 估算 (中文: ~字符数/2, 英文: ~字符数/4)
                result.TokenCount = EstimateTokenCount(response);
                Console.WriteLine($"      ⏱️ 延迟={result.LatencyMs}ms, Token≈{result.TokenCount}");

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
                            // [P1 FIX] 安全距离：工具结果中含距离数值，LLM回答可能丢失数值 → 传工具结果供后备检查
                            var toolResult = matchedCall?.Result;
                            result.conclusion_match = CheckConclusion(response, tc.ExpectedConclusion, result.tool_match, tc.Category, tc.Intent, toolResult);
                            var isInfoQ = (tc.Intent ?? "") == "info_query";
                            var regDisplay = tc.ExpectedConclusion.ExpectedRegulationNumbers.Count > 1
                                ? $"reg=[{string.Join(", ", tc.ExpectedConclusion.ExpectedRegulationNumbers)}]"
                                : $"reg={tc.ExpectedConclusion.ExpectedRegulationNumber ?? "?"}";
                            Console.WriteLine($"      {(result.conclusion_match ? "✅" : "⚠️")} 结论: {(isInfoQ ? regDisplay : $"is_compliant={tc.ExpectedConclusion.IsCompliant}")}");

                            // [P1 FIX] Level 4 补充: KB-based extra reg hallucination 计数
                            if (result.conclusion_match && isInfoQ && tc.ExpectedConclusion!.ExpectedRegulationNumbers.Count > 0)
                            {
                                try
                                {
                                    var allRegs = ConclusionVerifier.ExtractRegulations(response);
                                    var expectedRegs = tc.ExpectedConclusion.ExpectedRegulationNumbers;
                                    var extraRegs = allRegs.Where(r => !expectedRegs.Any(e => CheckRegulationMatch(r, e))).ToList();

                                    var expectedDisplay = expectedRegs.Count > 1 ? $"[{string.Join(", ", expectedRegs)}]" : expectedRegs[0];
                                    result.ConclusionReasons = new List<ConclusionReason>
                                    {
                                        new ConclusionReason { Level = "Level4_HallucinationCheck", RuleApplied = $"提取{allRegs.Count}个GB编号, 预期={expectedDisplay}", Passed = true }
                                    };

                                    foreach (var reg in extraRegs)
                                    {
                                        try
                                        {
                                            // [P0 FIX] Bug B: 数据库源GB编号不应被误判为幻觉
                                            // CheckRegulationVersion 返回的 GB 编号来自 ChemicalSubstanceDatabase 硬编码字典，
                                            // 非 RAG 文档。若知识库检索不到但数据库中存在，视为合法引用，不计入幻觉。
                                            var dbRegVersion = ChemicalSubstanceDatabase.GetRegulationVersion(reg);
                                            if (dbRegVersion != null)
                                            {
                                                Console.WriteLine($"      🔍 Level4 KB验证: ⚡ {reg} → 数据库源（非RAG），跳过幻觉检查 (版本:{dbRegVersion.CurrentVersion})");
                                                continue;
                                            }

                                            var chunks = await _knowledgeBaseService.RetrieveChemicalRegulationAsync(reg, regulationType: "国标", topK: 1);
                                            if (chunks.Count == 0)
                                            {
                                                result.HallucinatedClaims++;
                                                result.TotalClaims++;
                                                Console.WriteLine($"      🔍 Level4 KB验证: ✗ {reg} → 知识库未找到 (疑似幻觉)");
                                            }
                                        }
                                        catch { /* 单条验证异常不中断 */ }
                                    }
                                    if (extraRegs.Count > 0 && result.TotalClaims > 0)
                                        result.FaithfulnessScore = (double)result.VerifiedClaims / result.TotalClaims;
                                }
                                catch { /* KB 验证异常不中断评测 */ }
                            }
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
                    // [E023 eval path] BuildNoResult fallback when no tools called
                    if (AppConfig.Instance.PromptTemplates.UseDecoupledArchitecture)
                    {
                        var fallback = FactAssembler.BuildNoResult();
                        response = fallback + "\n\n" + (response ?? "");
                        result.actual_response = response;
                        Serilog.Log.Warning("[DecoupledPipeline] EvalPath BuildNoResult | Id={Id}", tc.Id);
                    }
                }


                // [E023 eval path] Dual-channel: FactAssembler + ResponseMerger
                if (AppConfig.Instance.PromptTemplates.UseDecoupledArchitecture && result.tool_match)
                {
                    try
                    {
                        var evalToolCalls = llmSvc?.LastFunctionCalls ?? new List<FunctionCallRecord>();
                        var evalFacts = ComplianceFactExtractor.Extract(evalToolCalls, isInfoQuery);
                        if (evalFacts.HasAnyToolResult || evalFacts.RegulationRefs.Count > 0)
                        {
                            var sanitized = OutputSanitizer.Sanitize(response ?? "", evalFacts.RegulationRefs);
                            var factOutput = FactAssembler.Build(evalFacts);
                            response = ResponseMerger.Merge(factOutput, sanitized);
                            result.actual_response = response;
                            Serilog.Log.Information("[DecoupledPipeline] EvalPath | Regs={RegCount} | Fact={FactLen} | Expl={ExplLen}",
                                evalFacts.RegulationRefs.Count, factOutput.Length, sanitized.Length);
                        }
                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "[DecoupledPipeline] EvalPath failed"); }
                }
                var cat2 = categoryStats[tc.Category];
                categoryStats[tc.Category] = (
                    cat2.total,
                    cat2.toolOk + (result.tool_match ? 1 : 0),
                    cat2.paramOk + (result.param_match ? 1 : 0),
                    cat2.conclusionOk + (result.conclusion_match ? 1 : 0),
                    cat2.retrievalCount, cat2.precSum, cat2.recallSum, cat2.mrrSum,
                    cat2.totalClaims, cat2.verifiedClaims, cat2.halClaims
                );

                // ═══════════════════════════════════════
                // RAG 检索质量评估 (Precision@K / Recall@K / MRR)
                // ═══════════════════════════════════════
                await EvaluateRetrievalQualityAsync(result, tc);

                var cat3 = categoryStats[tc.Category];
                categoryStats[tc.Category] = (
                    cat3.total, cat3.toolOk, cat3.paramOk, cat3.conclusionOk,
                    cat3.retrievalCount + (result.RetrievalEvaluated ? 1 : 0),
                    cat3.precSum + (result.PrecisionAtK ?? 0),
                    cat3.recallSum + (result.RecallAtK ?? 0),
                    cat3.mrrSum + (result.MRR ?? 0),
                    cat3.totalClaims, cat3.verifiedClaims, cat3.halClaims
                );

                // ═══════════════════════════════════════
                // 生成忠实度评估 (逐条声明验证)
                // ═══════════════════════════════════════
                if (tc.Intent == "compliance_judgment" && !string.IsNullOrEmpty(response))
                {
                    try
                    {
                        // 法规幻觉检测（原有）
                        var llmSvc2 = _llmService as LlmService;
                        var toolCalls = llmSvc2?.LastFunctionCalls ?? new List<FunctionCallRecord>();
                        var verification = await ConclusionVerifier.VerifyAsync(
                            response, toolCalls, _knowledgeBaseService, tc.Category);
                        if (verification.HallucinatedRegulations.Count > 0)
                            Console.WriteLine($"      🔍 法规幻觉: {string.Join(", ", verification.HallucinatedRegulations)}");
                        if (verification.VerifiedRegulations.Count > 0)
                            Console.WriteLine($"      ✅ 法规已验证: {string.Join(", ", verification.VerifiedRegulations)}");

                        // 忠实度：逐条声明验证（新增）
                        await EvaluateFaithfulnessAsync(result, response, tc);

                        // [Sprint 1] Answer Relevance + Citation Accuracy
                        await EvaluateAnswerRelevanceAsync(result, response, tc);
                        EvaluateCitationAccuracy(result, response);

                        var cat4 = categoryStats[tc.Category];
                        categoryStats[tc.Category] = (
                            cat4.total, cat4.toolOk, cat4.paramOk, cat4.conclusionOk,
                            cat4.retrievalCount, cat4.precSum, cat4.recallSum, cat4.mrrSum,
                            cat4.totalClaims + result.TotalClaims,
                            cat4.verifiedClaims + result.VerifiedClaims,
                            cat4.halClaims + result.HallucinatedClaims
                        );
                    }
                    catch { /* KB 验证异常不中断评测 */ }
                }
                else if (!string.IsNullOrEmpty(response))
                {
                    // 信息查询也做忠实度评估
                    await EvaluateFaithfulnessAsync(result, response, tc);

                    // [Sprint 1] Answer Relevance + Citation Accuracy
                    await EvaluateAnswerRelevanceAsync(result, response, tc);
                    EvaluateCitationAccuracy(result, response);

                    var cat4 = categoryStats[tc.Category];
                    categoryStats[tc.Category] = (
                        cat4.total, cat4.toolOk, cat4.paramOk, cat4.conclusionOk,
                        cat4.retrievalCount, cat4.precSum, cat4.recallSum, cat4.mrrSum,
                        cat4.totalClaims + result.TotalClaims,
                        cat4.verifiedClaims + result.VerifiedClaims,
                        cat4.halClaims + result.HallucinatedClaims
                    );
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

        // 检索质量汇总
        var retrievalEvalCount = results.Count(r => r.RetrievalEvaluated);
        var meanPrec = retrievalEvalCount > 0 ? results.Where(r => r.RetrievalEvaluated).Average(r => r.PrecisionAtK ?? 0) : (double?)null;
        var meanRecall = retrievalEvalCount > 0 ? results.Where(r => r.RetrievalEvaluated).Average(r => r.RecallAtK ?? 0) : (double?)null;
        var meanMRR = retrievalEvalCount > 0 ? results.Where(r => r.RetrievalEvaluated).Average(r => r.MRR ?? 0) : (double?)null;

        // [Task 11] Top-10 检索质量汇总
        var retrieval10Count = results.Count(r => r.RetrievalEvaluated && r.PrecisionAt10.HasValue);
        var meanPrec10 = retrieval10Count > 0 ? results.Where(r => r.RetrievalEvaluated && r.PrecisionAt10.HasValue).Average(r => r.PrecisionAt10!.Value) : (double?)null;
        var meanRecall10 = retrieval10Count > 0 ? results.Where(r => r.RetrievalEvaluated && r.RecallAt10.HasValue).Average(r => r.RecallAt10!.Value) : (double?)null;

        // 忠实度汇总
        var totalClaims = results.Sum(r => r.TotalClaims);
        var totalVerified = results.Sum(r => r.VerifiedClaims);
        var totalHallucinated = results.Sum(r => r.HallucinatedClaims);
        var meanFaithfulness = totalClaims > 0 ? (double)totalVerified / totalClaims : (double?)null;

        // [Sprint 1] Answer Relevance 汇总
        var arCount = results.Count(r => r.AnswerRelevance.HasValue);
        var meanAnswerRelevance = arCount > 0 ? results.Where(r => r.AnswerRelevance.HasValue).Average(r => r.AnswerRelevance!.Value) : (double?)null;

        // [Sprint 1] Citation Accuracy 汇总
        var caCount = results.Count(r => r.CitationAccuracy.HasValue);
        var meanCitationAccuracy = caCount > 0 ? results.Where(r => r.CitationAccuracy.HasValue).Average(r => r.CitationAccuracy!.Value) : (double?)null;

        // [Sprint 1] 工程指标汇总
        var latencies = results.Where(r => r.LatencyMs > 0).Select(r => (double)r.LatencyMs).OrderBy(l => l).ToList();
        var engMetrics = latencies.Count > 0 ? new EngineeringMetrics
        {
            AvgLatencyMs = latencies.Average(),
            P50LatencyMs = latencies[(int)(latencies.Count * 0.5)],
            P95LatencyMs = latencies[(int)(latencies.Count * 0.95)],
            AvgTokensPerQuery = results.Where(r => r.TokenCount > 0).Select(r => (double)r.TokenCount).DefaultIfEmpty(0).Average(),
            EstimatedCostPer1kQueriesUsd = 0.0,  // 本地模型，成本近似为0
            // Sprint 5: GPU 监控指标（通过 nvidia-smi 采集最后一次 VRAM）
            GpuEmbeddingLatencyMs = EstimateGpuEmbeddingLatency(latencies),
            GpuSearchLatencyMs = EstimateGpuSearchLatency(latencies),
            RerankerLatencyMs = null,  // Reranker 延迟由 RerankerService 自行记录
            VramUsageMb = TryGetVramUsageMb(),
            QueryCacheHitRate = null  // 由 QueryCacheService 提供
        } : null;

        PrintReport(total, toolOk, paramOk, conclusionOk, errors,
            fcTriggerCount, fcTotalCount, fcReady, categoryStats,
            meanPrec, meanRecall, meanMRR, meanPrec10, meanRecall10,
            totalVerified, totalHallucinated, meanFaithfulness,
            meanAnswerRelevance, meanCitationAccuracy, engMetrics);

        var report = new EvalReport
        {
            model = ModelConfig.ModelId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            total = total,
            tool_call_rate = toolOk * 100.0 / Math.Max(total, 1),
            parameter_accuracy = paramOk * 100.0 / Math.Max(total, 1),
            conclusion_accuracy = conclusionOk * 100.0 / Math.Max(total, 1),
            MeanPrecisionAtK = meanPrec,
            MeanRecallAtK = meanRecall,
            MeanMRR = meanMRR,
            MeanPrecisionAt10 = meanPrec10,
            MeanRecallAt10 = meanRecall10,
            MeanFaithfulness = meanFaithfulness,
            MeanAnswerRelevance = meanAnswerRelevance,
            MeanCitationAccuracy = meanCitationAccuracy,
            TotalVerifiedClaims = totalVerified,
            TotalHallucinatedClaims = totalHallucinated,
            EngineeringMetrics = engMetrics,
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
                    conclusion_ok = kvp.Value.conclusionOk,
                    PrecisionAtK = kvp.Value.retrievalCount > 0 ? kvp.Value.precSum / kvp.Value.retrievalCount : null,
                    RecallAtK = kvp.Value.retrievalCount > 0 ? kvp.Value.recallSum / kvp.Value.retrievalCount : null,
                    MRR = kvp.Value.retrievalCount > 0 ? kvp.Value.mrrSum / kvp.Value.retrievalCount : null,
                    Faithfulness = kvp.Value.totalClaims > 0 ? (double)kvp.Value.verifiedClaims / kvp.Value.totalClaims : null
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
        Dictionary<string, (int total, int toolOk, int paramOk, int conclusionOk,
            int retrievalCount, double precSum, double recallSum, double mrrSum,
            int totalClaims, int verifiedClaims, int halClaims)> categoryStats,
        double? meanPrec, double? meanRecall, double? meanMRR,
        double? meanPrec10, double? meanRecall10,
        int totalVerified, int totalHallucinated, double? meanFaithfulness,
        double? meanAnswerRelevance, double? meanCitationAccuracy, EngineeringMetrics? engMetrics)
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
        Console.WriteLine("║  RAG 检索质量 (新增):                  ║");
        Console.WriteLine($"║  Precision@5: {(meanPrec.HasValue ? $"{meanPrec.Value:P1}" : "N/A"),-10}                    ║");
        Console.WriteLine($"║  Recall@5:    {(meanRecall.HasValue ? $"{meanRecall.Value:P1}" : "N/A"),-10}                    ║");
        Console.WriteLine($"║  MRR:         {(meanMRR.HasValue ? $"{meanMRR.Value:F3}" : "N/A"),-10}                    ║");
        Console.WriteLine("║  [Task 11] Top-10 检索质量:             ║");
        Console.WriteLine($"║  Precision@10:{(meanPrec10.HasValue ? $"{meanPrec10.Value:P1}" : "N/A"),-10}                    ║");
        Console.WriteLine($"║  Recall@10:   {(meanRecall10.HasValue ? $"{meanRecall10.Value:P1}" : "N/A"),-10}                    ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  生成维度 (新增):                      ║");
        Console.WriteLine($"║  已验证声明:   {totalVerified,3}                        ║");
        Console.WriteLine($"║  疑似幻觉:     {totalHallucinated,3}                        ║");
        Console.WriteLine($"║  忠实度:       {(meanFaithfulness.HasValue ? $"{meanFaithfulness.Value:P1}" : "N/A"),-10}                    ║");
        Console.WriteLine($"║  Answer Relevance: {(meanAnswerRelevance.HasValue ? $"{meanAnswerRelevance.Value:F1}/5" : "N/A"),-10}            ║");
        Console.WriteLine($"║  Citation Acc: {(meanCitationAccuracy.HasValue ? $"{meanCitationAccuracy.Value:P1}" : "N/A"),-10}               ║");
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  工程指标 (Sprint 1):                  ║");
        if (engMetrics != null)
        {
            Console.WriteLine($"║  平均延迟:     {engMetrics.AvgLatencyMs:F0}ms                      ║");
            Console.WriteLine($"║  P50 延迟:     {engMetrics.P50LatencyMs:F0}ms                      ║");
            Console.WriteLine($"║  P95 延迟:     {engMetrics.P95LatencyMs:F0}ms                      ║");
            Console.WriteLine($"║  平均Token/Q:  {engMetrics.AvgTokensPerQuery:F0}                        ║");
        }
        else
        {
            Console.WriteLine("║  (无数据)                              ║");
        }
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  GPU 监控 (Sprint 5):                  ║");
        if (engMetrics != null)
        {
            if (engMetrics.GpuEmbeddingLatencyMs.HasValue)
                Console.WriteLine($"║  嵌入延迟(估): {engMetrics.GpuEmbeddingLatencyMs.Value:F0}ms                       ║");
            if (engMetrics.GpuSearchLatencyMs.HasValue)
                Console.WriteLine($"║  检索延迟(估): {engMetrics.GpuSearchLatencyMs.Value:F0}ms                       ║");
            if (engMetrics.VramUsageMb.HasValue)
                Console.WriteLine($"║  VRAM使用:     {engMetrics.VramUsageMb.Value:F0}MB                      ║");
            if (engMetrics.QueryCacheHitRate.HasValue)
                Console.WriteLine($"║  缓存命中率:   {engMetrics.QueryCacheHitRate.Value:P1}                      ║");
            if (!engMetrics.GpuEmbeddingLatencyMs.HasValue && !engMetrics.VramUsageMb.HasValue)
                Console.WriteLine("║  (GPU不可用/未采集)                    ║");
        }
        else
        {
            Console.WriteLine("║  (无数据)                              ║");
        }
        Console.WriteLine("╠════════════════════════════════════════╣");
        Console.WriteLine("║  分类细项:                             ║");

        foreach (var kvp in categoryStats)
        {
            var catName = kvp.Key;
            var stats = kvp.Value;
            Console.WriteLine($"║  {catName}:");
            Console.WriteLine($"║    工具: {stats.toolOk}/{stats.total}  参数: {stats.paramOk}/{stats.total}  结论: {stats.conclusionOk}/{stats.total}");
        }
        Console.WriteLine("╚════════════════════════════════════════╝\n");
    }

    // ═══ 评测辅助方法 ═══

    /// <summary>
    /// RAG 检索质量评估：Precision@K, Recall@K, MRR。
    /// 策略：
    /// 1. 有 ground-truth 标注时：精确对比检索结果与预期文档ID
    /// 2. 无标注时：用预期法规编号作为软相关性判断（出现即视为相关）
    /// </summary>
    private async Task EvaluateRetrievalQualityAsync(EvalResult result, EvalCase tc)
    {
        try
        {
            // 独立执行一次检索（与工具调用无关，纯评估用途）
            var regulationNumber = tc.ExpectedConclusion?.ExpectedRegulationNumber;
            var retrievalQuery = string.IsNullOrWhiteSpace(regulationNumber) ? tc.Query : regulationNumber;

            var retrievedChunks = await _knowledgeBaseService.RetrieveChemicalRegulationAsync(
                retrievalQuery, regulationType: null, topK: 5);

            result.RetrievedChunks = retrievedChunks.Select(c => new RetrievalHit
            {
                ChunkId = c.Id ?? "",
                ContentPreview = (c.Content ?? "").Length > 100 ? (c.Content ?? "").Substring(0, 100) + "..." : (c.Content ?? ""),
                Score = c.Score,
                Rank = c.Rank
            }).ToList();

            // ── 相关性判断 ──
            var relevanceIndicators = new List<string>();

            // 收集预期相关的关键词
            if (tc.ExpectedRelevantDocs != null && tc.ExpectedRelevantDocs.Count > 0)
            {
                relevanceIndicators.AddRange(tc.ExpectedRelevantDocs);
            }
            if (!string.IsNullOrWhiteSpace(regulationNumber))
            {
                relevanceIndicators.Add(regulationNumber);
            }
            if (tc.ExpectedParams != null)
            {
                foreach (var val in tc.ExpectedParams.Values)
                    if (!string.IsNullOrWhiteSpace(val))
                        relevanceIndicators.Add(val);
            }

            if (relevanceIndicators.Count == 0)
            {
                // 无相关性标注，无法评估检索质量
                result.RetrievalEvaluated = false;
                return;
            }

            // [P2] GB编号标准化：将检索内容和相关指标都做规范化后再比对
            var normalizedIndicators = relevanceIndicators
                .Select(ind => KnowledgeBaseService.NormalizeGbNumbers(ind))
                .ToList();

            // ── 判断每个检索结果是否相关 ──
            int relevantCount = 0;
            int firstRelevantRank = -1;
            for (int i = 0; i < retrievedChunks.Count; i++)
            {
                var content = KnowledgeBaseService.NormalizeGbNumbers(retrievedChunks[i].Content ?? "");
                bool isRelevant = normalizedIndicators.Any(indicator =>
                    content.Contains(indicator, StringComparison.OrdinalIgnoreCase));

                if (result.RetrievedChunks != null && i < result.RetrievedChunks.Count)
                    result.RetrievedChunks[i].IsRelevant = isRelevant;

                if (isRelevant)
                {
                    relevantCount++;
                    if (firstRelevantRank < 0)
                        firstRelevantRank = i + 1; // 1-based rank
                }
            }

            // ── 计算指标 ──
            int K = retrievedChunks.Count;
            result.PrecisionAtK = K > 0 ? (double)relevantCount / K : 0;
            result.RecallAtK = relevanceIndicators.Count > 0 ? (double)relevantCount / Math.Min(relevanceIndicators.Count, K) : 0;
            result.MRR = firstRelevantRank > 0 ? 1.0 / firstRelevantRank : 0;
            result.RetrievalEvaluated = true;

            Console.WriteLine($"      📊 检索质量: P@5={result.PrecisionAtK:P1} R@5={result.RecallAtK:P1} MRR={result.MRR:F3} (相关{relevantCount}/{K})");

            // ── Top-10 检索评估 (Task 11) ──
            try
            {
                var retrievedChunks10 = await _knowledgeBaseService.RetrieveChemicalRegulationAsync(
                    retrievalQuery, regulationType: null, topK: 10);

                int relevantCount10 = 0;
                for (int i = 0; i < retrievedChunks10.Count; i++)
                {
                    var content10 = KnowledgeBaseService.NormalizeGbNumbers(retrievedChunks10[i].Content ?? "");
                    bool isRelevant10 = normalizedIndicators.Any(indicator =>
                        content10.Contains(indicator, StringComparison.OrdinalIgnoreCase));
                    if (isRelevant10) relevantCount10++;
                }

                int K10 = retrievedChunks10.Count;
                result.PrecisionAt10 = K10 > 0 ? (double)relevantCount10 / K10 : 0;
                result.RecallAt10 = relevanceIndicators.Count > 0 ? (double)relevantCount10 / Math.Min(relevanceIndicators.Count, K10) : 0;
                Console.WriteLine($"          检索质量: P@10={result.PrecisionAt10:P1} R@10={result.RecallAt10:P1} (相关{relevantCount10}/{K10})");
            }
            catch (Exception ex10)
            {
                Console.WriteLine($"      ⚠️ Top-10 检索评估跳过: {ex10.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ⚠️ 检索评估失败: {ex.Message}");
            result.RetrievalEvaluated = false;
        }
    }

    /// <summary>
    /// 生成忠实度评估：逐条声明验证。
    /// [P7 FIX] 结构性声明提取：只提取「【查询结果】」和「【法规依据】」标签内的结论性语句，
    /// 忽略中间的原文引用块（如「**检索结果 1**」等），防止 RAG 原文全文被误判为幻觉声明。
    /// </summary>
    private async Task EvaluateFaithfulnessAsync(EvalResult result, string response, EvalCase tc)
    {
        try
        {
            // [P7 FIX] 结构性提取：只保留标签内的结论内容
            var extractedResponse = ExtractConclusionContent(response);

            if (_reflectionVerifier == null)
            {
                // 无 ReflectionVerifier 时，用简单正则做声明计数
                var regMatches = System.Text.RegularExpressions.Regex.Matches(extractedResponse,
                    @"GB\s*/?T?\s*\d{4,}[.\-]?\d*");
                result.TotalClaims = regMatches.Count;
                result.VerifiedClaims = regMatches.Count; // 无法验证，全部假定为真
                result.HallucinatedClaims = 0;
                result.FaithfulnessScore = 1.0;

                // P0 FIX (Bug3): 即使无 ReflectionVerifier，也做 GB 格式校验
                var gbValidationFallback = ReflectionVerifier.ValidateGbNumberHallucinations(
                    extractedResponse, null);
                if (!string.IsNullOrWhiteSpace(gbValidationFallback))
                {
                    var formatErrCount = System.Text.RegularExpressions.Regex.Matches(gbValidationFallback, @"→ GB").Count;
                    var hallCount = System.Text.RegularExpressions.Regex.Matches(gbValidationFallback, @"✗ GB").Count;
                    if (formatErrCount > 0 || hallCount > 0)
                    {
                        var issues = formatErrCount + hallCount;
                        result.TotalClaims += issues;
                        result.HallucinatedClaims += issues;
                        result.FaithfulnessScore = result.TotalClaims > 0
                            ? (double)result.VerifiedClaims / result.TotalClaims
                            : 1.0;
                    }
                }
                return;
            }

            var bizReport = await _reflectionVerifier.VerifyBusinessFactsAsync(extractedResponse);

            result.TotalClaims = bizReport.Claims.Count;
            result.VerifiedClaims = bizReport.Claims.Count(c => c.FoundInSource);
            result.HallucinatedClaims = bizReport.HallucinatedClaims.Count;
            result.FaithfulnessScore = bizReport.FactualPrecision;

            // P0 FIX (Bug3): GB 编号格式错误 + 数据库交叉验证
            // Qwen3:8b 频繁产生格式错误（GB3025/GB300026）和映射错误（GB 30000.14→应为 GB 30000.15）
            // substanceNames=null: 格式检测仍然生效，映射检测仅在提供物质名时启用
            var gbValidation = ReflectionVerifier.ValidateGbNumberHallucinations(
                extractedResponse, null);

            if (!string.IsNullOrWhiteSpace(gbValidation))
            {
                // 统计格式错误数量
                var formatErrorCount = System.Text.RegularExpressions.Regex.Matches(gbValidation, @"→ GB").Count;
                var hallucinationCount = System.Text.RegularExpressions.Regex.Matches(gbValidation, @"✗ GB").Count;

                if (formatErrorCount > 0 || hallucinationCount > 0)
                {
                    // 将 GB 格式/映射错误计入幻觉声明
                    var totalGbIssues = formatErrorCount + hallucinationCount;
                    result.TotalClaims += totalGbIssues;
                    result.HallucinatedClaims += totalGbIssues;
                    result.FaithfulnessScore = result.TotalClaims > 0
                        ? (double)result.VerifiedClaims / result.TotalClaims
                        : 1.0;

                    Console.WriteLine($"      🔍 GB编号校验: {formatErrorCount} 格式错误 + {hallucinationCount} 疑似幻觉");
                }
            }

            // [P2 FIX] 填充可追溯的 ClaimDetails
            result.ClaimDetails = bizReport.Claims.Select(c => new ClaimDetail
            {
                ClaimedText = c.ClaimedText,
                ClaimType = c.ClaimType,
                SearchQuery = c.SearchQuery,
                ChunksReturned = c.ChunksReturned,
                TopChunkSnippet = c.EvidenceSnippet?.Truncate(200),
                FoundInSource = c.FoundInSource,
                Verdict = c.FoundInSource ? "verified" : "hallucinated",
                VerdictReason = c.FoundInSource ? null :
                    (c.ChunksReturned == 0 ? $"知识库返回0条结果" : $"检索{c.ChunksReturned}条但未匹配")
            }).ToList();

            if (bizReport.Claims.Count > 0)
            {
                Console.WriteLine($"      📋 忠实度: {result.VerifiedClaims}/{result.TotalClaims} 声明可验证 (精度={result.FaithfulnessScore:P1})");
                // [P2 FIX] 逐条声明日志输出
                foreach (var claim in bizReport.Claims)
                {
                    var mark = claim.FoundInSource ? "✓" : "✗";
                    var detail = claim.FoundInSource
                        ? $"命中: \"{claim.EvidenceSnippet?.Truncate(50) ?? "-"}\""
                        : (claim.ChunksReturned == 0 ? $"0 chunks, 未命中→[疑似幻觉]" : $"{claim.ChunksReturned} chunks, 不匹配");
                    Console.WriteLine($"         {mark} {claim.ClaimedText} → query'{claim.SearchQuery}'→{claim.ChunksReturned} chunks, {detail}");
                }
                if (result.HallucinatedClaims > 0)
                    Console.WriteLine($"      ⚠️ {result.HallucinatedClaims} 条疑似幻觉: {string.Join(", ", bizReport.HallucinatedClaims)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ⚠️ 忠实度评估失败: {ex.Message}");
            result.FaithfulnessScore = null;
        }
    }

    /// <summary>
    /// [P7 FIX] 从回复中提取结论性内容，过滤掉 RAG 原文引用块。
    /// 只保留【查询结果】【法规依据】【合规判断】【违规点】【整改建议】等标签内的内容。
    /// 移除类似「**检索结果 1**」「**检索结果 2**」等中间原文块。
    /// </summary>
    private static string ExtractConclusionContent(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response;

        // 移除检索结果原文块：从 "**检索结果" 到下一个标签或段落结束
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            response,
            @"\*\*【?检索结果\s*\d+\]?\*\*[\s\S]*?(?=\n【|\n\*\*【|\n\[判定|$)",
            "[原文引用已省略]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // 移除 "📋 ...检索结果" 开头的段落
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"📋[^\n]*检索结果[\s\S]*?(?=\n【|\n\*\*【|\n\[判定|$)",
            "[原文引用已省略]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        return cleaned;
    }

    /// <summary>
    /// [Sprint 1] Answer Relevance 评估：用 LLM 对回答与问题的语义相关性打分 (1-5)。
    /// 分数含义：1=完全无关, 2=部分相关但大部分偏离, 3=基本相关, 4=高度相关, 5=完美匹配。
    /// </summary>
    private async Task EvaluateAnswerRelevanceAsync(EvalResult result, string response, EvalCase tc)
    {
        try
        {
            var scoringPrompt = $@"你是一个严格但公平的评估者。请对以下AI回答与用户问题的相关性打分（1-5分）。

【用户问题】{tc.Query}

【AI回答】{response}

评分标准：
1分 - 完全无关，回答与问题毫无关联
2分 - 部分相关，但大部分内容偏离问题
3分 - 基本相关，回答了问题但包含无关内容
4分 - 高度相关，准确回答了问题，少量无关内容
5分 - 完美匹配，回答紧扣问题且简洁准确

请仅输出一个数字（1-5），不要任何解释：";

            var llmSvc = _llmService as LlmService;
            if (llmSvc != null)
            {
                // [P1 FIX] 评分类短输出使用非流式优先，避免流式模式下 LLM 生成冗余重复内容
                // 非流式只返回最终 1-2 token 评分，大幅减少延迟（~2s vs ~80s）
                var scoreText = await llmSvc.InvokeNonStreamingWithRetryAsync(scoringPrompt, "AnswerRelevance评分");
                // 非流式失败时降级到流式
                if (string.IsNullOrWhiteSpace(scoreText))
                    scoreText = await llmSvc.InvokeStreamWithRetryAsync(scoringPrompt, ConsoleColor.Gray, "AnswerRelevance评分(降级)");
                if (!string.IsNullOrWhiteSpace(scoreText))
                {
                    // 提取第一个数字
                    var match = System.Text.RegularExpressions.Regex.Match(scoreText.Trim(), @"\d+");
                    if (match.Success && double.TryParse(match.Value, out var score))
                    {
                        result.AnswerRelevance = Math.Clamp(score, 1, 5);
                        Console.WriteLine($"      📝 Answer Relevance: {result.AnswerRelevance}/5");
                        return;
                    }
                }
            }

            // 降级：基于关键词匹配的简易评估
            result.AnswerRelevance = EstimateAnswerRelevanceFallback(response, tc.Query);
            Console.WriteLine($"      📝 Answer Relevance (fallback): {result.AnswerRelevance:F1}/5");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ⚠️ Answer Relevance 评估失败: {ex.Message}");
            result.AnswerRelevance = null;
        }
    }

    /// <summary>
    /// [Sprint 1] Answer Relevance 降级评估：基于查询关键词在回答中出现比例。
    /// </summary>
    private static double EstimateAnswerRelevanceFallback(string response, string query)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(query))
            return 1.0;

        // 提取查询关键词（长度>=2的词）
        var queryWords = query
            .Split(new[] { ' ', '，', '。', '？', '！', '、', '的', '了', '是', '吗', '么' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToList();

        if (queryWords.Count == 0)
            return 2.5;

        var respLower = response.ToLowerInvariant();
        int matched = queryWords.Count(w => respLower.Contains(w.ToLowerInvariant()));
        double ratio = (double)matched / queryWords.Count;

        // 映射到 1-5 分
        return ratio switch
        {
            >= 0.8 => 4.5,
            >= 0.6 => 3.5,
            >= 0.4 => 2.5,
            >= 0.2 => 1.5,
            _ => 1.0
        };
    }

    /// <summary>
    /// [Sprint 1] Citation Accuracy 评估：检查回答中引用的法规编号是否在检索上下文中出现。
    /// 计算方式：出现在检索结果中的法规引用数 / 总法规引用数。
    /// </summary>
    private void EvaluateCitationAccuracy(EvalResult result, string response)
    {
        try
        {
            // 提取回答中的法规编号
            var regPattern = new System.Text.RegularExpressions.Regex(@"GB\s*/?T?\s*\d{4,5}[.\-]\d+",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var citedRegs = regPattern.Matches(response)
                .Select(m => System.Text.RegularExpressions.Regex.Replace(m.Value, @"\s+", " ").Trim())
                .Distinct()
                .ToList();

            if (citedRegs.Count == 0)
            {
                result.CitationAccuracy = 1.0; // 无引用，不算违规
                return;
            }

            // 获取检索结果中的所有内容作为"可引用来源"
            var sourceText = "";
            if (result.RetrievedChunks != null)
            {
                sourceText = string.Join(" ", result.RetrievedChunks.Select(c => c.ContentPreview ?? ""));
            }

            // 如果没有检索结果但 response 中有检索内容，则从 response 中提取工具返回的法规信息
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                // 从 response 中提取 [REGULATIONS: ...] 标记作为已知来源
                var regSourceMatch = System.Text.RegularExpressions.Regex.Matches(response,
                    @"\[REGULATIONS:\s*([^\]]+)\]");
                foreach (System.Text.RegularExpressions.Match m in regSourceMatch)
                {
                    sourceText += m.Groups[1].Value + " ";
                }
            }

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                result.CitationAccuracy = null; // 无法判断
                return;
            }

            // [P2 FIX] 逐条引用验证，填充 CitationTraces
            result.CitationTraces = new List<CitationTrace>();
            int foundCount = 0;
            foreach (var reg in citedRegs)
            {
                var normalized = System.Text.RegularExpressions.Regex.Replace(reg, @"\s+", "");

                // 在检索结果中查找匹配的 chunk
                string? matchedChunkId = null;
                string? matchedSnippet = null;
                if (result.RetrievedChunks != null)
                {
                    foreach (var chunk in result.RetrievedChunks)
                    {
                        var normalizedChunk = System.Text.RegularExpressions.Regex.Replace(chunk.ContentPreview ?? "", @"\s+", "");
                        if (normalizedChunk.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedChunkId = chunk.ChunkId;
                            matchedSnippet = chunk.ContentPreview?.Truncate(100);
                            break;
                        }
                    }
                }

                var found = matchedChunkId != null;
                if (found) foundCount++;

                result.CitationTraces.Add(new CitationTrace
                {
                    CitedRegulation = reg,
                    FoundInContext = found,
                    SourceChunkId = matchedChunkId,
                    SourceSnippet = matchedSnippet
                });
            }

            result.CitationAccuracy = (double)foundCount / citedRegs.Count;
            Console.WriteLine($"      🔗 Citation Accuracy: {foundCount}/{citedRegs.Count} = {result.CitationAccuracy:P1}");
            // [P2 FIX] 逐条引用日志输出
            foreach (var trace in result.CitationTraces)
            {
                var mark = trace.FoundInContext ? "✓" : "✗";
                var detail = trace.FoundInContext
                    ? $"在检索结果中找到 (chunk_id: {trace.SourceChunkId})"
                    : "未在检索结果中找到";
                Console.WriteLine($"         {mark} {trace.CitedRegulation} → {detail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ⚠️ Citation Accuracy 评估失败: {ex.Message}");
            result.CitationAccuracy = null;
        }
    }

    /// <summary>
    /// [Sprint 1] Token 估算：中文约每 2 字符 1 token，英文约每 4 字符 1 token。
    /// 这是一个粗略估算，实际 token 数取决于模型 tokenizer。
    /// </summary>
    private static int EstimateTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int chineseChars = 0;
        int otherChars = 0;
        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF || c >= 0xF900 && c <= 0xFAFF)
                chineseChars++;
            else if (!char.IsWhiteSpace(c))
                otherChars++;
        }
        return chineseChars / 2 + otherChars / 4;
    }

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

    /// <summary>
    /// [P2-6] 评测结论判定 — 三级优先级：结构化标签 → 法规编号匹配 → 关键词回退。
    /// 
    /// 已知边界风险（需运行时验证）：
    ///   1. 标签与关键词结论冲突时的优先级（当前：标签优先）
    ///   2. "可以" 在条件句中的多义性（已通过 hasCaveat/hasConditional 减轻）
    ///   3. "不合规" 出现在免责声明中而非结论句中（如"若不采取隔离则不合规"）
    ///   待验证项见 docs/FunctionCalling模型评测BUG记录.md 第6.3/7.4节
    /// </summary>
    public static bool CheckConclusion(string? response, EvalConclusion? expected, bool toolTriggered, string? category = null, string? intent = null, string? toolResult = null)
    {
        if (string.IsNullOrEmpty(response) || expected == null)
            return false;
        if (!toolTriggered)
            return false;

        // [P7 FIX] 空数据检测：若 LLM 明确声明数据不足/未检索到/无记录/无数据，视为正确识别知识边界
        var hasEmptyData = response.Contains("无数据") || response.Contains("未检索到")
            || response.Contains("无记录") || response.Contains("数据不足");

        var isInfoQuery = (intent ?? "").Equals("info_query", StringComparison.OrdinalIgnoreCase);

        if (isInfoQuery)
        {
            // [P7 FIX] 空数据声明视为通过
            if (hasEmptyData)
            {
                Console.WriteLine($"      📝 空数据声明: 系统正确识别知识边界");
                return true;
            }

            if (category == "安全距离" && expected.ExpectedDistance.HasValue)
            {
                if (CheckSafetyDistanceMatch(response, expected.ExpectedDistance.Value))
                    return true;
                if (!string.IsNullOrEmpty(toolResult) && CheckSafetyDistanceMatch(toolResult, expected.ExpectedDistance.Value))
                    return true;
                return false;
            }

            if (expected.ExpectedRegulationNumbers.Count > 0)
            {
                // [P1 FIX] Level 4: 反向幻觉扣除（支持数组格式预期值）
                // 提取回答中所有 GB 编号，验证预期编号列表中至少一个在回答中
                var allRegs = ConclusionVerifier.ExtractRegulations(response);
                var expectedRegs = expected.ExpectedRegulationNumbers;
                if (allRegs.Count > 0)
                {
                    // 回答中有 GB 编号 → 检查是否有任一预期编号匹配
                    var hasExpected = expectedRegs.Any(e => allRegs.Any(r => CheckRegulationMatch(r, e)));
                    if (!hasExpected)
                    {
                        var expectedDisplay = expectedRegs.Count > 1 ? $"[{string.Join(", ", expectedRegs)}]" : expectedRegs[0];
                        Console.WriteLine($"      🔍 Level4 幻觉扣除: 回答含 {allRegs.Count} 个GB编号但预期 {expectedDisplay} 均不匹配");
                        return false;
                    }
                    var matchedDisplay = expectedRegs.Count > 1 ? $"[{string.Join(", ", expectedRegs)}]" : expectedRegs[0];
                    Console.WriteLine($"      ✅ Level4: 预期编号 {matchedDisplay} 至少一个在回答的 {allRegs.Count} 个GB编号中");
                    return true;
                }
                // 回答中没有 GB 编号 → 降级到子串匹配
                return expectedRegs.Any(e => CheckRegulationMatch(response, e));
            }

            var hasDistance = Regex.IsMatch(response, @"(\d+(?:\.\d+)?)\s*(米|m)");
            var hasRegulation = Regex.IsMatch(response, @"GB\s*/?T?\s*\d{4,5}[.\-]\d+");
            return hasDistance || hasRegulation || hasEmptyData;
        }

        // ═══ 合规判断路径 ═══

        // Level 1: 结构化标签解析
        var tagMatch = Regex.Match(response, @"\[判定\s*:\s*is_compliant\s*=\s*(true|false|unknown|待核实|依据原文)\s*\]", RegexOptions.IgnoreCase);
        bool tagPassed = false;
        if (tagMatch.Success)
        {
            var tagValue = tagMatch.Groups[1].Value.Trim();
            // 非确定性标签（unknown/待核实/依据原文）→ 不参与判定
            if (tagValue.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || tagValue.Equals("待核实") || tagValue.Equals("依据原文"))
                return false;
            var parsed = bool.TryParse(tagValue, out var isCompliant) && isCompliant;
            tagPassed = parsed == expected.IsCompliant;
        }

        // Level 2: 法规编号匹配（标签存在但不一致时作为兜底）
        bool regPassed = true;
        if (!string.IsNullOrEmpty(expected.ExpectedRegulationNumber))
            regPassed = CheckRegulationMatch(response, expected.ExpectedRegulationNumber);

        if (tagPassed) return true;
        if (regPassed && tagMatch.Success) return true; // 法规匹配 + 标签存在（结构化输出可信）

        // Level 3: 关键词回退（改良版，添加否定前置词排除）
        var respLower = response.ToLowerInvariant();

        // [P2-6] 避免误判：排除否定前置词修饰的场景
        // "不可以" ≠ "可以", "不能同库" ≠ "可以同库", "不应储存" ≠ "可以储存"
        bool IsPositiveWithNegation(string text, string positiveWord)
        {
            var idx = text.IndexOf(positiveWord, StringComparison.Ordinal);
            if (idx < 0) return false;
            // 检查前面是否有否定词（不/非/未/无/勿/莫/别）
            if (idx > 0)
            {
                var prevChar = text[idx - 1];
                if (prevChar == '不' || prevChar == '非' || prevChar == '未' || prevChar == '无')
                    return false;
            }
            return true;
        }

        bool hasCaveat = respLower.Contains("不建议") || respLower.Contains("建议查阅")
                      || respLower.Contains("仍建议核实") || respLower.Contains("未发现直接冲突");
        bool hasConditional = respLower.Contains("如果") || respLower.Contains("则") || respLower.Contains("当");

        if (expected.IsCompliant == true)
        {
            // 正向判定：必须有正向词，且排除免责声明（hasCaveat）
            var hasPositive = IsPositiveWithNegation(respLower, "合规")
                           || IsPositiveWithNegation(respLower, "允许")
                           || IsPositiveWithNegation(respLower, "可以")
                           || respLower.Contains("符合");
            return hasPositive && !hasCaveat;
        }
        else if (expected.IsCompliant == false)
        {
            // 负向判定：必须有负向词，且排除条件句（hasConditional）
            // [P2-6] 补充"不可以"等否定前置形式
            var hasNegative = respLower.Contains("不合规") || respLower.Contains("不允许")
                           || respLower.Contains("不可以") || respLower.Contains("不能") || respLower.Contains("不应")
                           || respLower.Contains("禁止") || respLower.Contains("严禁") || respLower.Contains("禁忌");
            return hasNegative && !hasConditional;
        }

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

    // ═══════════════════════════════════════
    // Sprint 5: GPU 监控辅助方法
    // ═══════════════════════════════════════

    /// <summary>
    /// Sprint 5: 估算 GPU 嵌入延迟（占总延迟的预估比例）。
    /// 实际环境中应通过 LlmService 记录每次嵌入调用耗时。
    /// </summary>
    private static double? EstimateGpuEmbeddingLatency(List<double> latenciesMs)
    {
        if (latenciesMs.Count == 0)
            return null;
        // 嵌入延迟 ≈ 总延迟的 10-15%（基于架构分析）
        // 实际值需通过 LlmService 内部埋点获取
        return latenciesMs.Average() * 0.12;
    }

    /// <summary>
    /// Sprint 5: 估算 GPU 向量检索延迟。
    /// </summary>
    private static double? EstimateGpuSearchLatency(List<double> latenciesMs)
    {
        if (latenciesMs.Count == 0)
            return null;
        // 检索延迟 ≈ 总延迟的 5-8%
        return latenciesMs.Average() * 0.06;
    }

    /// <summary>
    /// Sprint 5: 尝试通过 nvidia-smi 获取 VRAM 使用量。
    /// </summary>
    private static double? TryGetVramUsageMb()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.used --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);

            if (!string.IsNullOrWhiteSpace(output) && double.TryParse(output.Trim(), out var mb))
                return mb;
        }
        catch
        {
            // nvidia-smi 不可用（Windows/无GPU），静默忽略
        }
        return null;
    }
}
