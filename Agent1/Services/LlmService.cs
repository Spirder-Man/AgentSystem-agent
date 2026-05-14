

using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public class LlmService : ILlmService
    {
        private readonly Kernel _kernel;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;

        public LlmService()
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint);
            kernelBuilder.Plugins.AddFromType<IndustrialTools>();
            _kernel = kernelBuilder.Build();
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
    }
}

