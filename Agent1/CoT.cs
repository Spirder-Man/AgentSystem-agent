
using Agent1.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Agent1
{
    public class CoT
    {
        /// <summary>
        /// LLM服务
        /// </summary>
        private readonly ILlmService _llmService;
        /// <summary>
        /// 会话服务
        /// </summary>
        private readonly ISessionService _sessionService;
        /// <summary>
        /// 工具服务
        /// </summary>
        private readonly IndustrialTools _tools;
        /// <summary>
        /// 会话上下文
        /// </summary>
        private readonly SessionContext _session;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="llmService">LLM服务</param>
        /// <param name="sessionService">会话服务</param>
        public CoT(ILlmService llmService, ISessionService sessionService)
        {
            _llmService = llmService;
            _sessionService = sessionService;
            _tools = new IndustrialTools();
            _session = _sessionService.CreateSession(SessionType.IndustrialDiagnostic);
        //这里的会话服务和LLM服务的区别是，会话服务负责管理会话的上下文，而LLM服务负责生成推理结果
        }

        /// <summary>
        /// 运行CoT推理器（多轮对话）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunCoT()
        {
            Console.WriteLine("\n====CoT（多轮对话）====");
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

                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);

                string cotPrompt = $@"示例：问题储罐温度超过180℃会有什么风险？
推理过程：
1. 查阈值：主轴安全温度≤180℃
2. 判断：超过阈值属于过热
3. 风险：可能导致轴承磨损、设备损坏
答案：温度超过180℃会增加设备损坏风险，建议立即检查

【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 严格按照步骤思考
2. 最后给出清晰的诊断结论";

                Console.WriteLine("\n📊 当前对话历史: " + _sessionService.GetHistoryCount(_session.SessionId) + " 轮");
                Console.WriteLine("💬 正在生成回复...");

                var answer = await _llmService.InvokeStreamWithRetryAsync(cotPrompt, ConsoleColor.Green, "CoT推理");

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", answer);

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ 处理完成");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }
        /// <summary>
        /// 运行CoT推理器（流式输出）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunCoTL()
        {
            Console.WriteLine("\n====CoT（流式输出·多轮对话）====");
            Console.WriteLine($"✅ 会话已创建，Session ID: {_session.SessionId}");
            Console.WriteLine("💡 输入 'exit' 或 'quit' 退出对话");
            Console.WriteLine("-----------------------------------");
            //这里写的应该是判断退出当前对话的逻辑，识别到用户输入的"exit"或"quit"或"退出"，就退出对话
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
                //将用户输入添加到会话历史中，用会话ID作为键值
                _sessionService.AddDialogTurn(_session.SessionId, "User", userInput);
                //获取会话历史，最多10轮
                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);
                //构建CoT推理的提示词
                string cotPrompt = $@"示例：问题储罐温度超过180℃会有什么风险？
推理过程：
1. 查阈值：主轴安全温度≤180℃
2. 判断：超过阈值属于过热
3. 风险：可能导致轴承磨损、设备损坏
答案：温度超过180℃会增加设备损坏风险，建议立即检查

【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 用标签包裹思考过程
2. 在标签后给出结论";

                //CoT推理过程中，流式输出推理结果，用绿色字体显示
                //CoT推理过程中，流式输出推理结果，用绿色字体显示
                Console.WriteLine("\n📊 当前对话历史: " + _sessionService.GetHistoryCount(_session.SessionId) + " 轮");
                Console.WriteLine("💬 正在生成回复...");
                //调用LLM服务的流式输出方法，生成推理结果
                var answer = await _llmService.InvokeStreamAsync(cotPrompt, ConsoleColor.Green);
       
                //将CoT推理的结果添加到会话历史中，用会话ID作为键值
                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", answer);

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ 处理完成");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }
        /// <summary>
        /// 运行ReAct推理器（流式输出）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunReActStream()
        {
            Console.WriteLine("\n====ReAct（流式·多轮对话）====");
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

                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);

                string reactPrompt = $@"【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 遵循ReAct流程：Thought → Action → Observation → Final Answer
2. 可用工具：GetSpindleTemperature(), GetTemperatureThreshold()
3. 思考过程用标签包裹";

                Console.WriteLine("\n📊 当前对话历史: " + _sessionService.GetHistoryCount(_session.SessionId) + " 轮");
                Console.WriteLine("💬 正在生成回复...");

                await _llmService.InvokeStreamAsync(reactPrompt, ConsoleColor.Cyan);
                var answer = "处理完成";

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", answer);

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ 处理完成");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }

        public async Task RunReActStreamTools()
        {
            Console.WriteLine("\n====ReAct（工业级手动工具调用·多轮对话）====");
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

                string thoughtPrompt = $@"你是工业设备诊断专家，现在需要分析以下问题：{userInput}

可用工具：
1. GetSpindleTemperature() - 获取机床主轴实时温度
2. GetTemperatureThreshold() - 获取温度安全阈值

请输出你的思考过程，并明确说明需要调用哪些工具（只需列出工具名称，用逗号分隔）
格式要求：先输出思考内容，最后用【工具调用】: 工具1,工具2 格式列出需要调用的工具。";

                string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "思考分析");
                Console.ResetColor();

                Console.WriteLine("\n【Step 2 - Action】解析工具调用指令");
                string[] toolsToCall = ParseToolCalls(thoughtResult);

                if (toolsToCall.Length == 0)
                {
                    toolsToCall = new string[] { "GetSpindleTemperature", "GetTemperatureThreshold" };
                }

                Console.WriteLine("\n【Step 3 - Observation】调用真实工业工具获取数据");
                Console.ForegroundColor = ConsoleColor.Green;

                Dictionary<string, string> toolResults = new Dictionary<string, string>();

                foreach (string toolName in toolsToCall)
                {
                    string result = CallTool(_tools, toolName);
                    toolResults.Add(toolName, result);
                    Console.WriteLine($"✓ {toolName} → {result}");
                }
                Console.ResetColor();

                Console.WriteLine("\n【Step 4 - Conclusion】基于真实数据推理结论");
                Console.ForegroundColor = ConsoleColor.Blue;

                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);
                string observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

                string conclusionPrompt = $@"【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【工具调用结果】
{observationSummary}
【要求】
1. 严格基于真实数据，禁止编造任何信息
2. 分析温度是否异常（安全阈值：≤ 180℃）
3. 指出可能的故障原因
4. 给出具体的整改建议
5. 输出格式清晰易读";

                var answer = await _llmService.InvokeStreamWithRetryAsync(conclusionPrompt, ConsoleColor.Blue, "最终结论");
                Console.ResetColor();

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", answer);

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ ReAct工业级工具调用流程执行完成！");
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
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t) && !t.Contains("无") && !t.Contains("空") && !t.Equals("-"))
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
