
using Agent1.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent1
{
    public class RunReflectionStreamTools
    {
        private readonly ILlmService _llmService;
        private readonly ISessionService _sessionService;
        private readonly IndustrialTools _industrialTools;
        private readonly SessionContext _session;

        public RunReflectionStreamTools(ILlmService llmService, ISessionService sessionService)
        {
            _llmService = llmService;
            _sessionService = sessionService;
            _industrialTools = new IndustrialTools();
            _session = _sessionService.CreateSession(SessionType.IndustrialDiagnostic);
        }

        public async Task RunReflectionStreamTool()
        {
            Console.WriteLine("\n====Reflection（工业级自我纠错·多轮对话）====");
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

                _sessionService.AddDialogTurn(_session.SessionId, "User", userInput);

                Console.WriteLine($"【用户提问】{userInput}");

                Console.WriteLine("\n【Step 1 - Thought】模型分析需要调用的工具");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);
                string thoughtPrompt = $@"【对话历史】
{history}
【当前问题】{userInput}

【可用工具】
1. GetSpindleTemperature() - 获取机床主轴实时温度
2. GetTemperatureThreshold() - 获取温度安全阈值

请输出你的思考过程，并明确说明需要调用哪些工具（只需列出工具名称，用逗号分隔）。
格式要求：先输出思考内容，最后用【工具调用】: 工具1,工具2 格式列出需要调用的工具。";

                string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "分析思考");
                Console.ResetColor();

                Console.WriteLine("\n【Step 2 - Action】解析工具调用指令");
                string[] toolsToCall = ParseToolCalls(thoughtResult);
                if (toolsToCall.Length == 0)
                {
                    Console.WriteLine("⚠️ 模型未指定需要调用的工具，将默认调用所有工具");
                    toolsToCall = new string[] { "GetSpindleTemperature", "GetTemperatureThreshold" };
                }

                Console.WriteLine("\n【Step 3 - Observation】调用真实工业工具获取数据");
                Console.ForegroundColor = ConsoleColor.Green;
                Dictionary<string, string> toolResults = new Dictionary<string, string>();
                foreach (string toolName in toolsToCall)
                {
                    string result = CallTool(_industrialTools, toolName);
                    toolResults.Add(toolName, result);
                    Console.WriteLine($"✓ {toolName} → {result}");
                }
                Console.ResetColor();

                Console.WriteLine("\n【Step 4 - Initial Conclusion】生成初步诊断结论（未反思）");
                Console.ForegroundColor = ConsoleColor.Yellow;
                string observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));
                string initialPrompt = $@"【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【工具调用结果】
{observationSummary}
【要求】分析温度是否异常，指出故障原因，给出整改建议。";

                string initialConclusion = await _llmService.InvokeStreamWithRetryAsync(initialPrompt, ConsoleColor.Yellow, "初步结论");
                Console.ResetColor();

                Console.WriteLine("\n【Step 5 - Reflection】自我反思（检查纠错）");
                Console.ForegroundColor = ConsoleColor.Magenta;
                string reflectionPrompt = $@"【角色】工业设备诊断专家
【对话历史】
{history}
【初步结论】
{initialConclusion}
【工具调用真实数据】
{observationSummary}
【任务】对初步结论进行严格检查，按以下维度反思：
1. 数据真实性：是否完全基于真实工具数据？
2. 结论严谨性：温度异常判断是否符合 ≤ 180℃ 规则？
3. 建议落地性：整改建议是否具体？
【输出格式】逐条指出问题，最后用【纠错指令】: 具体修改方向 总结。";

                string reflectionResult = await _llmService.InvokeStreamWithRetryAsync(reflectionPrompt, ConsoleColor.Magenta, "反思检查");
                Console.ResetColor();

                Console.WriteLine("\n【Step 6 - Final Conclusion】纠错后的最终诊断结论");
                Console.ForegroundColor = ConsoleColor.Blue;
                string finalPrompt = $@"【角色】工业设备诊断专家
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
3. 结论严谨，建议具体可落地
4. 输出格式清晰易读";

                var answer = await _llmService.InvokeStreamAsync(finalPrompt, ConsoleColor.Blue);

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", "已生成诊断结论");

                Console.ResetColor();
                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ Reflection工业级自我纠错流程执行完成！");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }

        private string[] ParseToolCalls(string modelOutput)
        {
            int startIndex = modelOutput.IndexOf("【工具调用】:");
            if (startIndex == -1)
                startIndex = modelOutput.IndexOf("[工具调用]:");

            if (startIndex == -1)
                return new string[0];

            string toolPart = modelOutput.Substring(startIndex + 6).Trim();
            return toolPart.Split(',')
                          .Select(t => t.Trim()
                                        .Replace("(", "")
                                        .Replace(")", "")
                                        .Replace("：", "")
                                        .Replace(":", "")
                                        .Trim())
                          .Where(t => !string.IsNullOrEmpty(t))
                          .ToArray();
        }

        private string CallTool(IndustrialTools tools, string toolName)
        {
            return toolName switch
            {
                "GetSpindleTemperature" => tools.GetSpindleTemperature(),
                "GetTemperatureThreshold" => tools.GetTemperatureThreshold(),
                _ => $"未知工具: {toolName}"
            };
        }

    }
}
