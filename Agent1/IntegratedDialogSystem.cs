using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agent1
{
    public class IntegratedDialogSystem
    {
        private readonly Kernel _kernel;
        private readonly IndustrialTools _industrialTools;
        private readonly SessionContext _session;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public IntegratedDialogSystem(SessionContext session)
        {
            _session = session;
            _industrialTools = new IndustrialTools();
            _cancellationTokenSource = new CancellationTokenSource();

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint);
            kernelBuilder.Plugins.AddFromType<IndustrialTools>();
            _kernel = kernelBuilder.Build();
        }

        public async Task<string> ProcessUserInput(string userInput)
        {
            try
            {
                SessionManager.AddDialogTurn(_session.SessionId, "User", userInput);

                string contextSummary = SessionManager.GetContextSummary(_session.SessionId);
                string customPrompt = _session.UserPromptTemplate;

                var toolsList = GetAvailableTools();

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤1】思考与工具调用需求");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string thoughtPrompt = $"""
                {customPrompt}
                
                你是工业设备诊断专家，拥有以下工具可用：
                {toolsList}
                
                当前对话上下文：
                {contextSummary}
                
                新问题：{userInput}
                
                请分析是否需要调用工具，如果需要，请列出工具名称。
                格式：【工具调用】: 工具1,工具2
                """;

                Console.WriteLine("\n思考中...");
                string thoughtResult = string.Empty;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                try
                {
                    await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(thoughtPrompt, cancellationToken: _cancellationTokenSource.Token))
                    {
                        thoughtResult += chunk;
                        Console.Write(chunk);
                        await Task.Delay(10);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  思考阶段异常: {ex.Message}");
                }
                Console.ResetColor();
                SessionManager.AddDialogTurn(_session.SessionId, "System", $"思考：{thoughtResult}");

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤2】调用工具");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string[] toolsToCall = ParseToolCalls(thoughtResult);
                Dictionary<string, string> toolResults = new Dictionary<string, string>();

                if (toolsToCall.Length == 0)
                {
                    Console.WriteLine("⚠️  未检测到明确工具调用需求");
                }
                else
                {
                    Console.WriteLine($"\n准备调用 {toolsToCall.Length} 个工具...");
                    foreach (string toolName in toolsToCall)
                    {
                        Console.WriteLine($"\n正在调用: {toolName}...");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        string result = await CallToolAsync(toolName);
                        toolResults.Add(toolName, result);
                        Console.WriteLine(result);
                        Console.ResetColor();
                        SessionManager.AddDialogTurn(_session.SessionId, "System", $"工具调用[{toolName}]: {result}");
                        await Task.Delay(200);
                    }
                }

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤3】生成内容大纲（ToC）");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string tocStructure = GenerateToC(userInput, toolResults);
                Console.WriteLine(tocStructure);

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤4】生成初步结论");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string initialConclusion = string.Empty;
                Console.ForegroundColor = ConsoleColor.Yellow;
                try
                {
                    initialConclusion = await GenerateInitialConclusionStream(userInput, toolResults, contextSummary, tocStructure);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  初步结论生成异常: {ex.Message}");
                    initialConclusion = "正在分析中...";
                }
                Console.ResetColor();
                SessionManager.AddDialogTurn(_session.SessionId, "Assistant", $"初步结论：{initialConclusion}");

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤5】自我反思与检查");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string reflectionResult = string.Empty;
                Console.ForegroundColor = ConsoleColor.Magenta;
                try
                {
                    reflectionResult = await ReflectAndCorrectStream(initialConclusion, toolResults);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  反思阶段异常: {ex.Message}");
                    reflectionResult = "无问题";
                }
                Console.ResetColor();
                SessionManager.AddDialogTurn(_session.SessionId, "System", $"反思：{reflectionResult}");

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("【步骤6】生成最终结论");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                string finalConclusion = string.Empty;
                Console.ForegroundColor = ConsoleColor.Blue;
                try
                {
                    finalConclusion = await GenerateFinalConclusionStream(userInput, toolResults, initialConclusion, reflectionResult, tocStructure);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  最终结论生成异常: {ex.Message}");
                    finalConclusion = "抱歉，生成最终结论时出错了";
                }
                Console.ResetColor();
                SessionManager.AddDialogTurn(_session.SessionId, "Assistant", finalConclusion);

                Console.WriteLine("\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ 处理完成");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                return finalConclusion;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 处理异常: {ex.Message}");
                Console.WriteLine($"堆栈: {ex.StackTrace}");
                return $"处理出错: {ex.Message}";
            }
        }

        private string GetAvailableTools()
        {
            var tools = new List<string>
            {
                "1. GetSpindleTemperature() - 获取机床主轴实时温度",
                "2. GetTemperatureThreshold() - 获取温度安全阈值",
                "3. WebSearch(query) - 联网搜索最新信息",
                "4. GetCurrentTime() - 获取当前时间",
                "5. Calculate(expression) - 数学计算"
            };
            return string.Join("\n", tools);
        }

        private string GenerateToC(string userInput, Dictionary<string, string> toolResults)
        {
            var toc = new StringBuilder();
            toc.AppendLine("【内容大纲】");
            toc.AppendLine("一、问题分析");
            toc.AppendLine($"  1.1 用户问题：{userInput}");

            if (toolResults.Any())
            {
                toc.AppendLine("二、工具调用结果");
                int idx = 1;
                foreach (var kv in toolResults)
                {
                    toc.AppendLine($"  2.{idx} {kv.Key}");
                    idx++;
                }
            }

            toc.AppendLine("三、诊断结论");
            toc.AppendLine("  3.1 问题判断");
            toc.AppendLine("  3.2 故障原因");
            toc.AppendLine("  3.3 整改建议");

            return toc.ToString();
        }

        private async Task<string> GenerateInitialConclusionStream(string userInput, Dictionary<string, string> toolResults, string context, string toc)
        {
            string toolSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

            string prompt = $"""
            你是工业设备诊断专家，请基于以下信息输出诊断结论：
            
            {toc}
            
            【上下文】
            {context}
            
            【当前问题】{userInput}
            
            【工具调用结果】
            {toolSummary}
            
            【要求】
            1. 严格按照大纲结构输出
            2. 基于真实数据，禁止编造
            3. 结论清晰，建议具体可落地
            """;

            string result = string.Empty;
            await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(prompt, cancellationToken: _cancellationTokenSource.Token))
            {
                result += chunk;
                Console.Write(chunk);
                await Task.Delay(10);
            }
            return result;
        }

        private async Task<string> ReflectAndCorrectStream(string initialConclusion, Dictionary<string, string> toolResults)
        {
            string toolSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

            string reflectionPrompt = $"""
            你是工业设备诊断专家，现在需要对初步结论进行严格检查：
            
            【反思维度】
            1. 数据真实性：是否完全基于工具调用的真实数据？是否编造了数据？
            2. 结论严谨性：判断是否符合阈值规则（安全阈值≤180℃）？原因是否有数据支撑？
            3. 建议落地性：整改建议是否具体可落地？
            4. 结构完整性：是否符合ToC大纲结构？
            
            【初步结论】
            {initialConclusion}
            
            【工具调用真实数据】
            {toolSummary}
            
            【输出要求】
            1. 逐条指出问题（无问题则说明"无问题"）
            2. 给出具体纠错建议
            3. 最后用【纠错指令】: 具体修改方向 格式总结
            """;

            string result = string.Empty;
            await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(reflectionPrompt, cancellationToken: _cancellationTokenSource.Token))
            {
                result += chunk;
                Console.Write(chunk);
                await Task.Delay(10);
            }
            return result;
        }

        private async Task<string> GenerateFinalConclusionStream(string userInput, Dictionary<string, string> toolResults,
                                                          string initialConclusion, string reflectionResult, string toc)
        {
            string toolSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

            string prompt = $"""
            你是工业设备诊断专家，请根据反思结果修正初步结论：
            
            {toc}
            
            【问题】{userInput}
            
            【工具调用真实数据】
            {toolSummary}
            
            【初步结论】
            {initialConclusion}
            
            【反思纠错结果】
            {reflectionResult}
            
            【要求】
            1. 严格修正所有反思指出的问题
            2. 完全基于真实工具数据
            3. 结论严谨、建议具体可落地
            4. 按照ToC大纲格式输出
            """;

            string result = string.Empty;
            await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(prompt, cancellationToken: _cancellationTokenSource.Token))
            {
                result += chunk;
                Console.Write(chunk);
                await Task.Delay(10);
            }
            return result;
        }

        private async Task<string> CallToolAsync(string toolName)
        {
            return toolName switch
            {
                "GetSpindleTemperature" => _industrialTools.GetSpindleTemperature(),
                "GetTemperatureThreshold" => _industrialTools.GetTemperatureThreshold(),
                "GetCurrentTime" => _industrialTools.GetCurrentTime(),
                "WebSearch" => await _industrialTools.WebSearch("工业设备故障诊断"),
                _ => await HandleDynamicToolCall(toolName)
            };
        }

        private async Task<string> HandleDynamicToolCall(string toolCall)
        {
            if (toolCall.StartsWith("Calculate(") && toolCall.EndsWith(")"))
            {
                string expression = toolCall.Substring(10, toolCall.Length - 11);
                return _industrialTools.Calculate(expression);
            }
            if (toolCall.StartsWith("WebSearch(") && toolCall.EndsWith(")"))
            {
                string query = toolCall.Substring(10, toolCall.Length - 11);
                return await _industrialTools.WebSearch(query);
            }
            return $"未知工具调用格式: {toolCall}";
        }

        private string[] ParseToolCalls(string modelOutput)
        {
            int startIndex = modelOutput.IndexOf("【工具调用】:");
            if (startIndex == -1)
                startIndex = modelOutput.IndexOf("[工具调用]:");

            if (startIndex == -1)
                return new string[0];

            string toolPart = modelOutput.Substring(startIndex + 6).Trim()
                                        .Replace("(", "")
                                        .Replace(")", "")
                                        .Replace("：", "")
                                        .Replace(":", "")
                                        .Replace("\n", "")
                                        .Replace(" ", "");
            return toolPart.Split(',')
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t))
                          .ToArray();
        }
    }
}
