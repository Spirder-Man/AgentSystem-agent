
using System;
using System.Linq;

namespace Agent1
{
    public enum IntentType
    {
        Unknown = 0,
        SimpleChat = 1,
        ChemicalCompliance = 2
    }

    public static class IntentRouter
    {
        private static readonly string[] ComplianceKeywords = new[]
        {
            "危化品", "化学品", "合规", "储存", "国标", "法规",
            "安全距离", "危险类别", "禁忌", "储罐", "间距", "泄露"
        };

        private static readonly string[] SimpleChatKeywords = new[]
        {
            "你好", "hi", "hello", "在吗", "忙吗", "谢谢", 
            "我叫", "我是", "名字", "再见", "好的", "明白了", "知道了",
            "哪个", "什么", "为什么", "怎么", "刚才", "之前"
        };

        public static IntentType Route(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return IntentType.SimpleChat;

            var lower = userInput.ToLower();

            if (ComplianceKeywords.Any(k => lower.Contains(k)))
                return IntentType.ChemicalCompliance;

            return IntentType.SimpleChat;
        }
    }
}

