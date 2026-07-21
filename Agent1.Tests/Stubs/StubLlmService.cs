// Stub ILlmService for EvalEngine integration testing
// Supports preset LLM responses for predictable test behavior

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Microsoft.SemanticKernel;

namespace Agent1.Tests.Stubs;

public class StubLlmService : ILlmService
{
    private string? _presetNonStreamingResponse;
    private string? _presetStreamingResponse;
    private readonly List<FunctionCallRecord> _presetFunctionCalls = new();

    /// <summary>Set the response returned by InvokeNonStreamingWithRetryAsync</summary>
    public void SetNonStreamingResponse(string response) => _presetNonStreamingResponse = response;

    /// <summary>Set the response returned by InvokeStreamWithRetryAsync</summary>
    public void SetStreamingResponse(string response) => _presetStreamingResponse = response;

    /// <summary>Add a preset function call record to LastFunctionCalls</summary>
    public void AddFunctionCall(FunctionCallRecord record) => _presetFunctionCalls.Add(record);

    /// <summary>Clear all preset function calls</summary>
    public void ClearFunctionCalls() => _presetFunctionCalls.Clear();

    public IReadOnlyList<FunctionCallRecord> LastFunctionCalls => _presetFunctionCalls.AsReadOnly();

    public Task<string> InvokeStreamAsync(string prompt, ConsoleColor color, FunctionChoiceBehavior? fcBehavior = null)
        => Task.FromResult(_presetStreamingResponse ?? "");

    public Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "")
        => Task.FromResult(_presetStreamingResponse ?? "");

    public Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName, FunctionChoiceBehavior? fcBehavior)
        => Task.FromResult(_presetStreamingResponse ?? "");

    public Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName = "")
        => Task.FromResult(_presetNonStreamingResponse ?? "");

    public Task<string> InvokeNonStreamingWithRetryAsync(string prompt, string stageName, FunctionChoiceBehavior? fcBehavior)
        => Task.FromResult(_presetNonStreamingResponse ?? "");

    public Task<float[]?> GetEmbeddingAsync(string text)
        => Task.FromResult<float[]?>(null);

    public Task<float[][]?> GetEmbeddingsAsync(IEnumerable<string> texts)
        => Task.FromResult<float[][]?>(null);

    public Task<float[][]?> GetEmbeddingsBatchAsync(IEnumerable<string> texts)
        => Task.FromResult<float[][]?>(null);

    public Task<string> GenerateSimpleResponseAsync(string prompt, int maxTokens = 512)
        => Task.FromResult(_presetStreamingResponse ?? "");
}
