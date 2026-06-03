

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using Agent1.Config;

namespace Agent1.Services
{
    public class LlmService : ILlmService
    {
        private readonly Kernel _kernel;
        private readonly HttpClient _httpClient;
        private readonly ChemicalComplianceTools _complianceTools;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;

        // Phase 2a 验证: 工具调用诊断 — 记录最近一次 SK Auto Function Calling 的执行信息
        public List<FunctionCallRecord> LastFunctionCalls { get; private set; } = new();

        public LlmService(IKnowledgeBaseService kbService)
        {
            _complianceTools = new ChemicalComplianceTools(kbService);
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint);
            // Phase 2a: 使用 RAG-backed 实例注册，SK Auto Function Calling 直接调用知识库检索
            kernelBuilder.Plugins.AddFromObject(_complianceTools, "ChemicalCompliance");
            _kernel = kernelBuilder.Build();
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };

            // Phase 2a 验证: 注册 Function Calling 拦截过滤器，捕获每次工具调用
            _kernel.FunctionInvocationFilters.Add(new FunctionCallDiagnosticsFilter(this));
        }

        /// <summary>
        /// Phase 2a DI 循环修复: 延迟注入 RAG 知识库服务。
        /// DI 容器解析完成后由 Program.cs 调用，将 null kbService 替换为真实实例。
        /// </summary>
        public void SetKnowledgeBaseService(IKnowledgeBaseService kbService)
        {
            _complianceTools.SetKnowledgeBaseService(kbService);
        }

        public async Task<string> InvokeStreamAsync(string prompt, ConsoleColor color)
        {
            var result = new StringBuilder();
            Console.WriteLine();
            Console.ForegroundColor = color;

            bool isInThinkBlock = false;
            string buffer = "";
            int bufferFlushThreshold = 50;

            // Phase 2a: 模型感知的 think 标签过滤 — 仅 DeepSeek-R1 需要
            bool filterThinkTags = ShouldFilterThinkTags();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            try
            {
                // Phase 2a: 启用 SK Auto Function Calling — LLM 自主决定调用工具
                var settings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                    Temperature = 0.3,
                };

                // Phase 2a 验证: 清空上一轮工具调用记录
                LastFunctionCalls.Clear();

                Console.WriteLine($"   [SK诊断] 模型={ModelConfig.ModelId}, FC=AutoInvokeKernelFunctions");

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

        public async Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "")
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        Console.WriteLine($"\n🔄 第{attempt}次重试 {stageName}...");
                    }

                    return await InvokeStreamAsync(prompt, color);
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                    Console.WriteLine($"\n⏰ 请求超时 ({attempt}/{MaxRetries}): {ex.Message}");

                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"\n❌ 错误 ({attempt}/{MaxRetries}): {ex.Message}");

                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs);
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ 所有重试失败: {lastException?.Message}");
            Console.ResetColor();

            return $"生成失败: {lastException?.Message}";
        }

        /// <summary>
        /// Phase 2a 评测加速: 非流式调用 — 跳过流式缓冲/控制台打印/think过滤，
        /// 直接拿完整结果。用于批量评测场景，相比流式节省 30-40% CPU 时间。
        /// </summary>
        public async Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName = "")
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                        Console.WriteLine($"   🔄 第{attempt}次重试 {stageName}...");

                    var settings = new OpenAIPromptExecutionSettings
                    {
                        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                        Temperature = 0.3,
                    };

                    // 清空上一轮工具调用记录
                    LastFunctionCalls.Clear();

                    var kernelResult = await _kernel.InvokePromptAsync(
                        prompt, new KernelArguments(settings));

                    return kernelResult.ToString();
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                    Console.WriteLine($"   ⏰ 请求超时 ({attempt}/{MaxRetries}): {ex.Message}");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * 2);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"   ❌ 错误 ({attempt}/{MaxRetries}): {ex.Message}");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * 2);
                }
            }

            Console.WriteLine($"   ❌ 所有重试失败: {lastException?.Message}");
            return $"生成失败: {lastException?.Message}";
        }

        // 生成单个文本的向量嵌入
        public async Task<float[]?> GetEmbeddingAsync(string text)
        {
            try
            {
                var config = AppConfig.Instance.VectorSearch;
                var baseUrl = ModelConfig.Endpoint;
                var url = new Uri(baseUrl, "/api/embeddings").ToString();
                // 创建请求对象
                var request = new
                {
                    //模型ID
                    model = config.EmbeddingModelId,
                    //输入文本
                    prompt = text
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
                var embedding = doc.RootElement.GetProperty("embedding").EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

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

        // 释放资源
        public void Dispose()
        {
            _httpClient?.Dispose();
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
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.Result = $"异常: {ex.Message}";
            }

            _llmService.LastFunctionCalls.Add(record);
        }
    }
}

