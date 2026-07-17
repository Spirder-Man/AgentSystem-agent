using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Models;
using Microsoft.SemanticKernel;

namespace Agent1.Services
{
    public interface ILlmService
    {
        Task<string> InvokeStreamAsync(string prompt, ConsoleColor color, FunctionChoiceBehavior? fcBehavior = null);
        Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "");
        Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName, FunctionChoiceBehavior? fcBehavior);
        Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName = "");
        Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName, FunctionChoiceBehavior? fcBehavior);
        
        // 新增：向量嵌入方法（失败时返回 null，调用方应跳过向量写入）
        Task<float[]?> GetEmbeddingAsync(string text);
        Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts);

        // Sprint 1: 批量嵌入（单次 API 调用处理多个文本，利用 GPU 批处理能力）
        Task<float[][]?> GetEmbeddingsBatchAsync(IEnumerable<string> texts);

        // Phase 2c: 轻量级文本生成（无工具调用，用于摘要/压缩）
        Task<string> GenerateSimpleResponseAsync(string prompt, int maxTokens = 512);

        // P2-2: 暴露最近一次 Function Calling 的工具调用记录，消除 as LlmService 向下转型
        IReadOnlyList<FunctionCallRecord> LastFunctionCalls { get; }
    }
}