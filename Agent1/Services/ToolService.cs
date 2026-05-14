using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public class ToolService : IToolService
    {
        private readonly IndustrialTools _tools;
        private readonly ILlmService _llmService;

        public ToolService(ILlmService llmService)
        {
            _tools = new IndustrialTools();
            _llmService = llmService;
        }

        public async Task<ToolPlan> AnalyzeAndPlanToolsAsync(string userInput, string history)
        {
            var tools = GetToolDescriptions();
            var lowerInput = userInput.ToLower();

            // ⭐ 优先直接根据关键词判断，不依赖LLM
            var plan = new ToolPlan();
            
            if (lowerInput.Contains("温度") || lowerInput.Contains("温度") || lowerInput.Contains("阈值"))
            {
                plan.NeedsTools = true;
                plan.ToolNames.Add("GetSpindleTemperature");
                plan.ToolNames.Add("GetTemperatureThreshold");
                Console.WriteLine("\n🤔 分析中... 检测到温度相关问题，准备调用温度工具");
                return plan;
            }
            if (lowerInput.Contains("时间"))
            {
                plan.NeedsTools = true;
                plan.ToolNames.Add("GetCurrentTime");
                Console.WriteLine("\n🤔 分析中... 检测到时间相关问题，准备调用时间工具");
                return plan;
            }

            // ⭐ 如果没有明确关键词，默认不需要工具
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

        private async Task<string> CallToolAsync(string toolName)
        {
            toolName = toolName.Trim();
            var lowerTool = toolName.ToLower();

            if (lowerTool.Contains("spindle") && lowerTool.Contains("temperature"))
                return _tools.GetSpindleTemperature();
            if (lowerTool.Contains("threshold"))
                return _tools.GetTemperatureThreshold();
            if (lowerTool.Contains("time") && lowerTool.Contains("current"))
                return _tools.GetCurrentTime();
            if (lowerTool.Contains("calculate") || lowerTool.Contains("计算"))
            {
                var idx = toolName.IndexOf('(');
                if (idx > 0)
                {
                    var arg = toolName.Substring(idx + 1).TrimEnd(')');
                    return _tools.Calculate(arg);
                }
            }

            return toolName switch
            {
                "GetSpindleTemperature" => _tools.GetSpindleTemperature(),
                "GetTemperatureThreshold" => _tools.GetTemperatureThreshold(),
                "GetCurrentTime" => _tools.GetCurrentTime(),
                "GetSpindleTemperature()" => _tools.GetSpindleTemperature(),
                "GetTemperatureThreshold()" => _tools.GetTemperatureThreshold(),
                "GetCurrentTime()" => _tools.GetCurrentTime(),
                "CurentTime" => _tools.GetCurrentTime(),
                _ => $"未知工具: {toolName}"
            };
        }

        private string GetToolDescriptions()
        {
            return @"1. GetSpindleTemperature - 获取主轴实时温度
2. GetTemperatureThreshold - 获取温度安全阈值
3. GetCurrentTime - 获取当前时间
4. Calculate(expression) - 数学计算
5. WebSearch(query) - 网络搜索";
        }

        private ToolPlan ParseToolPlan(string analysis)
        {
            var plan = new ToolPlan();

            try
            {
                Console.WriteLine("\n🔍 解析调试:");
                Console.WriteLine("分析内容长度: " + analysis.Length);

                var lines = analysis.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    if (!string.IsNullOrWhiteSpace(trimmed))
                        Console.WriteLine($"  行: {trimmed}");

                    if (trimmed.StartsWith("需要工具:"))
                    {
                        var value = trimmed.Substring("需要工具:".Length).Trim();
                        plan.NeedsTools = value.Contains("是") || !string.IsNullOrWhiteSpace(value);

                        if (!value.Contains("是") && !value.Contains("否") && !string.IsNullOrWhiteSpace(value))
                        {
                            var tools = value.Split(',')
                                            .Select(t => t.Trim())
                                            .Where(t => !string.IsNullOrWhiteSpace(t) && !t.Contains("无"))
                                            .ToList();
                            if (tools.Count > 0)
                                plan.ToolNames.AddRange(tools);
                        }
                    }
                    else if (trimmed.StartsWith("工具列表:"))
                    {
                        var value = trimmed.Substring("工具列表:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var tools = value.Split(',')
                                            .Select(t => t.Trim())
                                            .Where(t => !string.IsNullOrWhiteSpace(t) && !t.Contains("无"))
                                            .ToList();
                            if (tools.Count > 0)
                                plan.ToolNames.AddRange(tools);
                        }
                    }
                }

                if (plan.ToolNames.Count > 0)
                    plan.NeedsTools = true;

                plan.ToolNames = plan.ToolNames.Distinct().ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 解析错误: {ex.Message}");
                plan.NeedsTools = false;
                plan.ToolNames = new List<string>();
            }

            Console.WriteLine($"  结果: NeedsTools={plan.NeedsTools}, Tools=[{string.Join(",", plan.ToolNames)}]");
            return plan;
        }
    }
}