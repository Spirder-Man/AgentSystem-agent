using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    public interface IMemoryService
    {
        /// <summary>Phase 1.1: 切换到指定会话的作用域</summary>
        void SetSession(string sessionId);

        string? TryAnswerFromMemory(string userInput);
        void ExtractAndStoreKeyFacts(string userInput, string assistantResponse);
        /// <summary>
        /// Phase 2c: 从工具执行结果中提取化工合规领域事实，存入当前会话的事实缓存
        /// </summary>
        void StoreToolFacts(string userInput, IReadOnlyDictionary<string, string> toolResults);
        void ClearMemory();
        Dictionary<string, string> GetKeyFacts();
        UserProfile GetUserProfile();

        // ── Phase 2c: 多轮上下文压缩 ──
        /// <summary>存储一轮对话（用于上下文积累和压缩触发判断）</summary>
        void StoreDialogueTurn(string userInput, string assistantResponse);
        /// <summary>获取压缩后的对话上下文（摘要 + 最近N轮原始对话）</summary>
        Task<string> GetConversationContextAsync(int maxRecentTurns = 5);
        /// <summary>获取当前未压缩的轮次数</summary>
        int GetDialogueTurnCount();

        // ── Phase 1.2: Token 感知 ──
        /// <summary>估算当前会话上下文的 token 数量</summary>
        int EstimateContextTokens();

        // ── Phase 1.3: 上下文卸载 ──
        /// <summary>卸载大型工具结果到文件，返回压缩后的引用文本</summary>
        string OffloadLargeResult(string toolName, string result);

        // ── Phase 1.4: 记忆统计 ──
        /// <summary>获取当前会话的记忆统计快照</summary>
        SessionMemoryStats GetSessionStats();
    }
}