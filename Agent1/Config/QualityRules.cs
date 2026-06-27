using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Agent1.Config
{
    /// <summary>
    /// [QF-2026-001] 质量规则加载器 — 从 quality-rules.json 加载声明式规则，
    /// 供 StoreToolFacts 写入门禁和 TryAnswerFromMemory 读出门禁使用。
    /// </summary>
    public class QualityRules
    {
        private static QualityRules? _instance;
        private static readonly object _lock = new();

        public static QualityRules Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        public List<QualityRule> Rules { get; init; } = new();
        public List<string> FallbackPatterns { get; init; } = new();
        public QualityTtlStrategy TtlStrategy { get; init; } = new();

        private static QualityRules Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "quality-rules.json");
            if (!File.Exists(path))
            {
                Console.WriteLine("   ⚠️ quality-rules.json 未找到，使用内置默认规则");
                return CreateDefault();
            }

            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var rules = new List<QualityRule>();
                var patterns = new List<string>();
                var ttlStrategy = new QualityTtlStrategy();

                if (root.TryGetProperty("rules", out var rulesElem))
                {
                    foreach (var rule in rulesElem.EnumerateArray())
                    {
                        var r = new QualityRule
                        {
                            Id = rule.GetProperty("id").GetString() ?? "",
                            Name = rule.GetProperty("name").GetString() ?? "",
                            Severity = rule.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "BLOCK" : "BLOCK",
                            Action = rule.TryGetProperty("action", out var act) ? act.GetString() ?? "REJECT" : "REJECT"
                        };

                        if (rule.TryGetProperty("fallback_patterns", out var fps))
                        {
                            foreach (var fp in fps.EnumerateArray())
                            {
                                var p = fp.GetString();
                                if (!string.IsNullOrWhiteSpace(p))
                                    patterns.Add(p);
                            }
                        }
                        rules.Add(r);
                    }
                }

                if (root.TryGetProperty("cache_ttl_strategy", out var ttlElem))
                {
                    ttlStrategy.DefaultTtlMinutes = ttlElem.TryGetProperty("default_ttl_minutes", out var def)
                        ? def.GetInt32() : 5;
                    ttlStrategy.UnknownQualityTtlMinutes = ttlElem.TryGetProperty("unknown_quality_ttl_minutes", out var unk)
                        ? unk.GetInt32() : 1;
                }

                if (root.TryGetProperty("quality_levels", out var qls))
                {
                    foreach (var ql in qls.EnumerateObject())
                    {
                        if (ql.Value.TryGetProperty("cache_ttl_minutes", out var ttl))
                            ttlStrategy.QualityTtls[ql.Name] = ttl.GetInt32();
                    }
                }

                Console.WriteLine($"   📋 质量规则已加载: {rules.Count} 条规则, {patterns.Count} 个兜底模式");
                return new QualityRules
                {
                    Rules = rules,
                    FallbackPatterns = patterns.Count > 0 ? patterns : GetDefaultPatterns(),
                    TtlStrategy = ttlStrategy
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 加载 quality-rules.json 失败: {ex.Message}，使用内置默认规则");
                return CreateDefault();
            }
        }

        private static QualityRules CreateDefault()
        {
            return new QualityRules
            {
                Rules = new List<QualityRule>
                {
                    new() { Id = "R001", Name = "禁止缓存兜底文本", Severity = "BLOCK", Action = "REJECT" },
                    new() { Id = "R002", Name = "缓存读取必须验证置信度", Severity = "BLOCK", Action = "RETURN_NULL" }
                },
                FallbackPatterns = GetDefaultPatterns(),
                TtlStrategy = new QualityTtlStrategy()
            };
        }

        private static List<string> GetDefaultPatterns()
        {
            return new List<string>
            {
                @"未在常见.*中直接匹配",
                @"建议查阅.*全文",
                @"未找到.*数据",
                @"建议参考.*查询",
                @"在常见禁忌表中未发现直接冲突"
            };
        }
    }

    public class QualityRule
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Severity { get; set; } = "BLOCK";
        public string Action { get; set; } = "REJECT";
    }

    public class QualityTtlStrategy
    {
        public int DefaultTtlMinutes { get; set; } = 5;
        public int UnknownQualityTtlMinutes { get; set; } = 1;
        public Dictionary<string, int> QualityTtls { get; set; } = new()
        {
            ["RAG_HIT"] = 10,
            ["DATABASE_HIT"] = 10,
            ["DICTIONARY_HIT"] = 5,
            ["FALLBACK"] = 0,
            ["ERROR"] = 0
        };
    }
}
