

using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    public class LlmService : ILlmService
    {
        private readonly Kernel _kernel;
        private readonly HttpClient _httpClient;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;

        public LlmService()
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint);
            kernelBuilder.Plugins.AddFromType<ChemicalComplianceTools>(); // P1: 切换为化工合规工具集，替代工业温度/主轴工具
            _kernel = kernelBuilder.Build();
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
        }

        public async Task<string> InvokeStreamAsync(string prompt, ConsoleColor color)
        {
            var result = new StringBuilder();
            Console.WriteLine();
            Console.ForegroundColor = color;

            bool isInThinkBlock = false;
            string buffer = "";
            int bufferFlushThreshold = 50;

            try
            {
                await foreach (var chunk in _kernel.InvokePromptStreamingAsync<string>(prompt))
                {
                    buffer += chunk;

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
                if (!isInThinkBlock && buffer.Length > 0)
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

            // 先彻底移除所有 <think> 和 </think> 标记
            content = content.Replace("<think>", "").Replace("</think>", "");

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

        // 生成单个文本的向量嵌入
        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            try
            {
                var config = AppConfig.Instance.VectorSearch;
                var baseUrl = ModelConfig.Endpoint;
                var url = new Uri(baseUrl, "/api/embeddings").ToString();
                
                var request = new
                {
                    model = config.EmbeddingModelId,
                    prompt = text
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                Console.WriteLine($"   📤 请求 URL: {url}");
                Console.WriteLine($"   📤 请求方法: POST");
                Console.WriteLine($"   📤 请求 Body: {json}");

                var response = await _httpClient.PostAsync(url, content);
                
                Console.WriteLine($"   📥 响应状态码: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"   📥 错误响应: {errorContent}");
                }
                
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"   📥 响应 Body: {responseJson}");
                
                using var doc = JsonDocument.Parse(responseJson);
                var embedding = doc.RootElement.GetProperty("embedding").EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();

                Console.WriteLine($"   ✅ 向量嵌入生成成功 (维度: {embedding.Length})");
                return embedding;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量嵌入生成失败: {ex.Message}");
                Console.WriteLine($"   🔍 完整堆栈: {ex.StackTrace}");
                
                // 返回随机向量作为降级方案
                var random = new Random();
                var fallback = new float[AppConfig.Instance.VectorSearch.EmbeddingDimension];
                for (int i = 0; i < fallback.Length; i++)
                {
                    fallback[i] = (float)(random.NextDouble() * 2 - 1); // -1 到 1 之间
                }
                return fallback;
            }
        }

        // 批量生成向量嵌入
        public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts)
        {
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                results.Add(await GetEmbeddingAsync(text));
            }
            return results.ToArray();
        }

        // 释放资源
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

