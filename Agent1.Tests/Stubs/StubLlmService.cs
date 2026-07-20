using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;
using Microsoft.SemanticKernel;

namespace Agent1.Tests.Stubs;

/// <summary>
/// ILlmService 存根 — 用于 ApiIntegrationTests。
/// 返回固定 mock 响应，避免对 llama.cpp 的真实 HTTP 调用。
/// 同时实现 LlmService 的行为特征（LastFunctionCalls 属性）供 (LlmService) 转型使用。
/// </summary>
public class StubLlmService : ILlmService
{
    /// <summary>模拟最近一次函数调用记录</summary>
    public List<FunctionCallRecord> LastFunctionCalls { get; set; } = new();
    IReadOnlyList<FunctionCallRecord> ILlmService.LastFunctionCalls => LastFunctionCalls;

    /// <summary>固定的 mock 嵌入向量（768 维，与 nomic-embed-text 一致）</summary>
    private static readonly float[] MockEmbedding = Enumerable.Range(0, 768).Select(i => 0.01f * (i % 100)).ToArray();

    public Task<string> InvokeStreamAsync(string prompt, ConsoleColor color,
        FunctionChoiceBehavior? fcBehavior = null)
    {
        return Task.FromResult("[Mock] 合规审核结果: 该操作符合 GB 30000.7-2013 相关要求。[判定:is_compliant=true]");
    }

    public Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color,
        string stageName = "")
    {
        return Task.FromResult($"[Mock:{stageName}] 处理完成。");
    }

    public Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color,
        string stageName, FunctionChoiceBehavior? fcBehavior)
    {
        return Task.FromResult($"[Mock:{stageName}] 处理完成。");
    }

    public Task<string> InvokeNonStreamingWithRetryAsync(string prompt,
        string stageName = "")
    {
        // 模拟 AgentDialog.ExecuteEvalFastAsync 的响应格式
        if (prompt.Contains("EvalFastPrompt") || prompt.Contains("合规判断"))
        {
            LastFunctionCalls = new List<FunctionCallRecord>
            {
                new() { FunctionName = "CheckHazardCategory", Arguments = "{\"substance\":\"测试\"}" }
            };
            return Task.FromResult(
                "【合规判断】是\n" +
                "【法规依据】GB 30000.7-2013 第4.1条\n" +
                "【违规点】无\n" +
                "【整改建议】无需整改\n" +
                "[判定:is_compliant=true]");
        }
        return Task.FromResult($"[Mock:{stageName}] 处理完成。");
    }

    public Task<string> InvokeNonStreamingWithRetryAsync(string prompt,
        string stageName, FunctionChoiceBehavior? fcBehavior)
    {
        return InvokeNonStreamingWithRetryAsync(prompt, stageName);
    }

    public Task<float[]?> GetEmbeddingAsync(string text)
    {
        return Task.FromResult<float[]?>(MockEmbedding);
    }

    public Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        var count = texts.Count();
        var result = Enumerable.Range(0, count).Select(_ => MockEmbedding).ToArray();
        return Task.FromResult<float[][]?>(result);
    }

    public Task<float[][]?> GetEmbeddingsBatchAsync(IEnumerable<string> texts)
    {
        return GetEmbeddingsAsync(texts);
    }

    public Task<string> GenerateSimpleResponseAsync(string prompt, int maxTokens = 512)
    {
        return Task.FromResult($"[Mock] 简单响应: {prompt[..Math.Min(prompt.Length, 50)]}...");
    }
}
