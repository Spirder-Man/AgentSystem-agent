
using Agent1.Services;
using Agent1.Models;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public class RunReflectionStreamTools
    {
        private readonly ILlmService _llmService;
        private readonly ISessionService _sessionService;
        private readonly SessionContext _session;
        /// <summary>
        /// Phase 2d: AgentDialog（统一 ReAct 循环 + 工具链），null 时走旧 Reflection 逻辑
        /// </summary>
        private readonly AgentDialog? _agentDialog;
        /// <summary>
        /// Phase 2e: 代码级反思验证引擎（非 LLM，基于正则+知识库反向检索）
        /// </summary>
        private readonly ReflectionVerifier? _verifier;

        public RunReflectionStreamTools(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
        {
            _llmService = llmService;
            _sessionService = sessionService;
            _agentDialog = agentDialog;
            _verifier = kbService != null ? new ReflectionVerifier(kbService) : null;
            _session = _sessionService.CreateSession(SessionType.ChemicalCompliance);
        }

        public async Task RunReflectionStreamTool()
        {
            // Phase 2d: 优先走 AgentDialog 统一工具链 + 自研 Reflection 反思层
            if (_agentDialog != null)
            {
                await RunWithAgentDialog();
                return;
            }

            // 旧逻辑（AgentDialog 未注入时的降级）
            Console.WriteLine("\n====Reflection（化工合规自我纠错·多轮对话）====");
            Console.WriteLine($"✅ 会话已创建，Session ID: {_session.SessionId}");
            Console.WriteLine("💡 输入 'exit' 或 'quit' 退出对话");
            Console.WriteLine("-----------------------------------");

            var complianceTools = new ChemicalComplianceTools();

            while (true)
            {
                Console.Write("\n👤 请输入: ");
                var userInput = Console.ReadLine();

                if (userInput == null) continue;
                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("退出", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("🚪 退出对话");
                    break;
                }

                _sessionService.AddDialogTurn(_session.SessionId, "User", userInput);

                Console.WriteLine($"【用户提问】{userInput}");

                Console.WriteLine("\n【Step 1 - Thought】模型分析需要调用的工具");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);
                string thoughtPrompt = $@"【对话历史】
{history}
【当前问题】{userInput}

【可用工具】
1. CheckHazardCategory(危化品名称) - 查询危险类别及适用国标
2. CheckStorageCompatibility(危化品A, 危化品B) - 检查两种危化品可否同库储存
3. GetSafetyDistance(设施类型) - 查询安全间距要求（如储罐间距、消防通道宽度）
4. GetCurrentTime() - 获取当前时间
5. Calculate(表达式) - 数学计算

请输出你的思考过程，最后必须单独一行以大写 TOOLS: 开头列出工具名，逗号分隔。
格式示例：TOOLS:CheckHazardCategory,CheckStorageCompatibility
（务必以 TOOLS: 开头，否则工具不会被调用！）";

                string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "分析思考",
                    fcBehavior: FunctionChoiceBehavior.Auto());
                Console.ResetColor();

                Console.WriteLine("\n【Step 2 - Action】解析工具调用指令");
                string[] toolsToCall = ParseToolCalls(thoughtResult);
                if (toolsToCall.Length == 0)
                {
                    Console.WriteLine("⚠️ 模型未指定需要调用的工具，将默认调用合规工具");
                    toolsToCall = new string[] { "GetSafetyDistance" };
                }

                Console.WriteLine("\n【Step 3 - Observation】调用化工合规工具获取数据");
                Console.ForegroundColor = ConsoleColor.Green;
                Dictionary<string, string> toolResults = new Dictionary<string, string>();
                foreach (string toolName in toolsToCall)
                {
                    string result = await CallTool(complianceTools, toolName, userInput);
                    toolResults.Add(toolName, result);
                    Console.WriteLine($"✓ {toolName} → {result}");
                }
                Console.ResetColor();

                Console.WriteLine("\n【Step 4 - Initial Conclusion】生成初步诊断结论（未反思）");
                Console.ForegroundColor = ConsoleColor.Yellow;
                string observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));
                string initialPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【工具调用结果】
{observationSummary}
【要求】基于工具数据判断是否合规，引用法规条款，给出整改建议。";

                string initialConclusion = await _llmService.InvokeStreamWithRetryAsync(initialPrompt, ConsoleColor.Yellow, "初步结论",
                    fcBehavior: FunctionChoiceBehavior.None());
                Console.ResetColor();

                Console.WriteLine("\n【Step 5 - Reflection】自我反思（检查纠错）");
                Console.ForegroundColor = ConsoleColor.Magenta;
                string reflectionPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【初步结论】
{initialConclusion}
【工具调用真实数据】
{observationSummary}
【任务】对初步结论进行严格检查，按以下维度反思：
1. 数据真实性：是否完全基于真实工具数据？有无编造？
2. 结论严谨性：合规判断是否引用了具体法规条款？
3. 建议落地性：整改建议是否具体可操作？
【输出格式】逐条指出问题，最后用【纠错指令】: 具体修改方向 总结。";

                string reflectionResult = await _llmService.InvokeStreamWithRetryAsync(reflectionPrompt, ConsoleColor.Magenta, "反思检查",
                    fcBehavior: FunctionChoiceBehavior.None());
                Console.ResetColor();

                Console.WriteLine("\n【Step 6 - Final Conclusion】纠错后的最终合规审核结论");
                Console.ForegroundColor = ConsoleColor.Blue;
                string finalPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【工具调用真实数据】
{observationSummary}
【初步结论】
{initialConclusion}
【反思纠错结果】
{reflectionResult}
【要求】
1. 严格修正反思指出的所有问题
2. 完全基于真实工具数据，禁止编造
3. 合规判断引用具体法规条款
4. 整改建议具体可落地";

                var answer = await _llmService.InvokeStreamAsync(finalPrompt, ConsoleColor.Blue,
                    fcBehavior: FunctionChoiceBehavior.None());

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", "已生成诊断结论");

                Console.ResetColor();
                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ Reflection化工合规自我纠错流程执行完成！");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }

        /// <summary>
        /// Phase 2d: AgentDialog 统一工具链 + Reflection 反思层叠加
        /// </summary>
        private async Task RunWithAgentDialog()
        {
            Console.WriteLine("\n====Reflection（AgentDialog 工具链 + 自我纠错）====");
            Console.WriteLine($"✅ 会话已创建，Session ID: {_session.SessionId}");
            Console.WriteLine("💡 输入 'exit' 或 'quit' 退出对话");
            Console.WriteLine("-----------------------------------");

            while (true)
            {
                Console.Write("\n👤 请输入: ");
                var userInput = Console.ReadLine();

                if (userInput == null) continue;
                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("退出", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("🚪 退出对话");
                    break;
                }

                // Step 1-3: AgentDialog 处理工具调用 + 生成初步结论
                Console.WriteLine("\n📦 AgentDialog 统一工具链处理中...");
                var execResult = await _agentDialog!.ExecuteAsync(userInput, _session);
                var initialConclusion = execResult.DisplayOutput;

                // Step 4: 代码级事实核查（ReflectionVerifier — 非 LLM！）
                if (_verifier != null)
                {
                    Console.WriteLine("\n═══════════ 代码级事实核查 ═══════════");

                    // 4a. 业务验证：法规编号反向检索
                    Console.Write("🔍 验证结论中的法规引用... ");
                    var bizReport = await _verifier.VerifyBusinessFactsAsync(initialConclusion);
                    Console.WriteLine($"完成 ({bizReport.Claims.Count}条声明)");

                    // 4b. 系统健康：工具链完整性（从 CliExecutionResult 读取，避免可变状态竞态）
                    Console.Write("🔧 检查系统健康... ");
                    var toolResults = new Dictionary<string, string>();
                    foreach (var tc in execResult.ToolCalls)
                        toolResults[tc.FunctionName] = tc.Result ?? "(无返回)";
                    var sysReport = _verifier.VerifySystemHealth(
                        toolResults,
                        new ToolPlan { NeedsTools = execResult.ToolCalls.Count > 0, ToolNames = execResult.ToolCalls.Select(tc => tc.FunctionName).ToList() });
                    Console.WriteLine($"完成 (执行:{sysReport.ToolsExecuted} 取消:{sysReport.ToolsCancelled})");

                    // 4c. [Tier 2] 结论完整性评估（在输出核查报告前）
                    bizReport.Completeness = ReflectionVerifier.AssessCompleteness(initialConclusion);

                    // 4d. 输出核查报告
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine(bizReport.ToMarkdown());
                    Console.WriteLine(sysReport.ToMarkdown());
                    Console.ResetColor();

                    // 4e. 分层反思处理
                    var hasPrecisionIssue = bizReport.FactualPrecision < 1.0 || sysReport.ToolsCancelled > 0;
                    var needsEnrichment = bizReport.Completeness?.NeedsEnrichment == true;

                    if (hasPrecisionIssue)
                    {
                        // Tier 1 + 修正: 精度问题 → 使用 BuildCorrectedPrompt（已集成Tier1建议+Tier2完整性提示）
                        Console.WriteLine("\n⚠️ 核查发现问题，基于客观报告修正结论...");
                        Console.ForegroundColor = ConsoleColor.Blue;
                        var correctedPrompt = _verifier.BuildCorrectedPrompt(
                            userInput, initialConclusion, bizReport, sysReport);
                        Console.WriteLine();
                        await _llmService.InvokeStreamAsync(correctedPrompt, ConsoleColor.Blue,
                            fcBehavior: FunctionChoiceBehavior.None());
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                    else if (needsEnrichment)
                    {
                        // Tier 2 富化: 精度通过但结论单薄 → 使用 BuildEnrichedPrompt 补充维度
                        Console.WriteLine($"\n📝 核查精度通过，但结论完整度偏低 ({bizReport.Completeness!.Score}/10)，触发 Tier 2 富化...");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        var enrichedPrompt = _verifier.BuildEnrichedPrompt(
                            userInput, initialConclusion, bizReport);
                        Console.WriteLine();
                        await _llmService.InvokeStreamAsync(enrichedPrompt, ConsoleColor.Cyan,
                            fcBehavior: FunctionChoiceBehavior.None());
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ 核查通过 (精度: {bizReport.FactualPrecision*100:F0}%, 完整度: {bizReport.Completeness?.Score ?? 10}/10)，结论所有法规引用均在知识库中得到验证");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // 降级：无验证层时，LLM 自我反思（旧逻辑）
                    Console.WriteLine("\n【Reflection 反思层】检查结论是否基于真实数据");
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    string reflectionPrompt = $@"你是化工园区危化品合规审核专家。禁止输出思考过程。

【初步结论】
{initialConclusion}

逐条检查（只输出问题，无问题写「无」）：
1. 数据真实性：是否编造？
2. 法规引用：是否引用具体标准编号？
3. 建议可行性：是否具体可操作？

最后一行必须输出：【纠错指令】: 具体修改方向（无问题则写「无需修正」）";

                    string reflectionResult = await _llmService.InvokeStreamWithRetryAsync(reflectionPrompt, ConsoleColor.Magenta, "Reflection反思",
                        fcBehavior: FunctionChoiceBehavior.None());
                    Console.ResetColor();

                    Console.WriteLine();
                    if (reflectionResult.Contains("无需修正") || (reflectionResult.Contains("无问题") && !reflectionResult.Contains("问题：")))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Reflection 通过，结论无需修正");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("\n【修正结论】基于反思结果重新生成");
                        Console.ForegroundColor = ConsoleColor.Blue;
                        string finalPrompt = $@"你是化工园区危化品合规审核专家。禁止输出思考过程。

【当前问题】{userInput}

【初步结论】
{initialConclusion}

【反思纠错】
{reflectionResult}

严格修正反思指出的问题，按以下模板输出：
【合规判断】是/否
【法规依据】引用具体标准编号+条款
【违规点】若无违规写「无」
【整改建议】若无违规则写「无需整改」";
                        Console.WriteLine();
                        await _llmService.InvokeStreamAsync(finalPrompt, ConsoleColor.Blue);
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                }

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ Reflection 化学合规自我纠错完成！");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }

        private string[] ParseToolCalls(string modelOutput)
        {
            // 尝试多种前缀格式（TOOLS: 优先，LLM 更容易遵守这个格式）
            string[] prefixes = { "TOOLS:", "tools:", "【工具调用】:", "[工具调用]:", "工具调用:" };
            foreach (var prefix in prefixes)
            {
                int startIndex = modelOutput.LastIndexOf(prefix);
                if (startIndex >= 0)
                {
                    string toolPart = modelOutput.Substring(startIndex + prefix.Length).Trim();
                    // 只取第一行（避免后续无关内容干扰）
                    string toolLine = toolPart.Split('\n')[0].Trim();
                    return toolLine.Split(',')
                        .Select(t => t.Trim()
                                      .Replace("(", "")
                                      .Replace(")", "")
                                      .Replace("：", "")
                                      .Replace(":", "")
                                      .Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToArray();
                }
            }

            // 兜底：扫描末尾 200 字符中已知工具名
            string[] knownTools = { "CheckHazardCategory", "CheckStorageCompatibility", "GetSafetyDistance", "GetCurrentTime", "Calculate" };
            string tailText = modelOutput.Length > 200 ? modelOutput.Substring(modelOutput.Length - 200) : modelOutput;
            var found = knownTools.Where(t => tailText.Contains(t)).ToList();
            return found.ToArray();
        }

        private async Task<string> CallTool(ChemicalComplianceTools tools, string toolName, string userInput)
        {
            return toolName.Trim()
                .Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "") switch
            {
                "CheckHazardCategory" => await tools.CheckHazardCategory(userInput),
                "CheckStorageCompatibility" => await tools.CheckStorageCompatibility(userInput, userInput),
                "GetSafetyDistance" => await tools.GetSafetyDistance(userInput),
                "GetCurrentTime" => tools.GetCurrentTime(),
                "Calculate" => tools.Calculate("1+1"),
                _ => $"未知工具: {toolName}"
            };
        }

    }
}
