using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// Phase 2a: 统一工具调度中心 —— LLM 语义工具选择 + 异步 RAG 工具执行
    /// LLM 优先，关键词兜底；异步 RAG 方法优先，同步硬编码兜底
    /// </summary>
    public class ToolService : IToolService
    {
        private readonly ChemicalComplianceTools _tools;
        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kbService;
        private readonly List<ToolDefinition> _toolDefinitions;

        public ToolService(ILlmService llmService, IKnowledgeBaseService kbService, List<ToolDefinition>? toolDefinitions)
        {
            _llmService = llmService;
            _kbService = kbService;
            _toolDefinitions = toolDefinitions ?? new List<ToolDefinition>();
            _tools = new ChemicalComplianceTools(kbService, llmService); // RAG-backed 构造
        }

        public async Task<ToolPlan> AnalyzeAndPlanToolsAsync(string userInput, string history)
        {
            var plan = new ToolPlan();

            // ═══ Step 1: LLM 语义工具选择（主路径） ═══
            try
            {
                var toolDesc = string.Join("\n", _toolDefinitions.Select((t, i) =>
                    $"{i + 1}. {t.Name} — {t.Description}"));

                var prompt = $@"你是工具调用规划器。用户问题是化工合规相关，请严格根据可用工具列表判断需要哪些工具。

可用工具：
{toolDesc}

用户问题：{userInput}

规则：只输出需要的工具名，英文逗号分隔。如果不需要任何工具，只输出一个「无」字。不要任何解释。
工具：";

                var llmResult = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Gray, "LLM工具规划");
                llmResult = llmResult.Trim().Replace("。", "").Replace(".", "");

                if (!string.IsNullOrEmpty(llmResult) &&
                    !llmResult.Equals("无", StringComparison.OrdinalIgnoreCase) &&
                    !llmResult.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    var candidates = llmResult.Split(',', '，')
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToList();

                    foreach (var candidate in candidates)
                    {
                        // 宽松匹配：精确匹配 或 候选包含已知工具名
                        var matched = _toolDefinitions.FirstOrDefault(td =>
                            td.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                            candidate.IndexOf(td.Name, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (matched != null && !plan.ToolNames.Contains(matched.Name))
                        {
                            plan.NeedsTools = true;
                            plan.ToolNames.Add(matched.Name);
                            Console.WriteLine($"\n🤖 LLM 选择工具: {matched.Name} ({matched.Description})");
                        }
                    }
                }
            }
            catch
            {
                Console.WriteLine("   ⚠️ LLM 工具规划失败，降级到关键词匹配");
            }

            // ═══ Step 2: 关键词兜底（LLM 未匹配到任何工具时） ═══
            if (plan.ToolNames.Count == 0)
            {
                var lowerInput = userInput.ToLower();
                foreach (var tool in _toolDefinitions)
                {
                    if (tool.KeywordTriggers.Any(kw => lowerInput.Contains(kw.ToLower())))
                    {
                        plan.NeedsTools = true;
                        plan.ToolNames.Add(tool.Name);
                        Console.WriteLine($"\n🔑 关键词触发工具: {tool.Name} ({tool.Description})");
                    }
                }
            }

            if (plan.ToolNames.Count == 0)
            {
                plan.NeedsTools = false;
                Console.WriteLine("\n🤔 分析中... 不需要调用工具");
            }

            return plan;
        }

        public async Task<Dictionary<string, string>> ExecuteToolsAsync(ToolPlan plan, string userInput)
        {
            var results = new Dictionary<string, string>();

            if (!plan.NeedsTools || plan.ToolNames.Count == 0)
            {
                Console.WriteLine("✅ 不需要调用工具");
                return results;
            }

            Console.WriteLine($"\n📋 计划调用 {plan.ToolNames.Count} 个工具...");
            foreach (var toolName in plan.ToolNames)
            {
                Console.Write($"\n🔧 调用 {toolName}... ");
                try
                {
                    var result = await CallToolAsync(toolName, userInput);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("成功!");
                    Console.WriteLine($"    结果: {result}");
                    Console.ResetColor();
                    results[toolName] = result;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"失败: {ex.Message}");
                    Console.ResetColor();
                    results[toolName] = $"调用失败: {ex.Message}";
                }
            }

            return results;
        }

        private async Task<string> CallToolAsync(string toolName, string userInput)
        {
            var cleaned = toolName.Trim()
                .Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "");

            // 优先调用异步 RAG 版本，GetCurrentTime/Calculate 仍为同步
            return cleaned switch
            {
                "CheckHazardCategory" => await _tools.CheckHazardCategoryAsync(RAG.ExtractSubstanceStatic(userInput)),
                "CheckStorageCompatibility" => await CallStorageCheckAsync(userInput),
                "GetSafetyDistance" => await _tools.GetSafetyDistanceAsync(RAG.ExtractFacilityTypeStatic(userInput)),
                "GetCurrentTime" => _tools.GetCurrentTime(),
                "Calculate" => _tools.Calculate(userInput),
                _ => $"未知工具: {toolName}"
            };
        }

        private async Task<string> CallStorageCheckAsync(string userInput)
        {
            var (a, b) = RAG.ExtractTwoSubstancesStatic(userInput);
            return await _tools.CheckStorageCompatibilityAsync(a, b);
        }

        public string GetToolDescriptions()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _toolDefinitions.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {_toolDefinitions[i].Name} - {_toolDefinitions[i].Description}");
            }
            return sb.ToString();
        }
    }
}