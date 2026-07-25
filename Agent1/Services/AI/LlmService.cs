

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services
{
    public class LlmService : ILlmService, IDisposable
    {
        private readonly Kernel _kernel;
        private readonly HttpClient _httpClient;
        private HttpClient? _injectedHttpClient; // 追踪通过反射注入到 SK 内部的 HttpClient
        private readonly ChemicalComplianceTools _complianceTools;
        // [P3 生产加固] 从配置读取重试/熔断参数，运维可通过 appsettings.json 调整
        private int MaxRetries => AppConfig.Instance.Llm.MaxRetries;
        private int RetryDelayMs => AppConfig.Instance.Llm.RetryDelayMs;

        // Phase 2a 验证: 工具调用诊断 — 记录最近一次 SK Auto Function Calling 的执行信息
        public List<FunctionCallRecord> LastFunctionCalls { get; private set; } = new();

        // P2-2: 显式接口实现，消除 as LlmService 向下转型
        IReadOnlyList<FunctionCallRecord> ILlmService.LastFunctionCalls => LastFunctionCalls;

        // ── Task 7: LLM 熔断器 — 连续失败达阈值后拒绝服务冷却期，防止雪崩 ──
        private int _consecutiveFailures = 0;
        private DateTime? _circuitOpenTime = null;
        private readonly object _circuitLock = new();
        private const int MaxConsecutiveFailures = 3;
        private static TimeSpan CircuitBreakDuration => TimeSpan.FromSeconds(30);

        /// <summary>
        /// [Thinking控制] 控制 Qwen3 思考模式开关。
        /// false=非Thinking(快速响应,Function Calling/评测/对话用)
        /// true=Thinking(深度推理,ReAct/Reflection/CoT用)
        /// 默认 false,由 InvokeStreamWithRetryAsync 临时切换为 true。
        /// </summary>
        internal bool EnableThinking { get; set; } = false;

        // [P0 Lazy<T>] Lazy 延迟解析 IKnowledgeBaseService，打破与 HybridKnowledgeBaseService 的循环依赖
        // .Value 仅在 ChemicalComplianceTools 首次执行 RAG 检索时触发，而非构造函数期
        private readonly Lazy<IKnowledgeBaseService> _lazyKb;

        public LlmService(Lazy<IKnowledgeBaseService> lazyKb)
        {
            _lazyKb = lazyKb;
            // ChemicalComplianceTools 同样接受 Lazy，将解析推迟到首次工具调用
            _complianceTools = new ChemicalComplianceTools(lazyKb, ChemicalDatabaseService.Instance);

            // [Thinking控制] 创建 OllamaThinkingHandler，后续通过反射注入到 SK 内部 HttpClient
            var thinkingHandler = new OllamaThinkingHandler(this);

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint, "not-needed");
            // Phase 2a: 使用 RAG-backed 实例注册，SK Auto Function Calling 直接调用知识库检索
            kernelBuilder.Plugins.AddFromObject(_complianceTools, "ChemicalCompliance");
            _kernel = kernelBuilder.Build();

            // [Thinking控制] 通过反射将 OllamaThinkingHandler 注入到 SK 内部 HttpClient 中
            InjectThinkingHandler(_kernel, thinkingHandler);

            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = AppConfig.Instance.VectorSearch.MaxConcurrentEmbeddings,
                EnableMultipleHttp2Connections = true
            })
            {
                Timeout = TimeSpan.FromSeconds(AppConfig.Instance.VectorSearch.EmbeddingTimeoutSeconds)
            };

            // Phase 2a 验证: 注册 Function Calling 拦截过滤器，捕获每次工具调用
            _kernel.FunctionInvocationFilters.Add(new FunctionCallDiagnosticsFilter(this));
        }

        public async Task<string> InvokeStreamAsync(string prompt, ConsoleColor color, FunctionChoiceBehavior? fcBehavior = null)
        {
            var result = new StringBuilder();
            Console.WriteLine();
            Console.ForegroundColor = color;

            bool isInThinkBlock = false;
            string buffer = "";
            int bufferFlushThreshold = 50;

            // P0 FIX: 通用 LLM 死循环检测 (替换旧版仅匹配"是否合规"的窄检测)
            // Bug1(T5 Reflection): 修正文本重复数百次 + 括号级联 ")"))))..."
            // Bug2(T6 RAG校验): 同一句话重复200+次  "不通过，回答中引用了..."
            int duplicateLineCount = 0;          // 相同行连续出现次数
            string? lastFlushedLine = null;      // 上一次 flush 的行内容
            const int MaxDuplicateLines = 8;     // 连续相同行超过8次 → 死循环
            int cascadeCount = 0;                // 字符级联计数 (如 ")"")....")
            char? cascadeChar = null;            // 当前级联字符
            const int MaxCascade = 12;           // 级联字符超过12个 → 死循环
            int totalOutputChars = 0;            // 总输出字符数
            const int MaxTotalChars = 5000;      // 硬截断上限

            // Phase 2a: 模型感知的 think 标签过滤 — 仅 DeepSeek-R1 需要
            bool filterThinkTags = ShouldFilterThinkTags();

            // [Bug-015/Bug-016 根因位点] 2分钟超时 + FC Required 组合在 KV Cache 不足时
            // 触发死亡螺旋：模型无法生成有效 FC JSON → 超时 → 重试 → 上下文更满 → 更大超时
            // 前提条件：llama-server -c >= 32768 可根治（见 zh-diag.sh 修复）
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // 对齐 HttpClient.Timeout

            try
            {
                // Phase 2a: 启用 SK Auto Function Calling — LLM 自主决定调用工具
                // [Bug-015 根因位点] FunctionChoiceBehavior.Required() 强制每次必有工具调用。
                // 当 llama-server -c 8192 KV Cache 不足时，模型无法在受限上下文中生成正确 FC JSON，
                // 导致 SK 一直等待工具调用 → 2min 超时 → 重试循环 → 上下文进一步被工具结果填充 → 死循环
                // [T13 无状态架构] cache_prompt=false 禁用服务端 KV Cache 复用，配合 -sps 0.0 确保每个请求独立
                // [Bug C 修复] FC 策略由调用方显式声明，不再硬编码 Required()。
                // 默认保持 Required() 向后兼容；HyDE/Reflection 等纯文本场景传 None()。
                var effectiveFc = fcBehavior ?? FunctionChoiceBehavior.Required();
                Console.WriteLine($"   [SK诊断] 模型={ModelConfig.ModelId}, FC={(effectiveFc?.GetType().Name ?? "None")}");

                var settings = new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = effectiveFc,
                    Temperature = 0.3,
                };
                settings.ExtensionData = new Dictionary<string, object>
                {
                    ["cache_prompt"] = false
                };

                // Phase 2a 验证: 清空上一轮工具调用记录
                LastFunctionCalls.Clear();

                // [Bug-016 根因位点] _kernel.InvokePromptStreamingAsync 每次发送完整 prompt（system + tools 定义 + user query）
                // 到 llama-server。当 tools 定义 + prompt > -c 设定值时，llama.cpp 截断最旧 tokens（含 system prompt 中的 FC 格式说明），
                // 导致模型无法正确生成 Function Calling JSON，陷入「不调用工具 → SK 重试 → 工具结果注入 Prompt → 上下文进一步膨胀」死亡螺旋。
                await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(
                    prompt, new KernelArguments(settings), cancellationToken: cts.Token))
                {
                    buffer += chunk;

                    // Phase 2a: 模型感知 — 仅 DeepSeek-R1 需要过滤 <think> 标签
                    if (filterThinkTags)
                    {
                        while (true)
                        {
                            int thinkEnd = buffer.IndexOf("</think>");
                            if (thinkEnd >= 0)
                            {
                                if (isInThinkBlock)
                                {
                                    buffer = buffer.Substring(thinkEnd + "</think>".Length);
                                    isInThinkBlock = false;
                                }
                                else
                                {
                                    string before = buffer.Substring(0, thinkEnd);
                                    string after = buffer.Substring(thinkEnd + "</think>".Length);
                                    buffer = before + after;
                                }
                                continue;
                            }

                            int thinkStart = buffer.IndexOf("<think>");
                            if (thinkStart >= 0)
                            {
                                string beforeThink = buffer.Substring(0, thinkStart);
                                if (!string.IsNullOrWhiteSpace(beforeThink))
                                {
                                    string cleaned = CleanLine(beforeThink);
                                    if (!string.IsNullOrWhiteSpace(cleaned))
                                    {
                                        result.Append(cleaned);
                                        Console.Write(cleaned);
                                    }
                                }
                                isInThinkBlock = true;
                                buffer = buffer.Substring(thinkStart + "<think>".Length);
                                continue;
                            }

                            // 没有更多标记需要处理
                            break;
                        }
                    }

                    // 定期清理并输出
                    if (!isInThinkBlock && buffer.Length > bufferFlushThreshold)
                    {
                        string cleaned = CleanChunk(buffer);
                        if (!string.IsNullOrWhiteSpace(cleaned))
                        {
                            // ── P0 FIX: 通用 LLM 死循环检测 ──
                            // 检测维度1: 连续相同行 (Bug2 RAG校验死循环: "不通过..." 重复200+次)
                            if (lastFlushedLine != null && cleaned == lastFlushedLine)
                            {
                                duplicateLineCount++;
                                if (duplicateLineCount > MaxDuplicateLines)
                                {
                                    Console.WriteLine($"\n   ⚠️ [截断] 检测到连续重复输出({duplicateLineCount}次相同行)，停止流式接收");
                                    break;
                                }
                            }
                            else
                            {
                                duplicateLineCount = 0;
                                lastFlushedLine = cleaned;
                            }

                            // 检测维度2: 字符级联 (Bug1 Reflection死循环: ")"))))...")
                            foreach (char ch in cleaned)
                            {
                                if (ch == cascadeChar)
                                {
                                    cascadeCount++;
                                    if (cascadeCount > MaxCascade)
                                    {
                                        Console.WriteLine($"\n   ⚠️ [截断] 检测到字符重复级联('{cascadeChar}'×{cascadeCount})，停止流式接收");
                                        break;
                                    }
                                }
                                else if (ch == ')' || ch == '）' || ch == '】' || ch == '}' || ch == '#' || ch == '*')
                                {
                                    cascadeChar = ch;
                                    cascadeCount = 1;
                                }
                                else
                                {
                                    cascadeChar = null;
                                    cascadeCount = 0;
                                }
                            }
                            if (cascadeCount > MaxCascade) break;  // 内层break传递

                            // 检测维度3: 总长度硬截断 (Bug1 Reflection死循环: 累计输出>5000字符)
                            totalOutputChars += cleaned.Length;
                            if (totalOutputChars > MaxTotalChars)
                            {
                                Console.WriteLine($"\n   ⚠️ [截断] 输出超过{MaxTotalChars}字符上限，停止流式接收");
                                break;
                            }

                            result.Append(cleaned);
                            Console.Write(cleaned);
                        }
                        buffer = "";
                    }

                    await Task.Delay(10);
                }

                // 输出剩余内容
                if ((!filterThinkTags || !isInThinkBlock) && buffer.Length > 0)
                {
                    string cleaned = CleanChunk(buffer);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        result.Append(cleaned);
                        Console.Write(cleaned);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n⚠️ 生成错误: {ex.Message}");
            }

            // Phase 2a 验证: 输出本轮工具调用诊断摘要
            if (LastFunctionCalls.Count > 0)
            {
                Console.WriteLine($"   [SK诊断] 本轮共调用 {LastFunctionCalls.Count} 个工具:");
                foreach (var fc in LastFunctionCalls)
                {
                    var resultPreview = (fc.Result ?? "").Length > 150
                        ? (fc.Result ?? "").Substring(0, 150) + "..."
                        : fc.Result;
                    Console.WriteLine($"     🔧 {fc.FunctionName}({fc.Arguments}) → {(fc.Success ? "✓" : "✗")} {resultPreview}");
                }
            }
            else
            {
                Console.WriteLine("   [SK诊断] ⚠️ 本轮未调用任何工具 — LLM 可能绕过 Function Calling 直接回答");
            }

            Console.ResetColor();
            Console.WriteLine();

            return CleanFinalOutput(result.ToString());
        }

        // ⭐ 单行清理
        private string CleanLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "";

            // 移除编号（如 "5. "）
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*\d+\.\s*", "");

            // 移除标记（如 "【内容】"）
            line = System.Text.RegularExpressions.Regex.Replace(line, @"【.*?】\s*", "");

            // 移除多余的空格
            line = line.Trim();

            return line;
        }

        // ⭐ 块清理
        private string CleanChunk(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                return "";

            // 逐行清理
            var lines = chunk.Split('\n');
            var cleanedLines = new List<string>();
            
            foreach (var line in lines)
            {
                string cleaned = CleanLine(line);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    cleanedLines.Add(cleaned);
                }
            }

            return string.Join("\n", cleanedLines);
        }

        // ⭐ 最终输出清理
        private string CleanFinalOutput(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "";

            // Phase 2a: 模型感知 — 仅 DeepSeek-R1 需要全局移除 <think> 标签
            if (ShouldFilterThinkTags())
            {
                content = content.Replace("<think>", "").Replace("</think>", "");
            }

            // 逐行清理
            var lines = content.Split('\n');
            var cleanedLines = new List<string>();
            
            foreach (var line in lines)
            {
                string cleaned = CleanLine(line);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    cleanedLines.Add(cleaned);
                }
            }

            // 合并结果
            return string.Join("\n", cleanedLines).Trim();
        }

        /// <summary>
        /// [Bug C 修复] 旧重载委托到新重载，默认 FC=null → Required()，保持向后兼容。
        /// </summary>
        public async Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "")
            => await InvokeStreamWithRetryAsync(prompt, color, stageName, fcBehavior: null);

        /// <summary>
        /// [Bug C 修复] 新增 FC 策略参数，调用方显式声明语义需求。
        /// fcBehavior=null → 默认 Required()（主流程兼容）；None() → 纯文本生成（HyDE/反思）；Auto() → 自主决定。
        /// </summary>
        public async Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName, FunctionChoiceBehavior? fcBehavior)
        {
            // Task 7: 熔断器检查 — 电路打开时快速失败
            CheckCircuitBreaker();

            // [Bug-016 关联位点] 重试机制在 KV Cache 不足时加剧问题：每次重试用同一个 prompt，
            // 但 SK 内部可能将前次失败的 Function Calling 结果注入对话上下文，使 prompt 越来越长，
            // 导致后续重试更慢、更易超时。根治需 zh-diag.sh -c 32768。
            var previousThinking = EnableThinking;
            EnableThinking = true;
            var sw = Stopwatch.StartNew();
            int retries = 0;
            MetricsCollector.IncrementLlmActiveRequests();
            try
            {
                Exception lastException = null;

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            retries = attempt - 1;
                            Console.WriteLine($"\n🔄 第{attempt}次重试 {stageName}...");
                        }

                        var result = await InvokeStreamAsync(prompt, color, fcBehavior);
                        RecordCircuitSuccess();
                        MetricsCollector.RecordLlmCall(sw.ElapsedMilliseconds, true, retries);
                        return result;
                    }
                    catch (OperationCanceledException ex)
                    {
                        lastException = ex;
                        Console.WriteLine($"\n⏰ 请求超时 ({attempt}/{MaxRetries}): {ex.Message}");

                        if (attempt < MaxRetries)
                            await Task.Delay(RetryDelayMs * attempt);  // 指数退避: 2s, 4s
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Console.WriteLine($"\n❌ 错误 ({attempt}/{MaxRetries}): {ex.Message}");

                        if (attempt < MaxRetries)
                            await Task.Delay(RetryDelayMs * attempt);  // 指数退避: 2s, 4s
                    }
                }

                RecordCircuitFailure();
                MetricsCollector.RecordLlmCall(sw.ElapsedMilliseconds, false, retries);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ 所有重试失败: {lastException?.Message}");
                Console.ResetColor();

                return $"生成失败: {lastException?.Message}";
            }
            finally
            {
                EnableThinking = previousThinking;
                MetricsCollector.DecrementLlmActiveRequests();
            }
        }

        /// <summary>
        /// [Bug C 修复] 旧重载委托到新重载，默认 FC=null → Required()，保持向后兼容。
        /// </summary>
        public async Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName = "")
            => await InvokeNonStreamingWithRetryAsync(prompt, stageName, fcBehavior: null);

        /// <summary>
        /// [Bug C 修复] 新增 FC 策略参数。
        /// Phase 2a 评测加速: 非流式调用 — 跳过流式缓冲/控制台打印/think过滤，
        /// 直接拿完整结果。用于批量评测场景，相比流式节省 30-40% CPU 时间。
        /// 
        /// 重试策略:
        ///   - 第1次: 使用调用方指定的 FC 策略（默认 Required），LLM 自主决定工具调用
        ///   - 第2-3次: 若首次已成功调用工具，重试时禁用 FC 并将工具结果内联到 prompt，
        ///              避免重复触发 RAG 检索浪费 CPU。仅重试 LLM 文本生成部分。
        /// 
        /// 超时: 5 分钟 (CPU 推理下 qwen3:8b-eval + num_ctx 8192 需要更多时间)
        /// </summary>
        public async Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName, FunctionChoiceBehavior? fcBehavior)
        {
            // Task 7: 熔断器检查 — 电路打开时快速失败
            CheckCircuitBreaker();

            Exception? lastException = null;
            // 记录首次尝试中成功调用的工具结果，供重试时内联
            List<FunctionCallRecord>? firstAttemptToolResults = null;
            var sw = Stopwatch.StartNew();
            int retries = 0;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                // 评测快速通道：缩减超时 + 限制输出 token，CPU 推理大幅提速
                var timeoutMinutes = attempt == 1 ? 6 : 5;
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));

                try
                {
                    if (attempt > 1)
                    {
                        retries = attempt - 1;
                        Console.WriteLine($"   🔄 第{attempt}次重试 {stageName}...");
                    }

                    if (attempt == 1)
                    {
                        // ── 第 1 次: 完整 Function Calling 模式 ──
                        // [Bug C 修复] FC 策略由调用方显式传入，默认 Required。
                        var effectiveFc = fcBehavior ?? FunctionChoiceBehavior.Required();
                        var settings = new OpenAIPromptExecutionSettings
                        {
                            FunctionChoiceBehavior = effectiveFc,
                            Temperature = 0.0,
                            MaxTokens = 512,
                        };
                        settings.ExtensionData = new Dictionary<string, object>
                        {
                            ["cache_prompt"] = false
                        };

                        LastFunctionCalls.Clear();

                        var kernelResult = await _kernel.InvokePromptAsync(
                            prompt, new KernelArguments(settings), cancellationToken: cts.Token);

                        // 保存工具调用结果，供重试使用
                        firstAttemptToolResults = new List<FunctionCallRecord>(LastFunctionCalls);

                        var resultText = kernelResult.ToString();

                        // 空响应回退：用工具调用结果拼一个回答
                        if (string.IsNullOrWhiteSpace(resultText) && firstAttemptToolResults.Count > 0)
                        {
                            Console.WriteLine($"   ⚠️ 模型未生成文本，用 {firstAttemptToolResults.Count} 个工具结果回退");
                            resultText = string.Join("\n\n",
                                firstAttemptToolResults.Select(fc =>
                                    $"【{fc.FunctionName}】{fc.Result}"));
                        }

                        RecordCircuitSuccess();
                        MetricsCollector.RecordLlmCall(sw.ElapsedMilliseconds, true, retries);
                        return resultText;
                    }
                    else
                    {
                        // ── 第 2-3 次: 禁用 FC，工具结果内联重试 ──
                        string retryPrompt = prompt;
                        if (firstAttemptToolResults != null && firstAttemptToolResults.Count > 0)
                        {
                            // 将首次成功调用的工具结果内联到 prompt，避免重跑 RAG
                            var toolResultsText = string.Join("\n",
                                firstAttemptToolResults.Select(fc =>
                                    $"[工具调用] {fc.FunctionName}({fc.Arguments}): {fc.Result}"));
                            retryPrompt = prompt + "\n\n【已获取的工具调用结果，请直接据此回答】\n" + toolResultsText;
                            Console.WriteLine($"   📋 [验证] 工具结果已内联: {firstAttemptToolResults.Count} 个调用结果附加到重试 Prompt");
                        }

                        var retrySettings = new OpenAIPromptExecutionSettings
                        {
                            // 禁用工具调用：避免重复触发 RAG 检索
                            FunctionChoiceBehavior = null,
                            Temperature = 0.3,
                            MaxTokens = 512,
                        };
                        retrySettings.ExtensionData = new Dictionary<string, object>
                        {
                            ["cache_prompt"] = false
                        };

                        var kernelResult = await _kernel.InvokePromptAsync(
                            retryPrompt, new KernelArguments(retrySettings), cancellationToken: cts.Token);

                        // [P0-1 FIX] 恢复工具调用记录，表明最终回答基于内联的工具数据
                        if (firstAttemptToolResults != null && firstAttemptToolResults.Count > 0)
                            LastFunctionCalls = firstAttemptToolResults;

                        RecordCircuitSuccess();
                        MetricsCollector.RecordLlmCall(sw.ElapsedMilliseconds, true, retries);
                        return kernelResult.ToString();
                    }
                }
                catch (OperationCanceledException ex)
                {
                    // [P0-2 FIX] 即使超时，工具也可能已成功调用，捕获结果供重试内联
                    if (attempt == 1 && LastFunctionCalls.Count > 0 && firstAttemptToolResults == null)
                    {
                        firstAttemptToolResults = new List<FunctionCallRecord>(LastFunctionCalls);
                        Console.WriteLine($"   💾 已保存 {firstAttemptToolResults.Count} 个工具结果供重试用");
                    }

                    // [P0-1 FIX] 清空残留工具记录，防止评测器读到 stale 数据
                    LastFunctionCalls.Clear();

                    lastException = ex;
                    Console.WriteLine($"   ⏰ 请求超时 ({attempt}/{MaxRetries}): {ex.Message}");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * 2);
                }
                catch (Exception ex)
                {
                    // [P0-1 FIX] 清空残留工具记录
                    LastFunctionCalls.Clear();

                    lastException = ex;
                    Console.WriteLine($"   ❌ 错误 ({attempt}/{MaxRetries}): {ex.Message}");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * 2);
                }
            }

            RecordCircuitFailure();
            MetricsCollector.RecordLlmCall(sw.ElapsedMilliseconds, false, retries);
            Console.WriteLine($"   ❌ 所有重试失败: {lastException?.Message}");
            return $"生成失败: {lastException?.Message}";
        }

        /// <summary>
        /// [T13 无状态架构] 评测专用流式调用：支持按工具名称过滤 Function Calling，
        /// 避免无关工具定义污染上下文（info_query 类 case 减少 40% prompt 体积）。
        /// 内置死循环检测 + cache_prompt=false 双重保护，配合 -sps 0.0 实现完全无状态评测。
        /// </summary>
        public async Task<string> InvokeEvalWithToolsAsync(string prompt, IReadOnlyList<string> allowedToolNames)
        {
            var result = new StringBuilder();

            // 获取过滤后的 KernelFunction 列表
            var plugin = _kernel.Plugins["ChemicalCompliance"];
            var functions = allowedToolNames
                .Select(name => plugin.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f != null)
                .Cast<KernelFunction>()
                .ToList();

            Console.WriteLine($"   [SK诊断] 工具裁剪: {functions.Count}/{plugin.Count()} → {string.Join(", ", functions.Select(f => f.Name))}");

            // [T13 无状态架构] 创建临时 Kernel，仅注册当前 case 需要的工具
            // SK 1.74.0 的 FunctionChoiceBehaviorOptions 不支持 Functions 过滤，
            // 改用 Kernel 级别隔离：创建轻量 Kernel，只注册指定插件子集
            var evalKernelBuilder = Kernel.CreateBuilder();
            evalKernelBuilder.AddOpenAIChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint, "not-needed");
            var filteredPlugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromFunctions("ChemicalCompliance", functions);
            evalKernelBuilder.Plugins.Add(filteredPlugin);
            var evalKernel = evalKernelBuilder.Build();
            // 复用诊断过滤器
            evalKernel.FunctionInvocationFilters.Add(new FunctionCallDiagnosticsFilter(this));

            // 死循环检测变量
            int duplicateLineCount = 0;
            string? lastFlushedLine = null;
            const int MaxDuplicateLines = 8;
            int cascadeCount = 0;
            char? cascadeChar = null;
            const int MaxCascade = 12;
            int totalOutputChars = 0;
            const int MaxTotalChars = 5000;

            // [T13 无状态架构] cache_prompt=false 禁用服务端 KV Cache 复用
            // [Bug B 修复] Required() → Auto()：业务评测由 LLM 自主决定是否调用工具，
            // 避免 Required() 在 FC 返回结果后继续强制新一轮调用导致死循环。
            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = 0.3,
            };
            settings.ExtensionData = new Dictionary<string, object>
            {
                ["cache_prompt"] = false
            };

            LastFunctionCalls.Clear();

            string buffer = "";
            int bufferFlushThreshold = 50;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                await foreach (var chunk in evalKernel.InvokePromptStreamingAsync<string>(
                    prompt, new KernelArguments(settings), cancellationToken: cts.Token))
                {
                    buffer += chunk;

                    if (buffer.Length > bufferFlushThreshold)
                    {
                        string cleaned = CleanChunk(buffer);
                        if (!string.IsNullOrWhiteSpace(cleaned))
                        {
                            // 死循环检测维度1: 连续相同行
                            if (lastFlushedLine != null && cleaned == lastFlushedLine)
                            {
                                duplicateLineCount++;
                                if (duplicateLineCount > MaxDuplicateLines)
                                {
                                    Console.WriteLine($"\n   ⚠️ [截断] 检测到连续重复输出({duplicateLineCount}次)，停止流式接收");
                                    break;
                                }
                            }
                            else
                            {
                                duplicateLineCount = 0;
                                lastFlushedLine = cleaned;
                            }

                            // 死循环检测维度2: 字符级联
                            foreach (char ch in cleaned)
                            {
                                if (ch == cascadeChar)
                                {
                                    cascadeCount++;
                                    if (cascadeCount > MaxCascade)
                                    {
                                        Console.WriteLine($"\n   ⚠️ [截断] 检测到字符重复级联('{cascadeChar}'×{cascadeCount})，停止流式接收");
                                        break;
                                    }
                                }
                                else if (ch == ')' || ch == '）' || ch == '】' || ch == '}' || ch == '#' || ch == '*')
                                {
                                    cascadeChar = ch;
                                    cascadeCount = 1;
                                }
                                else
                                {
                                    cascadeChar = null;
                                    cascadeCount = 0;
                                }
                            }
                            if (cascadeCount > MaxCascade) break;

                            // 死循环检测维度3: 总长度硬截断
                            totalOutputChars += cleaned.Length;
                            if (totalOutputChars > MaxTotalChars)
                            {
                                Console.WriteLine($"\n   ⚠️ [截断] 输出超过{MaxTotalChars}字符上限，停止流式接收");
                                break;
                            }

                            result.Append(cleaned);
                            Console.Write(cleaned);
                        }
                        buffer = "";
                    }

                    await Task.Delay(10);
                }

                // 输出剩余内容
                if (buffer.Length > 0)
                {
                    string cleaned = CleanChunk(buffer);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        result.Append(cleaned);
                        Console.Write(cleaned);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n⚠️ 生成错误: {ex.Message}");
            }

            // 输出工具调用诊断
            if (LastFunctionCalls.Count > 0)
            {
                Console.WriteLine($"   [SK诊断] 本轮共调用 {LastFunctionCalls.Count} 个工具:");
                foreach (var fc in LastFunctionCalls)
                {
                    var resultPreview = (fc.Result ?? "").Length > 150
                        ? (fc.Result ?? "").Substring(0, 150) + "..."
                        : fc.Result;
                    Console.WriteLine($"     🔧 {fc.FunctionName}({fc.Arguments}) → {(fc.Success ? "✓" : "✗")} {resultPreview}");
                }
            }
            else
            {
                Console.WriteLine("   [SK诊断] ⚠️ 本轮未调用任何工具");
            }

            return CleanFinalOutput(result.ToString());
        }

        /// <summary>
        /// Layer 1 FC 就绪性检查 — 在业务评测前验证 Function Calling 管线是否正常。
        /// 使用与评测相同的非流式调用路径 (InvokeNonStreamingWithRetryAsync)，
        /// 确保 Layer 1 和 Layer 2 的 FC 行为完全一致。
        /// 返回 (是否基础通过, 触发数, 总数, 详细诊断文本)。
        /// 基础通过标准: 5 条至少 1 条触发了工具调用。
        /// </summary>
        // ════════════════════════════════════════
        // Task 7: LLM 熔断器方法
        // ════════════════════════════════════════

        /// <summary>
        /// 检查熔断器状态。若连续失败达 3 次且冷却期未过(30秒)，
        /// 抛出 CircuitBreakerOpenException 快速失败，由上层中间件处理。
        /// </summary>
        private void CheckCircuitBreaker()
        {
            lock (_circuitLock)
            {
                if (_consecutiveFailures >= MaxConsecutiveFailures && _circuitOpenTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - _circuitOpenTime.Value;
                    if (elapsed < CircuitBreakDuration)
                    {
                        var remainingSeconds = (int)(CircuitBreakDuration - elapsed).TotalSeconds;
                        throw new CircuitBreakerOpenException(
                            $"LLM 服务熔断中：连续 {_consecutiveFailures} 次失败，" +
                            $"请在 {Math.Max(1, remainingSeconds)} 秒后重试");
                    }
                    // 冷却期已过，半开状态：允许下一次尝试
                    _consecutiveFailures = 0;
                    _circuitOpenTime = null;
                    Console.WriteLine("   🔓 [熔断器] 冷却期已过，进入半开状态，允许试探请求");
                }
            }
        }

        private void RecordCircuitSuccess()
        {
            lock (_circuitLock)
            {
                if (_consecutiveFailures > 0 || _circuitOpenTime.HasValue)
                    Console.WriteLine($"   ✅ [熔断器] 调用成功，重置计数器 (之前失败 {_consecutiveFailures} 次)");
                _consecutiveFailures = 0;
                _circuitOpenTime = null;
            }
            MetricsCollector.SetCircuitBreakerOpen(false);
        }

        private void RecordCircuitFailure()
        {
            lock (_circuitLock)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _circuitOpenTime = DateTime.UtcNow;
                    Console.WriteLine($"   🔴 [熔断器] 连续 {_consecutiveFailures} 次失败，熔断器打开 ({CircuitBreakDuration.TotalSeconds}s 冷却)");
                }
                else
                {
                    Console.WriteLine($"   ⚠️ [熔断器] 失败计数: {_consecutiveFailures}/{MaxConsecutiveFailures}");
                }
            }
            // 更新 Prometheus gauge
            if (_consecutiveFailures >= MaxConsecutiveFailures)
                MetricsCollector.SetCircuitBreakerOpen(true);
        }

        public async Task<(bool passed, int triggerCount, int totalCount, string detail)> RunFcReadinessCheckAsync()
        {
            var testCases = new (string query, string expectedTools, string description)[]
            {
                ("苯属于什么危险类别", "CheckHazardCategory", "危险类别查询"),
                ("苯和丙酮能同库储存吗", "CheckStorageCompatibility", "储存兼容性检查"),
                ("甲类仓库与明火点的安全距离是多少", "GetSafetyDistance", "安全距离查询"),
                ("现在几点", "GetCurrentTime", "通用工具-时间"),
                ("甲醇和硝酸存放在同一个仓库是否合规", "CheckHazardCategory,CheckStorageCompatibility", "多工具调用"),
            };

            var promptTemplate = AppConfig.Instance.PromptTemplates.EvalFastPrompt;
            var detail = new System.Text.StringBuilder();
            int triggerCount = 0;

            Console.WriteLine("\n   ── FC 就绪性检查 ──");

            for (int i = 0; i < testCases.Length; i++)
            {
                var tc = testCases[i];
                Console.Write($"   [{i + 1}/{testCases.Length}] {tc.description}: \"{tc.query}\" ... ");

                // 使用与 eval 完全相同的 prompt 模板和调用路径
                var prompt = promptTemplate
                    .Replace("{SystemRole}", AppConfig.Instance.PromptTemplates.SystemRole)
                    .Replace("{UserInput}", tc.query);

                try
                {
                    await InvokeNonStreamingWithRetryAsync(prompt, "FC就绪检查");

                    if (LastFunctionCalls.Count > 0)
                    {
                        var tools = string.Join(", ", LastFunctionCalls.Select(fc => fc.FunctionName));
                        Console.WriteLine($"✅ 触发: {tools}");
                        detail.AppendLine($"  ✅ [{tc.description}] {tc.query} → {tools}");
                        triggerCount++;
                    }
                    else
                    {
                        Console.WriteLine("❌ 未触发");
                        detail.AppendLine($"  ❌ [{tc.description}] {tc.query} → 未触发任何工具 (预期: {tc.expectedTools})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 异常: {ex.Message}");
                    detail.AppendLine($"  ⚠️ [{tc.description}] {tc.query} → 异常: {ex.Message}");
                }

                await Task.Delay(500);
            }

            var passed = triggerCount >= 1; // 基础通过: 至少 1 条触发
            var status = passed
                ? (triggerCount >= 3 ? "就绪 ✅" : "基础通过 ⚠️")
                : "未就绪 ❌";

            Console.WriteLine($"   FC 就绪性: {triggerCount}/{testCases.Length} 触发 → {status}");
            detail.Insert(0, $"FC 就绪性检查: {triggerCount}/{testCases.Length} → {status}\n");

            return (passed, triggerCount, testCases.Length, detail.ToString());
        }

        // ═══════════════════════════════════════════
        // Phase 2c: 轻量级文本生成（无 Function Calling）
        // 用于对话摘要、上下文压缩等非主链路场景
        // ═══════════════════════════════════════════
        public async Task<string> GenerateSimpleResponseAsync(string prompt, int maxTokens = 512)
        {
            try
            {
                var settings = new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = null,  // 不触发工具调用
                    Temperature = 0.1,
                    MaxTokens = maxTokens
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await _kernel.InvokePromptAsync<string>(
                    prompt, new KernelArguments(settings), cancellationToken: cts.Token);

                return result ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ GenerateSimpleResponseAsync 失败: {ex.Message}");
                return "";
            }
        }

        // 生成单个文本的向量嵌入
        public async Task<float[]?> GetEmbeddingAsync(string text)
        {
            try
            {
                var config = AppConfig.Instance.VectorSearch;
                var embedEndpoint = config.EmbeddingEndpoint;
                var url = $"{embedEndpoint.TrimEnd('/')}/embeddings";
                // 创建请求对象
                var request = new
                {
                    //模型ID
                    model = config.EmbeddingModelId,
                    //输入文本
                    input = text
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(url, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"   ⚠️ 向量请求失败 [{response.StatusCode}]: {errorContent.Substring(0, Math.Min(200, errorContent.Length))}");
                    // 不返回随机向量（会污染向量库），返回 null 由调用方跳过向量写入
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseJson);
                var embedding = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

                if (!EvalMode.IsActive)
                    Console.WriteLine($"   ✅ 向量嵌入成功 (维度: {embedding.Length})");
                return embedding;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量嵌入失败: {ex.Message}");
                return null;
            }
        }

        // 批量生成向量嵌入
        public async Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts)
        {
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                var emb = await GetEmbeddingAsync(text);
                if (emb != null)
                    results.Add(emb);
            }
            return results.ToArray();
        }

        // Sprint 1: 真正的批量嵌入——单次 API 调用处理多个文本，利用 GPU 批处理
        public async Task<float[][]?> GetEmbeddingsBatchAsync(IEnumerable<string> texts)
        {
            var textList = texts.ToList();
            if (textList.Count == 0)
                return Array.Empty<float[]>();

            var config = AppConfig.Instance.VectorSearch;

            try
            {
                // 按配置的批大小拆分，防止单次请求过大
                var batchSize = Math.Max(1, config.EmbeddingBatchSize);
                var allEmbeddings = new List<float[]>();

                for (int batchIdx = 0; batchIdx < textList.Count; batchIdx += batchSize)
                {
                    var batch = textList.Skip(batchIdx).Take(batchSize).ToList();

                    var embedEndpoint = config.EmbeddingEndpoint;
                    var url = $"{embedEndpoint.TrimEnd('/')}/embeddings";

                    // 批量请求：llama.cpp /v1/embeddings 支持 input 为字符串数组
                    var request = new
                    {
                        model = config.EmbeddingModelId,
                        input = batch.ToArray()  // 数组形式，一次请求处理多个
                    };

                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.EmbeddingTimeoutSeconds));
                    var response = await _httpClient.PostAsync(url, content, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"   ⚠️ 批量向量请求失败 [{response.StatusCode}]: {errorContent.Substring(0, Math.Min(200, errorContent.Length))}");

                        // 降级：逐条调用
                        Console.WriteLine($"   🔄 降级为逐条嵌入...");
                        foreach (var text in batch)
                        {
                            var singleEmb = await GetEmbeddingAsync(text);
                            if (singleEmb != null)
                                allEmbeddings.Add(singleEmb);
                        }
                        continue;
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var dataArray = doc.RootElement.GetProperty("data");

                    foreach (var item in dataArray.EnumerateArray())
                    {
                        var embedding = item.GetProperty("embedding").EnumerateArray()
                            .Select(e => e.GetSingle())
                            .ToArray();
                        allEmbeddings.Add(embedding);
                    }

                    if (!EvalMode.IsActive)
                        Console.WriteLine($"   ✅ 批量嵌入成功: {batch.Count} 条, 维度={allEmbeddings.LastOrDefault()?.Length ?? 0}");
                }

                return allEmbeddings.ToArray();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"   ⚠️ 批量嵌入超时 ({config.EmbeddingTimeoutSeconds}s)，降级为逐条嵌入...");
                return await GetEmbeddingsAsync(textList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 批量嵌入失败: {ex.Message}，降级为逐条嵌入...");
                return await GetEmbeddingsAsync(textList);
            }
        }

        // 释放资源
        public void Dispose()
        {
            _httpClient?.Dispose();
            _injectedHttpClient?.Dispose();
        }

        /// <summary>
        /// [Thinking控制] 通过反射将 OllamaThinkingHandler 注入到 SK Ollama 连接器的内部 HttpClient。
        /// SK 1.74.0-alpha 实际链路:
        ///   ChatClientChatCompletionService → _chatClient (KernelFunctionInvokingChatClient)
        ///   → InnerClient(属性) → OllamaApiClient → _client (HttpClient)
        /// 采用递归搜索策略（字段+属性），自动适应 SK 内部结构变化。
        /// </summary>
        private void InjectThinkingHandler(Kernel kernel, OllamaThinkingHandler handler)
        {
            try
            {
                var chatService = kernel.GetRequiredService<IChatCompletionService>();

                // 递归搜索 HttpClient（最多深入 5 层，避免循环引用）
                var result = FindHttpClientField(chatService, 0, 5);
                if (result == null)
                {
                    Console.WriteLine("   ⚠️ [Thinking] 未能在 5 层内找到 HttpClient，跳过 handler 注入");
                    return;
                }

                var (owner, httpClientField) = result.Value;

                // 创建新的 HttpClient，将 handler 作为最内层 handler
                // [P0 FIX] 必须设置 BaseAddress，否则 SK 内部使用相对 URI (/api/chat) 时
                // 抛出 "An invalid request URI was provided"。BaseAddress 需以 / 结尾，
                // 确保与相对路径正确拼接（如 http://localhost:11434/ + api/chat = 正确 URL）。
                var baseAddr = ModelConfig.Endpoint;
                if (!baseAddr.AbsoluteUri.EndsWith("/"))
                    baseAddr = new Uri(baseAddr.AbsoluteUri + "/");
                // 先释放旧 HttpClient（如果存在），防止 socket 泄漏
                var oldClient = httpClientField.GetValue(owner) as HttpClient;
                if (oldClient != null && oldClient != _httpClient)
                {
                    oldClient.Dispose();
                }

                var newHttpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMinutes(15),
                    BaseAddress = baseAddr
                };
                httpClientField.SetValue(owner, newHttpClient);
                _injectedHttpClient = newHttpClient; // 追踪以便 Dispose

                Console.WriteLine($"   ✅ [Thinking] OllamaThinkingHandler 已注入 → {owner.GetType().Name}.{httpClientField.Name} (think 默认关闭)");
            }
            catch (Exception ex)
            {
                // 注入失败不应阻止程序启动
                Console.WriteLine($"   ⚠️ [Thinking] handler 注入异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归搜索对象图中的 HttpClient。
        /// SK 1.74.0-alpha 使用 Microsoft.Extensions.AI 装饰器模式，
        /// 内部客户端通过属性（InnerClient）而非字段暴露，因此同时搜索字段和属性。
        /// 返回 (拥有该字段的对象, FieldInfo) 或 null。
        /// </summary>
        private static (object owner, System.Reflection.FieldInfo field)? FindHttpClientField(object obj, int depth, int maxDepth)
        {
            if (obj == null || depth >= maxDepth)
                return null;

            var type = obj.GetType();
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var bfAll = bf | System.Reflection.BindingFlags.Public;

            // === 1. 搜索非公开字段 ===
            var fields = type.GetFields(bf);
            foreach (var f in fields)
            {
                var value = f.GetValue(obj);
                if (value is HttpClient || f.FieldType == typeof(HttpClient))
                    return (obj, f);
            }

            // === 2. 搜索属性（装饰器模式: InnerClient / InnerService） ===
            var props = type.GetProperties(bfAll);
            var childObjects = new List<object>();
            foreach (var p in props)
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    var value = p.GetValue(obj);
                    if (value == null) continue;
                    if (value is HttpClient)
                    {
                        var backingField = type.GetField("<" + p.Name + ">k__BackingField", bf);
                        if (backingField != null) return (obj, backingField);
                    }
                    var pt = p.PropertyType;
                    if (!pt.IsPrimitive && !pt.IsEnum && pt != typeof(string) && !pt.IsArray
                        && pt.FullName?.StartsWith("System.Collections") != true)
                        childObjects.Add(value);
                }
                catch { /* 某些属性可能有副作用 */ }
            }

            // === 3. 递归深入字段子对象 ===
            foreach (var f in fields)
            {
                var value = f.GetValue(obj);
                if (value == null) continue;
                var ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft.IsArray
                    || ft.FullName?.StartsWith("System.Collections") == true) continue;
                var found = FindHttpClientField(value, depth + 1, maxDepth);
                if (found != null) return found;
            }

            // === 4. 递归深入属性子对象 ===
            foreach (var child in childObjects)
            {
                var found = FindHttpClientField(child, depth + 1, maxDepth);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Phase 2a: 模型感知 — 判断当前模型是否需要过滤 &lt;think&gt; 标签。
        /// DeepSeek-R1 系列会在输出中嵌入思考过程标签，需要过滤。
        /// Qwen/GPT 等模型不需要此处理。Qwen3 在 Function Calling 场景下自动使用 non-thinking 模式,
        /// 但以防万一(如用户在菜单 1-7 中使用普通推理),也过滤 think 标签。
        /// </summary>
        private static bool ShouldFilterThinkTags()
        {
            var modelId = ModelConfig.ModelId.ToLowerInvariant();
            return modelId.Contains("deepseek-r1") || modelId.Contains("qwen3");
        }

        // ════════════════════════════════════════
        // [Thinking控制] Ollama 请求拦截器
        // ════════════════════════════════════════

        /// <summary>
        /// 拦截 SK → Ollama 的 /api/chat 请求，根据 EnableThinking 注入 think 参数。
        /// Qwen3 默认启用思考模式(生成大量 &lt;think&gt; token 导致超时)，
        /// Function Calling/评测场景需关闭思考，ReAct/Reflection 场景需开启。
        /// </summary>
        private class OllamaThinkingHandler : DelegatingHandler
        {
            private readonly LlmService _owner;

            public OllamaThinkingHandler(LlmService owner) : base(new HttpClientHandler())
            {
                _owner = owner;
            }

            /// <summary>
            /// 拦截请求并注入 think 参数。
            /// 一句话总结：它是一个"门卫"，在 HTTP 请求出门之前检查一下，
            /// 如果需要思考模式，就在 JSON 里塞一个 "think":true，然后放行。
            /// </summary>
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // 第1步：判断是不是发往 /api/chat 的 POST 请求
                if (request.Content != null
                    && request.RequestUri != null
                    && request.RequestUri.AbsolutePath.Contains("/api/chat")
                    && request.Method == HttpMethod.Post)
                {
                    // 第2步：读出原始JSON body
                    var body = await request.Content.ReadAsStringAsync(cancellationToken);


                    //第3步：只在 EnableThinking=true 且 body 里没有 think 参数时才注入
                    // 仅在显式要求思考时才注入 think:true；
                    // think:false 不再注入，让 Qwen3 根据 tools 在场自动选择 non-thinking（更快）
                    if (_owner.EnableThinking && !body.Contains("\"think\""))
                    {
                        var trimmed = body.TrimEnd();
                        if (trimmed.EndsWith("}"))
                        {
                            var enableValue = _owner.EnableThinking ? "true" : "false";
                            var newBody = trimmed.Substring(0, trimmed.Length - 1)
                                + $",\"think\":{enableValue}" + "}";
                            request.Content = new StringContent(newBody, Encoding.UTF8, "application/json");
                        }
                    }
                }
                // 第5步：把修改后的请求发给 llama.cpp
                return await base.SendAsync(request, cancellationToken);
            }
        }

        // ════════════════════════════════════════
        // E1 修复: FC 关键字兜底
        // ════════════════════════════════════════
        public async Task<List<FunctionCallRecord>?> TryKeywordToolFallbackAsync(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return null;
            var results = new List<FunctionCallRecord>();

            // 模式1: "{X}和{Y}可以同库储存吗"
            var storageMatch = Regex.Match(userInput, @"([\u4e00-\u9fa5a-zA-Z0-9]+)\s*[和与、]\s*([\u4e00-\u9fa5a-zA-Z0-9]+)\s*(?:可以|能否|是否可)?\s*(?:同库)?\s*[储贮存](?:兼容|禁忌|共存|安全)");
            if (storageMatch.Success)
            {
                var a = storageMatch.Groups[1].Value.Trim(); var b = storageMatch.Groups[2].Value.Trim();
                Console.WriteLine($"   [E1兜底] 同库储存 -> CheckStorageCompatibility(\"{a}\", \"{b}\")");
                try { var r = await _complianceTools.CheckStorageCompatibility(a, b); results.Add(new FunctionCallRecord { FunctionName = "CheckStorageCompatibility", Arguments = $"substanceA={a}, substanceB={b}", Result = r, Success = true, Quality = QualityLevel.DATABASE_HIT }); } catch (Exception ex) { Console.WriteLine($"   [E1兜底] 异常: {ex.Message}"); }
                return results;
            }

            // 模式2: "{X}的安全距离"
            var safetyMatch = Regex.Match(userInput, @"(?:([\u4e00-\u9fa5a-zA-Z0-9]+)\s*(?:和|与|、)\s*)?([\u4e00-\u9fa5a-zA-Z0-9]+)\s*(?:的)?\s*(?:安全距离|防火间距|最小间距|间距)");
            if (safetyMatch.Success)
            {
                var ft = safetyMatch.Groups[1].Success ? $"{safetyMatch.Groups[1].Value.Trim()}-{safetyMatch.Groups[2].Value.Trim()}" : safetyMatch.Groups[2].Value.Trim();
                Console.WriteLine($"   [E1兜底] 安全距离 -> GetSafetyDistance(\"{ft}\")");
                try { var r = await _complianceTools.GetSafetyDistance(ft); results.Add(new FunctionCallRecord { FunctionName = "GetSafetyDistance", Arguments = $"facilityType={ft}", Result = r, Success = true, Quality = QualityLevel.RAG_HIT }); } catch (Exception ex) { Console.WriteLine($"   [E1兜底] 异常: {ex.Message}"); }
                return results;
            }

            // 模式3: "{X}的危险类别"
            var hazardMatch = Regex.Match(userInput, @"([\u4e00-\u9fa5a-zA-Z0-9]+)\s*(?:的|属于)?\s*(?:危险类别|危险特性|GHS分类|什么危险)");
            if (hazardMatch.Success)
            {
                var sn = hazardMatch.Groups[1].Value.Trim();
                Console.WriteLine($"   [E1兜底] 危险类别 -> CheckHazardCategory(\"{sn}\")");
                try { var r = await _complianceTools.CheckHazardCategory(sn); results.Add(new FunctionCallRecord { FunctionName = "CheckHazardCategory", Arguments = $"substanceName={sn}", Result = r, Success = true, Quality = QualityLevel.DATABASE_HIT }); } catch (Exception ex) { Console.WriteLine($"   [E1兜底] 异常: {ex.Message}"); }
                return results;
            }

            return null;
        }

    }

    /// <summary>
    /// Task 7: LLM 熔断器打开异常 — 电路打开时快速拒绝请求，避免雪崩。
    /// GlobalExceptionMiddleware 会将其映射为 HTTP 503 Service Unavailable。
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    // ════════════════════════════════════════
    // Phase 2a 验证: 工具调用诊断数据模型
    // ════════════════════════════════════════

    /// <summary>SK Function Calling 单次调用记录</summary>
    public class FunctionCallRecord
    {
        public string FunctionName { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string? Result { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        /// <summary>[QF-2026-001] 工具返回值的质量等级（RAG_HIT/DATABASE_HIT/DICTIONARY_HIT/FALLBACK）</summary>
        public QualityLevel? Quality { get; set; }
    }

    /// <summary>
    /// SK Function Invocation 过滤器 — 拦截每次工具调用，记录到 LlmService.LastFunctionCalls
    /// 不修改调用行为，仅做观测
    /// </summary>
    public class FunctionCallDiagnosticsFilter : IFunctionInvocationFilter
    {
        private readonly LlmService _llmService;

        public FunctionCallDiagnosticsFilter(LlmService llmService)
        {
            _llmService = llmService;
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            // Phase 2a: 仅记录 ChemicalCompliance 插件的工具调用，过滤 SK 内部函数
            var pluginName = context.Function.PluginName ?? "";
            if (!pluginName.Equals("ChemicalCompliance", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            // [QF-2026-001] 每次工具调用前清除旧的质量上下文
            ToolQualityContext.Clear();

            var record = new FunctionCallRecord
            {
                FunctionName = context.Function.Name,
                Arguments = string.Join(", ", context.Arguments.Select(a => $"{a.Key}={a.Value}")),
                Timestamp = DateTime.Now
            };

            try
            {
                await next(context);
                record.Success = true;
                record.Result = context.Result?.ToString();
                // [QF-2026-001] 从 AsyncLocal 上下文读取工具质量标签
                record.Quality = ToolQualityContext.Current?.Quality;
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.Result = $"异常: {ex.Message}";
                record.Quality = QualityLevel.ERROR;
            }

            _llmService.LastFunctionCalls.Add(record);
        }
    }
}

