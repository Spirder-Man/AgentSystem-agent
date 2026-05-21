using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public interface ILlmService
    {
        Task<string> InvokeStreamAsync(string prompt, ConsoleColor color);
        Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "");
        
        // 新增：向量嵌入方法
        Task<float[]> GetEmbeddingAsync(string text);
        Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts);
    }
}