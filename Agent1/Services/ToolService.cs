using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    public class ToolService : IToolService
    {
        private readonly ChemicalComplianceTools _tools;
        private readonly ILlmService _llmService;
        private readonly List<ToolDefinition> _toolDefinitions;

        public ToolService(ILlmService llmService, List<ToolDefinition> toolDefinitions)
        {
            _tools = new ChemicalComplianceTools();
            _llmService = llmService;
            _toolDefinitions = toolDefinitions ?? new List<ToolDefinition>();
        }

        public async Task<ToolPlan> AnalyzeAndPlanToolsAsync(string userInput, string history)
        {
            var lowerInput = userInput.ToLower();
            var plan = new ToolPlan();

            // 配置驱动的关键词匹配
            foreach (var tool in _toolDefinitions)
            {
                if (tool.KeywordTriggers.Any(kw => lowerInput.Contains(kw.ToLower())))
                {
                    plan.NeedsTools = true;
                    plan.ToolNames.Add(tool.Name);
                    Console.WriteLine($"\n🤔 分析中... 关键词触发工具: {tool.Name} ({tool.Description})");
                    // 不再首次匹配即return，继续收集所有匹配工具
                }
            }
            // 用户输入	路径 A (ToolService)	路径 B (RAG.cs LLM推理)
            // "苯属于什么类别"	✅ 匹配"类别"→触发	✅ LLM也能推理出需要查类别
            // "苯的储存要注意什么"	❌ 没有"同库""共存"等关键词	✅ LLM推理出需要查储存兼容性
            // "这个化学品安不安全"	❌ 没有任何关键词匹配	✅ LLM推理出需要查危险类别


            plan.NeedsTools = false; // ← 全部遍历完都没匹配 → 不调用工
            Console.WriteLine("\n🤔 分析中... 不需要调用工具");
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

        private Task<string> CallToolAsync(string toolName, string userInput)
        {
            var cleaned = toolName.Trim()
                .Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "");

            return Task.FromResult(cleaned switch
            {
                "CheckHazardCategory" => _tools.CheckHazardCategory(RAG.ExtractSubstanceStatic(userInput)),
                "CheckStorageCompatibility" => CallStorageCheck(userInput),
                "GetSafetyDistance" => _tools.GetSafetyDistance(RAG.ExtractFacilityTypeStatic(userInput)),
                "GetCurrentTime" => _tools.GetCurrentTime(),
                "Calculate" => _tools.Calculate(userInput),
                _ => $"未知工具: {toolName}"
            });
        }

        private string CallStorageCheck(string userInput)
        {
            var (a, b) = RAG.ExtractTwoSubstancesStatic(userInput);
            return _tools.CheckStorageCompatibility(a, b);
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