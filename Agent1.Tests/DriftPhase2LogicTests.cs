using System.Collections.Generic;
using System.Linq;
using Agent1.Services.DriftMonitor;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 认知漂移监测 Phase 2 纯逻辑单元测试（不依赖数据库）：
/// 文本规范化、强标记抽取、匹配打分、断言抽取、漂移量计算。
/// </summary>
public class DriftPhase2LogicTests
{
    // ═══════════════════════════════════════════
    // DriftMatcher.Normalize — 文本规范化
    // ═══════════════════════════════════════════

    [Fact]
    public void Normalize_FullWidthToHalfWidth_Lowercase_NoWhitespace()
    {
        DriftMatcher.Normalize("ＡＰＩ 监听 :5000").Should().Be("api监听:5000");
    }

    [Fact]
    public void Normalize_MixedCasePath_Lowercased()
    {
        DriftMatcher.Normalize("Data/ComplianceEvalSet.JSON").Should().Be("data/complianceevalset.json");
    }

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        DriftMatcher.Normalize("   ").Should().Be("");
        DriftMatcher.Normalize(null!).Should().Be("");
    }

    // ═══════════════════════════════════════════
    // DriftMatcher.ExtractStrongTokens — 强标记抽取
    // ═══════════════════════════════════════════

    [Fact]
    public void ExtractStrongTokens_PortPathUpperKey_AllExtracted()
    {
        var value = "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000, 见 Agent1.Api/Program.cs L42";

        var tokens = DriftMatcher.ExtractStrongTokens(value);

        tokens.Should().Contain("5000");                     // 端口
        tokens.Should().Contain("aspnetcore_urls");         // 大写标识符（已小写规范化）
        tokens.Should().Contain("agent1.api/program.cs");   // 路径（规范化小写）
    }

    [Fact]
    public void ExtractStrongTokens_ShortNumbers_NotExtracted()
    {
        // 1-2 位数字（数量/年份）不是强标记——避免测量噪音
        DriftMatcher.ExtractStrongTokens("35 物质/21 距离/20 配伍").Should().BeEmpty();
    }

    [Fact]
    public void IsFalsifiable_DescriptiveOnlyAnchor_ReturnsFalse()
    {
        var anchor = new DriftAnchor { CanonicalValue = "SQLite 排 Level1, PG 排 Level2(字段更全)" };

        DriftMatcher.IsFalsifiable(anchor).Should().BeFalse();
    }

    // ═══════════════════════════════════════════
    // DriftMatcher.MatchScore — 匹配打分
    // ═══════════════════════════════════════════

    [Fact]
    public void MatchScore_AllTokensHit_Returns1()
    {
        // 锚点：API监听端口 = ASPNETCORE_URLS 控制, 默认 5000
        var score = DriftMatcher.MatchScore(
            "API 监听端口是 ASPNETCORE_URLS 控制的 5000",
            "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000");

        score.Should().Be(1.0);
    }

    [Fact]
    public void MatchScore_WrongPort_Returns0()
    {
        var score = DriftMatcher.MatchScore(
            "API 监听端口是 8080",
            "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000");

        score.Should().Be(0.0);
    }

    [Fact]
    public void MatchScore_PartialHit_Returns0_5()
    {
        // 只命中 ASPNETCORE_URLS，端口错误 → 部分漂移
        var score = DriftMatcher.MatchScore(
            "API 监听端口由 ASPNETCORE_URLS 控制，当前是 9090",
            "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000");

        score.Should().Be(0.5);
    }

    [Fact]
    public void MatchScore_NotFalsifiable_Returns1()
    {
        // 纯描述性锚点无法证伪 → 提及即视为一致
        var score = DriftMatcher.MatchScore(
            "降级顺序是 SQLite 第一",
            "SQLite 排 Level1, PG 排 Level2");

        score.Should().Be(1.0);
    }

    [Fact]
    public void ExtractStrongTokens_CamelCaseCodeSymbol_Extracted()
    {
        // 驼峰类名（代码符号）成为强标记——乱码闸门锚点因此可证伪
        var tokens = DriftMatcher.ExtractStrongTokens(
            "GarbledTextDetector 三规则确定性拒收, 乱码块不准入库");

        tokens.Should().Contain("garbledtextdetector");
    }

    [Fact]
    public void ExtractStrongTokens_ProductNames_NotExtracted()
    {
        // 产品名/通用词/版本号不算代码符号——防误伤契约：
        // SQLite（全大写前缀）/ Vite（无第二大写）/ Level1（无第二大写）/ Agent1.Api（数字嵌首）
        DriftMatcher.ExtractStrongTokens("SQLite 排 Level1, Vite 开发, Agent1.Api 启动")
            .Should().NotContain("sqlite")
            .And.NotContain("vite")
            .And.NotContain("level1")
            .And.NotContain("agent1.api");
    }

    [Fact]
    public void IsFalsifiable_GarbledGateAnchor_ReturnsTrue()
    {
        // 004 迁移真实锚点：乱码闸门——升级后从不可证伪变为可证伪
        var anchor = new DriftAnchor { CanonicalValue = "GarbledTextDetector 三规则确定性拒收, 乱码块不准入库" };

        DriftMatcher.IsFalsifiable(anchor).Should().BeTrue();
    }

    [Fact]
    public void MatchScore_GarbledGate_CorrectAnswer_Returns1()
    {
        // 答对（提到类名）→ 一致
        var score = DriftMatcher.MatchScore(
            "乱码文本块在入库前由 GarbledTextDetector 拦截",
            "GarbledTextDetector 三规则确定性拒收, 乱码块不准入库");

        score.Should().Be(1.0);
    }

    [Fact]
    public void MatchScore_GarbledGate_WrongAnswer_Returns0()
    {
        // 答错（未提类名）→ 全漂移——修复前这条永远判 1.0
        var score = DriftMatcher.MatchScore(
            "乱码文本块在入库前由正则表达式拦截",
            "GarbledTextDetector 三规则确定性拒收, 乱码块不准入库");

        score.Should().Be(0.0);
    }

    // ═══════════════════════════════════════════
    // DriftClaimExtractor — 断言抽取
    // ═══════════════════════════════════════════

    [Fact]
    public void Extract_TextMentionsAnchorKey_ProducesClaim()
    {
        var anchors = new List<DriftAnchor>
        {
            new() { EntityKey = "API监听端口", CanonicalValue = "ASPNETCORE_URLS 控制, 默认 5000", Severity = 2 },
            new() { EntityKey = "图谱无兜底", CanonicalValue = "PG 不可用→启动失败(官方铜牌)", Severity = 2 }
        };

        var claims = new DriftClaimExtractor().Extract("API 监听端口是 5000", anchors);

        claims.Should().ContainSingle();
        claims[0].Anchor.EntityKey.Should().Be("API监听端口");
    }

    [Fact]
    public void Extract_NotFalsifiableAnchor_NoClaim()
    {
        var anchors = new List<DriftAnchor>
        {
            new() { EntityKey = "图谱无兜底", CanonicalValue = "PG 不可用→启动失败(官方铜牌)", Severity = 2 }
        };

        var claims = new DriftClaimExtractor().Extract("图谱无兜底：PG 不可用就启动失败", anchors);

        claims.Should().BeEmpty(); // 无强标记 → 无法证伪 → 不产生断言
    }

    [Fact]
    public void Extract_NoMention_NoClaim()
    {
        var anchors = new List<DriftAnchor>
        {
            new() { EntityKey = "API监听端口", CanonicalValue = "ASPNETCORE_URLS 控制, 默认 5000", Severity = 2 }
        };

        var claims = new DriftClaimExtractor().Extract("今天天气不错", anchors);

        claims.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════
    // DriftMetricsService — 漂移量计算
    // ═══════════════════════════════════════════

    [Fact]
    public void Compute_AllConsistent_ScoreZero()
    {
        var matches = new List<ClaimMatch>
        {
            new() { Anchor = Anchor("端口A", "5000", 2), Score = 1.0 },
            new() { Anchor = Anchor("路径B", "a.cs", 1), Score = 1.0 }
        };

        var result = new DriftMetricsService().Compute(matches);

        result.ClaimCount.Should().Be(2);
        result.MatchCount.Should().Be(2);
        result.DriftScore.Should().Be(0.0);
    }

    [Fact]
    public void Compute_StructuralDrift_WeightedDouble()
    {
        // 结构级(sev2)全漂移 + 参考级(sev1)一致 → D = 2/(2+1) = 0.6667
        var matches = new List<ClaimMatch>
        {
            new() { Anchor = Anchor("API监听端口", "5000", 2), Score = 0.0 },
            new() { Anchor = Anchor("Seq端口", "5341", 1), Score = 1.0 }
        };

        var result = new DriftMetricsService().Compute(matches);

        result.DriftScore.Should().BeApproximately(2.0 / 3.0, 0.0001);
        result.MatchCount.Should().Be(1);
    }

    [Fact]
    public void Compute_PartialDrift_Score0_5()
    {
        var matches = new List<ClaimMatch>
        {
            new() { Anchor = Anchor("API监听端口", "5000", 2), Score = 0.5 }
        };

        var result = new DriftMetricsService().Compute(matches);

        result.DriftScore.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void Compute_DomainBreakdown_GroupedAndSorted()
    {
        var matches = new List<ClaimMatch>
        {
            new() { Anchor = Anchor("端口A", "5000", 2, "port"), Score = 0.0 },
            new() { Anchor = Anchor("端口B", "5341", 1, "port"), Score = 1.0 },
            new() { Anchor = Anchor("表A", "audit_logs", 2, "data"), Score = 0.0 }
        };

        var result = new DriftMetricsService().Compute(matches);

        result.DomainBreakdown.Should().HaveCount(2);
        // data 域 1/1 漂移 > port 域 1/2 漂移 → 降序
        result.DomainBreakdown[0].Domain.Should().Be("data");
        result.DomainBreakdown[0].Score.Should().Be(1.0);
        result.DomainBreakdown[1].Domain.Should().Be("port");
        result.DomainBreakdown[1].Score.Should().Be(0.5);
    }

    [Fact]
    public void Compute_Empty_ZeroResult()
    {
        var result = new DriftMetricsService().Compute(new List<ClaimMatch>());

        result.ClaimCount.Should().Be(0);
        result.DriftScore.Should().Be(0.0);
        result.DomainBreakdown.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════
    // 端到端纯逻辑链路：抽取 → 比对 → 度量（模拟漂移检测）
    // ═══════════════════════════════════════════

    [Fact]
    public void FullPipeline_DriftingText_ProducesDriftScore()
    {
        // 模拟：AI 长对话后把端口记错（5000 → 8080），路径记错
        var anchors = new List<DriftAnchor>
        {
            new() { EntityKey = "API监听端口", CanonicalValue = "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000", Severity = 2, Domain = "port" },
            new() { EntityKey = "审计日志表", CanonicalValue = "audit_logs + chain_hash", Severity = 2, Domain = "data" }
        };
        var text = "API 监听端口是 ASPNETCORE_URLS 控制的 8080；审计日志表叫 audit_logs 带 chain_hash";

        var claims = new DriftClaimExtractor().Extract(text, anchors);
        var matches = claims
            .Select(c => new ClaimMatch
            {
                Anchor = c.Anchor,
                Score = DriftMatcher.MatchScore(text, c.Anchor.CanonicalValue),
                ActualTokens = c.MentionedTokens
            })
            .ToList();
        var result = new DriftMetricsService().Compute(matches);

        result.ClaimCount.Should().Be(2);
        result.MatchCount.Should().Be(1);          // 审计日志表一致，端口漂移
        // D = Σ(sev·err)/Σsev = (2×0.5 + 2×0) / 4 = 0.25
        result.DriftScore.Should().Be(0.25);
    }

    private static DriftAnchor Anchor(string key, string value, int severity, string domain = "port")
        => new() { EntityKey = key, CanonicalValue = value, Severity = severity, Domain = domain };
}
