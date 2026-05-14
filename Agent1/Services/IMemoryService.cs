using System.Collections.Generic;

namespace Agent1.Services
{
    public interface IMemoryService
    {
        string TryAnswerFromMemory(string userInput);
        void ExtractAndStoreKeyFacts(string userInput, string assistantResponse);
        void ClearMemory();
        Dictionary<string, string> GetKeyFacts();
        UserProfile GetUserProfile();
    }
}