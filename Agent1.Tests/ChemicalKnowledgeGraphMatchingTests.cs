using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 知识图谱匹配链单测（#31 安全距离特化优先、#36 法规编号精确/最长优先、#27/#30 禁忌词规范化）。
/// 不连数据库：通过反射注入内存列表，专注验证匹配算法本身。
/// </summary>
public class ChemicalKnowledgeGraphMatchingTests
{
    private static ChemicalKnowledgeGraph CreateEmptyGraph()
    {
        var config = new AppConfig
        {
            Database = new DatabaseConfig
            {
                Host = "localhost",
                Port = 5432,
                DatabaseName = "unused-test-db",
                Username = "postgres",
                Password = ""
            }
        };
        var graph = new ChemicalKnowledgeGraph(config, new ChemicalNamingInference());
        SetField(graph, "_initialized", true);
        return graph;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = typeof(ChemicalKnowledgeGraph).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"字段不存在: {fieldName}");
        field.SetValue(target, value);
    }

    [Fact]
    public void GetSafetyDistance_LongestSpecificRule_WinsOverGeneric()
    {
        var graph = CreateEmptyGraph();
        SetField(graph, "_safetyDistances", new List<SafetyDistanceRule>
        {
            new() { FacilityPair = "储罐-建筑", MinDistanceMeters = 25 },
            new() { FacilityPair = "液化烃储罐-建筑", MinDistanceMeters = 35 }
        });

        var result = graph.GetSafetyDistance("液化烃储罐-建筑");

        result.Should().NotBeNull();
        result!.MinDistanceMeters.Should().Be(35, "特化条目（更长）必须优先于泛化条目，避免安全距离少答 10m");
    }

    [Fact]
    public void GetRegulationVersion_SpecificNumber_NotShadowedByGeneral()
    {
        var graph = CreateEmptyGraph();
        SetField(graph, "_regulationVersions", new List<RegulationVersion>
        {
            new() { RegulationNumber = "GB 30000", CurrentVersion = "2013" },
            new() { RegulationNumber = "GB 30000.1", CurrentVersion = "2024" }
        });

        var result = graph.GetRegulationVersion("GB 30000.1");

        result.Should().NotBeNull();
        result!.CurrentVersion.Should().Be("2024", "GB 30000.1 必须命中 2024 修订，而不是被 GB 30000 总纲遮蔽");
    }

    [Fact]
    public void CheckCompatibility_SynonymAlias_ResolvesToForbiddenRule()
    {
        var graph = CreateEmptyGraph();
        var acid = new ChemicalSubstance
        {
            Name = "氢氟酸",
            IncompatibleWith = { "氨水" } // 种子同义词：氨水 → 氨
        };
        var ammonia = new ChemicalSubstance
        {
            Name = "氨",
            Aliases = { "液氨", "氨气" }
        };
        var nodes = new Dictionary<int, ChemicalSubstance>
        {
            [1] = acid,
            [2] = ammonia
        };
        SetField(graph, "_nodes", nodes);
        SetField(graph, "_nameIndex", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [acid.Name] = 1,
            [ammonia.Name] = 2
        });
        SetField(graph, "_aliasIndex", new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["液氨"] = new() { 2 },
            ["氨气"] = new() { 2 }
        });
        SetField(graph, "_incompatEdges", new Dictionary<(int, int), StorageIncompatibilityRule>());

        var result = graph.CheckCompatibility("氢氟酸", "氨");

        result.Should().NotBeNull();
        result!.IsCompatible.Should().BeFalse("氢氟酸禁忌词'氨水'应规范化后命中物质'氨'");
    }
}
