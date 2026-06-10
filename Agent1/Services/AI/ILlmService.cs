using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public interface ILlmService
    {
        Task<string> InvokeStreamAsync(string prompt, ConsoleColor color);
        Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "");
        
        // 新增：向量嵌入方法（失败时返回 null，调用方应跳过向量写入）
        Task<float[]?> GetEmbeddingAsync(string text);
        Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts);

        // Phase 2c: 轻量级文本生成（无工具调用，用于摘要/压缩）
        Task<string> GenerateSimpleResponseAsync(string prompt, int maxTokens = 512);
    }
}