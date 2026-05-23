
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
        private readonly ChemicalComplianceTools _tools; // P2: IndustrialTools → ChemicalComplianceTools
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
            _tools = new ChemicalComplianceTools(); // P2: 化工合规工具集
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

                string cotPrompt = $@"示例：问题氧化剂与易燃液体能否同库储存？
推理过程：
1. 查禁忌表：氧化剂与易燃液体存在配伍禁忌（GB15603-1995 4.2.2）
2. 判断：不可同库储存
3. 风险：混合可能导致火灾/爆炸
答案：氧化剂与易燃液体不可同库贮存，依据GB15603-1995第4.2.2条

【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 严格按照步骤思考
2. 最后给出清晰的合规判断结论"; // P2: 工业设备诊断→化工合规审核

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
                string cotPrompt = $@"示例：问题氧化剂与易燃液体能否同库储存？
推理过程：
1. 查禁忌表：氧化剂与易燃液体存在配伍禁忌（GB15603-1995 4.2.2）
2. 判断：不可同库储存
3. 风险：混合可能导致火灾/爆炸
答案：氧化剂与易燃液体不可同库贮存，依据GB15603-1995第4.2.2条

【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 用标签包裹思考过程
2. 在标签后给出结论"; // P2: 工业设备诊断→化工合规审核

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

                string reactPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【要求】
1. 遵循ReAct流程：Thought → Action → Observation → Final Answer
2. 可用工具：CheckHazardCategory, CheckStorageCompatibility, GetSafetyDistance, GetCurrentTime, Calculate
3. 思考过程用标签包裹"; // P2: 工业设备诊断→化工合规审核

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
            Console.WriteLine("\n====ReAct（化工合规手动工具调用·多轮对话）===="); // P2: 工业级→化工合规
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

                string thoughtPrompt = $@"你是化工园区危化品合规审核专家，现在需要分析以下问题：{userInput}

可用工具：
1. CheckHazardCategory(危化品名称) - 查询危险类别及适用国标（GB 30000 系列）
2. CheckStorageCompatibility(危化品A, 危化品B) - 检查两种危化品可否同库储存（GB15603）
3. GetSafetyDistance(设施类型) - 查询安全间距要求（GB50160/GB50016）
4. GetCurrentTime() - 获取当前时间
5. Calculate(表达式) - 数学计算

请输出你的思考过程，最后必须单独一行以大写 TOOLS: 开头列出工具名，逗号分隔。
格式示例：TOOLS:CheckHazardCategory,CheckStorageCompatibility
（务必以 TOOLS: 开头，否则工具不会被调用！）"; // P2: 工业工具→化工合规工具

                string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "思考分析");
                Console.ResetColor();

                Console.WriteLine("\n【Step 2 - Action】解析工具调用指令");
                string[] toolsToCall = ParseToolCalls(thoughtResult);

                if (toolsToCall.Length == 0)
                {
                    toolsToCall = new string[] { "GetSafetyDistance" }; // P2: 兜底工具从主轴温度改为安全距离
                }

                Console.WriteLine("\n【Step 3 - Observation】调用化工合规工具获取数据"); // P2: 工业工具→化工合规工具
                Console.ForegroundColor = ConsoleColor.Green;

                Dictionary<string, string> toolResults = new Dictionary<string, string>();

                foreach (string toolName in toolsToCall)
                {
                    string result = CallTool(_tools, toolName, userInput);
                    toolResults.Add(toolName, result);
                    Console.WriteLine($"✓ {toolName} → {result}");
                }
                Console.ResetColor();

                Console.WriteLine("\n【Step 4 - Conclusion】基于真实数据推理结论");
                Console.ForegroundColor = ConsoleColor.Blue;

                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);
                string observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

                string conclusionPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【工具调用结果】
{observationSummary}
【要求】
1. 严格基于真实数据，禁止编造任何信息
2. 判断是否合规，引用具体法规条款（GB 30000、GB15603、GB50160）
3. 指出违规点和对应的整改措施
4. 输出格式清晰易读"; // P2: 工业设备诊断→化工合规审核

                var answer = await _llmService.InvokeStreamWithRetryAsync(conclusionPrompt, ConsoleColor.Blue, "最终结论");
                Console.ResetColor();

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", answer);

                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ ReAct化工合规工具调用流程执行完成！"); // P2: 工业级→化工合规
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
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t) && !t.Contains("无") && !t.Contains("空") && !t.Equals("-"))
                        .ToArray();
                }
            }

            // 兜底：扫描末尾 200 字符中已知工具名
            string[] knownTools = { "CheckHazardCategory", "CheckStorageCompatibility", "GetSafetyDistance", "GetCurrentTime", "Calculate" };
            string tailText = modelOutput.Length > 200 ? modelOutput.Substring(modelOutput.Length - 200) : modelOutput;
            var found = knownTools.Where(t => tailText.Contains(t)).ToList();
            return found.ToArray();
        }

        private string CallTool(ChemicalComplianceTools tools, string toolName, string userInput) // P2: IndustrialTools→ChemicalComplianceTools
        {
            return toolName switch
            {
                "CheckHazardCategory" => tools.CheckHazardCategory(userInput), // P2: 传入用户问题进行模糊匹配
                "CheckStorageCompatibility" => tools.CheckStorageCompatibility(userInput, userInput),
                "GetSafetyDistance" => tools.GetSafetyDistance(userInput),
                "GetCurrentTime" => tools.GetCurrentTime(),
                "Calculate" => tools.Calculate("1+1"),
                _ => $"未知工具: {toolName}"
            };
        }
    }
}
