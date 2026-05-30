using System.Collections.Generic;

namespace Agent1.Services
{
    public interface IMemoryService
    {
        string? TryAnswerFromMemory(string userInput);
        void ExtractAndStoreKeyFacts(string userInput, string assistantResponse);
        /// <summary>
        /// Phase 2c: 从工具执行结果中提取化工合规领域事实，存入 _keyFacts 缓存
        /// </summary>
        void StoreToolFacts(string userInput, IReadOnlyDictionary<string, string> toolResults);
        void ClearMemory();
        Dictionary<string, string> GetKeyFacts();
        UserProfile GetUserProfile();
    }
}