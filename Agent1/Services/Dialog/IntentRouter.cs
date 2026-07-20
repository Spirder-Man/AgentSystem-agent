
using System;
using System.Linq;

namespace Agent1.Services
{
    /// <summary>
    /// P2-1: 意图类型扩展 —— 应急响应、知识图谱、法规审计各自独立路由。
    /// </summary>
    public enum IntentType
    {
        Unknown = 0,           // 默认未知意图
        SimpleChat = 1,        // 纯闲聊
        ChemicalCompliance = 2,// 化工合规（通用查询）
        Emergency = 3,         // 应急响应
        KnowledgeGraph = 4,    // 知识图谱查询
        RegulatoryAudit = 5,   // 法规审计
    }

    /// <summary>意图路由器</summary>
    public static class IntentRouter
    {
        /// <summary>最近一次路由决策匹配到的具体关键词（供审计追溯）</summary>
        public static string? LastMatchedKeyword { get; private set; }

        // ── P2-1: 应急响应关键词（优先级最高）──
        private static readonly string[] EmergencyKeywords = new[]
        {
            "应急", "泄漏", "泄露", "火灾", "爆炸", "疏散",
            "PPE", "ppe", "急救", "事故"
        };

        // ── P2-1: 法规审计关键词 ──
        private static readonly string[] RegulatoryAuditKeywords = new[]
        {
            "审计", "核查", "合规检查", "监管"
        };

        // ── P2-1: 知识图谱关键词 ──
        private static readonly string[] KnowledgeGraphKeywords = new[]
        {
            "知识图谱", "图谱查询", "实体"
        };

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

        /// <summary>
        /// 路由用户输入到相应的意图处理逻辑。
        /// 匹配优先级：应急 > 审计 > 知识图谱 > 合规 > 闲聊。
        /// 化工安全系统要求：每个路由决策必须可审计——记录匹配到的具体关键词。
        /// </summary>
        /// <param name="userInput">用户输入</param>
        /// <returns>意图类型</returns>
        public static IntentType Route(string userInput)
        {
            // 清空上次匹配到的关键词
            LastMatchedKeyword = null;
            // 空输入直接判定为闲聊
            if (string.IsNullOrWhiteSpace(userInput))
            {
                Serilog.Log.Information("[IntentRouter] 空输入 → SimpleChat");
                return IntentType.SimpleChat;
            }
            // 转换为小写
            var lower = userInput.ToLower();

            // P2-1: 应急响应优先匹配
            var matchedKeyword = EmergencyKeywords.FirstOrDefault(k => lower.Contains(k));
            if (matchedKeyword != null)
            {
                LastMatchedKeyword = matchedKeyword;
                Serilog.Log.Information("[IntentRouter] 关键词 \"{Keyword}\" 命中 → Emergency | 输入: {Input}",
                    matchedKeyword, userInput.Truncate(80));
                return IntentType.Emergency;
            }

            // P2-1: 法规审计匹配
            matchedKeyword = RegulatoryAuditKeywords.FirstOrDefault(k => lower.Contains(k));
            if (matchedKeyword != null)
            {
                LastMatchedKeyword = matchedKeyword;
                Serilog.Log.Information("[IntentRouter] 关键词 \"{Keyword}\" 命中 → RegulatoryAudit | 输入: {Input}",
                    matchedKeyword, userInput.Truncate(80));
                return IntentType.RegulatoryAudit;
            }

            // P2-1: 知识图谱匹配
            matchedKeyword = KnowledgeGraphKeywords.FirstOrDefault(k => lower.Contains(k));
            if (matchedKeyword != null)
            {
                LastMatchedKeyword = matchedKeyword;
                Serilog.Log.Information("[IntentRouter] 关键词 \"{Keyword}\" 命中 → KnowledgeGraph | 输入: {Input}",
                    matchedKeyword, userInput.Truncate(80));
                return IntentType.KnowledgeGraph;
            }

            // 合规关键词匹配
            matchedKeyword = ComplianceKeywords.FirstOrDefault(k => lower.Contains(k));
            if (matchedKeyword != null)
            {
                LastMatchedKeyword = matchedKeyword;
                Serilog.Log.Information("[IntentRouter] 关键词 \"{Keyword}\" 命中 → ChemicalCompliance | 输入: {Input}",
                    matchedKeyword, userInput.Truncate(80));
                return IntentType.ChemicalCompliance;
            }
            // 闲聊关键词匹配
            Serilog.Log.Information("[IntentRouter] 无合规关键词命中 → SimpleChat | 输入: {Input}",
                userInput.Truncate(80));
            // 返回闲聊意图
            return IntentType.SimpleChat;
        }
    }
}

