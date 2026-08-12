using System;
using Agent1.Services.DriftMonitor;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// DriftAnchorRegistry 纯逻辑单元测试（不依赖数据库）。
/// 覆盖：敏感值哈希稳定性、严重度规范化、模型默认值。
/// SQL 侧验证：本地执行 db/migrations/004_drift_anchor_baseline.sql 后
/// 通过 psql 查询 drift_anchors 表确认 50 条种子锚点（见迁移文件末尾自检 SQL）。
/// </summary>
public class DriftAnchorRegistryTests
{
    // ═══════════════════════════════════════
    // 敏感值哈希
    // ═══════════════════════════════════════

    [Fact]
    public void HashSensitiveValue_SameInput_ReturnsStableHash()
    {
        var h1 = DriftAnchorRegistry.HashSensitiveValue("JWT_KEY-placeholder");
        var h2 = DriftAnchorRegistry.HashSensitiveValue("JWT_KEY-placeholder");

        h1.Should().Be(h2);
    }

    [Fact]
    public void HashSensitiveValue_Returns64HexChars()
    {
        var hash = DriftAnchorRegistry.HashSensitiveValue("anything");

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void HashSensitiveValue_DifferentInput_ProducesDifferentHash()
    {
        var h1 = DriftAnchorRegistry.HashSensitiveValue("value-A");
        var h2 = DriftAnchorRegistry.HashSensitiveValue("value-B");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void HashSensitiveValue_EmptyString_StillProducesHash()
    {
        var hash = DriftAnchorRegistry.HashSensitiveValue("");

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ═══════════════════════════════════════
    // 严重度规范化
    // ═══════════════════════════════════════

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void ClampSeverity_ValidValues_Unchanged(int input, int expected)
    {
        DriftAnchorRegistry.ClampSeverity(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-100, 0)]
    [InlineData(3, 2)]
    [InlineData(99, 2)]
    public void ClampSeverity_OutOfRange_Converges(int input, int expected)
    {
        DriftAnchorRegistry.ClampSeverity(input).Should().Be(expected);
    }

    // ═══════════════════════════════════════
    // 模型默认值（锚点语义契约）
    // ═══════════════════════════════════════

    [Fact]
    public void DriftAnchor_Defaults_Severity1Version1()
    {
        var anchor = new DriftAnchor();

        anchor.Severity.Should().Be(1);
        anchor.Version.Should().Be(1);
        anchor.Domain.Should().BeEmpty();
    }
}
