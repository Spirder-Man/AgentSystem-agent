
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
        // 合规关键词 — 覆盖化学品名称、危险属性、法规术语等典型查询特征
        private static readonly string[] ComplianceKeywords = new[]
        {
            // 法规与标准
            "国标", "法规", "合规", "GB", "GHS",
            // 危险属性（宽匹配："危险类别""危险特性""危险品"都能命中）
            "危险", "易燃", "易爆", "腐蚀", "毒性", "氧化",
            // 化学品通用标识
            "化学品", "危化品", "危险品", "化学",
            // 存储与安全
            "储存", "禁忌", "储罐", "间距", "安全距离", "泄露",
            "同库", "共存", "配伍", "混合",
            // 查询词（"属于什么类别""分类是什么"都能命中）
            "属于", "分类", "特性", "类别"
        };

        // 纯闲聊关键词 — 删除了过于通用的"什么""为什么""怎么"，避免误判合规查询
        private static readonly string[] SimpleChatKeywords = new[]
        {
            "你好", "hi", "hello", "在吗", "忙吗", "谢谢",
            "我叫", "我是", "名字", "再见", "好的", "明白了", "知道了",
            "刚才", "之前"
        };

        public static IntentType Route(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return IntentType.SimpleChat;

            var lower = userInput.ToLower();

            // 合规优先匹配：只要命中任一合规关键词就判定为合规查询
            if (ComplianceKeywords.Any(k => lower.Contains(k)))
                return IntentType.ChemicalCompliance;

            return IntentType.SimpleChat;
        }
    }
}

