
using System.Collections.Generic;

namespace Agent1.Services
{
    public class MemoryService : IMemoryService
    {
        private readonly UserProfile _userProfile;
        private readonly Dictionary<string, string> _keyFacts;

        public MemoryService()
        {
            _userProfile = new UserProfile();
            _keyFacts = new Dictionary<string, string>();
        }

        public string TryAnswerFromMemory(string userInput)
        {
            var lower = userInput.ToLower();

            if (lower.Contains("我叫") || lower.Contains("名字") || lower.Contains("我是谁"))
            {
                if (!string.IsNullOrEmpty(_userProfile.UserName))
                {
                    if (!string.IsNullOrEmpty(_userProfile.AssistantName))
                    {
                        return $"你叫 {_userProfile.UserName}，我叫 {_userProfile.AssistantName}！";
                    }
                    return $"你叫 {_userProfile.UserName}！";
                }
            }

            if (lower.Contains("你叫") || lower.Contains("你是谁"))
            {
                if (!string.IsNullOrEmpty(_userProfile.AssistantName))
                {
                    if (!string.IsNullOrEmpty(_userProfile.UserName))
                    {
                        return $"我叫 {_userProfile.AssistantName}，你叫 {_userProfile.UserName}！";
                    }
                    return $"我叫 {_userProfile.AssistantName}！";
                }
            }

            if ((lower.Contains("刚才") || lower.Contains("刚刚") || lower.Contains("之前"))
                && lower.Contains("温度"))
            {
                if (_keyFacts.ContainsKey("主轴温度"))
                {
                    return $"你好！之前检查的温度是 {_keyFacts["主轴温度"]}。还有什么需要我帮你的吗？";
                }
            }

            if (lower.Contains("阈值") || lower.Contains("安全"))
            {
                if (_keyFacts.ContainsKey("安全阈值"))
                {
                    return $"安全阈值是 {_keyFacts["安全阈值"]}。";
                }
            }

            return null;
        }

        public void ExtractAndStoreKeyFacts(string userInput, string assistantResponse)
        {
            var lowerInput = userInput.ToLower();
            var lowerResponse = assistantResponse.ToLower();

            if (lowerInput.Contains("我叫") || lowerInput.Contains("我是"))
            {
                var idx = lowerInput.IndexOf("我叫");
                if (idx < 0) idx = lowerInput.IndexOf("我是");
                
                if (idx >= 0)
                {
                    var nameStart = idx + 2;
                    if (nameStart < userInput.Length)
                    {
                        var nameEnd = userInput.IndexOf("，", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.IndexOf("。", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.Length;
                        
                        var name = userInput.Substring(nameStart, nameEnd - nameStart).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _userProfile.UserName = name;
                        }
                    }
                }
            }

            if (lowerInput.Contains("你叫"))
            {
                var idx = lowerInput.IndexOf("你叫");
                if (idx >= 0)
                {
                    var nameStart = idx + 2;
                    if (nameStart < userInput.Length)
                    {
                        var nameEnd = userInput.IndexOf("，", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.IndexOf("。", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.Length;
                        
                        var name = userInput.Substring(nameStart, nameEnd - nameStart).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _userProfile.AssistantName = name;
                        }
                    }
                }
            }

            if (lowerResponse.Contains("温度"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(assistantResponse, @"(\d+(\.\d+)?)\s*℃");
                if (match.Success)
                {
                    _keyFacts["主轴温度"] = match.Value;
                }
            }

            if (lowerResponse.Contains("阈值"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(assistantResponse, @"≤?\s*(\d+(\.\d+)?)\s*℃");
                if (match.Success)
                {
                    _keyFacts["安全阈值"] = match.Value;
                }
            }
        }

        public void ClearMemory()
        {
            _userProfile.UserName = string.Empty;
            _userProfile.JobTitle = string.Empty;
            _userProfile.AssistantName = string.Empty;
            _keyFacts.Clear();
        }

        public Dictionary<string, string> GetKeyFacts() => _keyFacts;

        public UserProfile GetUserProfile() => _userProfile;
    }
}

