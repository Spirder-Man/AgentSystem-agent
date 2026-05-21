
using System;
using System.Linq;

namespace Agent1
{
    public enum IntentType
    {
        Unknown = 0,
        SimpleChat = 1,
        IndustrialDiagnostic = 2
    }

    public static class IntentRouter
    {
        private static readonly string[] IndustrialKeywords = new[]
        {
            "温度", "机床", "主轴", "异常", "故障", "阈值", 
            "诊断", "检查", "传感器", "数据", "参数"
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

            if (IndustrialKeywords.Any(k => lower.Contains(k)))
                return IntentType.IndustrialDiagnostic;

            return IntentType.SimpleChat;
        }
    }
}

