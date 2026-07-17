using System.Linq;
using Agent1.Models;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P1-3: QualityLevel 枚举数值重排验证。
/// 确认 DATABASE_HIT(4) > RAG_HIT(3)，
/// 确保按数值比较取源时数据库优先级高于 RAG。
/// </summary>
public class QualityLevelEnumTests
{
    // ═══════════════════════════════════════
    // 数值顺序
    // ═══════════════════════════════════════

    [Fact]
    public void DatabaseHit_ShouldBeHigherThan_RagHit()
    {
        ((int)QualityLevel.DATABASE_HIT).Should().BeGreaterThan((int)QualityLevel.RAG_HIT);
    }

    [Fact]
    public void RagHit_ShouldBeHigherThan_DictionaryHit()
    {
        ((int)QualityLevel.RAG_HIT).Should().BeGreaterThan((int)QualityLevel.DICTIONARY_HIT);
    }

    [Fact]
    public void DictionaryHit_ShouldBeHigherThan_Fallback()
    {
        ((int)QualityLevel.DICTIONARY_HIT).Should().BeGreaterThan((int)QualityLevel.FALLBACK);
    }

    [Fact]
    public void Fallback_ShouldBeHigherThan_Error()
    {
        ((int)QualityLevel.FALLBACK).Should().BeGreaterThan((int)QualityLevel.ERROR);
    }

    [Fact]
    public void EnumValues_ShouldBeInDescendingOrder()
    {
        var values = new[]
        {
            (int)QualityLevel.DATABASE_HIT,
            (int)QualityLevel.RAG_HIT,
            (int)QualityLevel.DICTIONARY_HIT,
            (int)QualityLevel.FALLBACK,
            (int)QualityLevel.ERROR
        };

        // 应该严格递减
        for (int i = 1; i < values.Length; i++)
            values[i - 1].Should().BeGreaterThan(values[i]);
    }

    // ═══════════════════════════════════════
    // 语义一致性：选出最高优先级来源
    // ═══════════════════════════════════════

    [Fact]
    public void WhenDbAndRagBothPresent_ShouldSelectDb()
    {
        // 模拟两个来源：DB 命中 + RAG 命中，应该选数值更大的 DB
        var sources = new[]
        {
            (Quality: QualityLevel.RAG_HIT, Source: "RAG"),
            (Quality: QualityLevel.DATABASE_HIT, Source: "DB"),
        };

        var best = sources.OrderByDescending(s => (int)s.Quality).First();

        best.Source.Should().Be("DB");
        best.Quality.Should().Be(QualityLevel.DATABASE_HIT);
    }

    [Fact]
    public void WhenRagAndDictionaryBothPresent_ShouldSelectRag()
    {
        var sources = new[]
        {
            (Quality: QualityLevel.DICTIONARY_HIT, Source: "Dictionary"),
            (Quality: QualityLevel.RAG_HIT, Source: "RAG"),
        };

        var best = sources.OrderByDescending(s => (int)s.Quality).First();

        best.Source.Should().Be("RAG");
    }

    // ═══════════════════════════════════════
    // 关键枚举值确认（防止意外重排）
    // ═══════════════════════════════════════

    [Fact]
    public void DatabaseHit_ValueShouldBe4()
    {
        ((int)QualityLevel.DATABASE_HIT).Should().Be(4);
    }

    [Fact]
    public void RagHit_ValueShouldBe3()
    {
        ((int)QualityLevel.RAG_HIT).Should().Be(3);
    }

    [Fact]
    public void DictionaryHit_ValueShouldBe2()
    {
        ((int)QualityLevel.DICTIONARY_HIT).Should().Be(2);
    }

    [Fact]
    public void Fallback_ValueShouldBe0()
    {
        ((int)QualityLevel.FALLBACK).Should().Be(0);
    }

    [Fact]
    public void Error_ValueShouldBeMinus1()
    {
        ((int)QualityLevel.ERROR).Should().Be(-1);
    }
}
