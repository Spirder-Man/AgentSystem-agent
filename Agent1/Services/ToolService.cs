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
                    return plan;
                }
            }

            plan.NeedsTools = false;
            Console.WriteLine("\n🤔 分析中... 不需要调用工具");
            return plan;
        }

        public async Task<Dictionary<string, string>> ExecuteToolsAsync(ToolPlan plan)
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
                    var result = await CallToolAsync(toolName);
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

        private Task<string> CallToolAsync(string toolName)
        {
            toolName = toolName.Trim()
                .Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "");

            return Task.FromResult(toolName switch
            {
                "CheckHazardCategory" => _tools.CheckHazardCategory(""),
                "CheckStorageCompatibility" => _tools.CheckStorageCompatibility("", ""),
                "GetSafetyDistance" => _tools.GetSafetyDistance(""),
                "GetCurrentTime" => _tools.GetCurrentTime(),
                "Calculate" => _tools.Calculate("1+1"),
                _ => $"未知工具: {toolName}"
            });
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