using System;
using System.Collections.Generic;
using System.Reflection;
using Agent1.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-2c: KnowledgeGraphService 纯逻辑测试。
///
/// 覆盖:
///   - ExportDOT: 空图/有实体 DOT 格式
///   - Traverse: 未知名称 → 空结果
///   - EntityCount/RelationCount: 初始状态
///   - 图实体/关系模型
/// </summary>
public class ChemicalKnowledgeGraphTests
{
    private static KnowledgeGraphService CreateEmptyGraph()
    {
        var mockKb = Mock.Of<IKnowledgeBaseService>();
        return new KnowledgeGraphService(mockKb);
    }

    private static void AddEntityViaReflection(KnowledgeGraphService graph, GraphEntity entity)
    {
        var method = typeof(KnowledgeGraphService).GetMethod("AddEntity",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(graph, new object[] { entity });
    }

    private static void AddRelationViaReflection(KnowledgeGraphService graph, GraphRelation relation)
    {
        var method = typeof(KnowledgeGraphService).GetMethod("AddRelation",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(graph, new object[] { relation });
    }

    // ═══════════════════════════════════════
    // 初始状态
    // ═══════════════════════════════════════

    [Fact]
    public void EmptyGraph_EntityCount_IsZero()
    {
        var graph = CreateEmptyGraph();
        graph.EntityCount.Should().Be(0);
    }

    [Fact]
    public void EmptyGraph_RelationCount_IsZero()
    {
        var graph = CreateEmptyGraph();
        graph.RelationCount.Should().Be(0);
    }

    // ═══════════════════════════════════════
    // ExportDOT
    // ═══════════════════════════════════════

    [Fact]
    public void ExportDOT_EmptyGraph_ReturnsValidDOT()
    {
        var graph = CreateEmptyGraph();
        var dot = graph.ExportDOT();

        dot.Should().Contain("digraph ChemicalSafetyKG");
        dot.Should().Contain("rankdir=LR");
        dot.Should().Contain("}");
    }

    [Fact]
    public void ExportDOT_WithEntities_IncludesNodeDefinitions()
    {
        var graph = CreateEmptyGraph();
        AddEntityViaReflection(graph, new GraphEntity("chem:苯", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "苯" }));

        var dot = graph.ExportDOT();

        dot.Should().Contain("\"chem:苯\"");
        dot.Should().Contain("fillcolor=lightyellow");
    }

    [Fact]
    public void ExportDOT_WithRelations_IncludesEdges()
    {
        var graph = CreateEmptyGraph();
        AddEntityViaReflection(graph, new GraphEntity("chem:苯", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "苯" }));
        AddEntityViaReflection(graph, new GraphEntity("cat:易燃液体", EntityType.HazardCategory,
            new Dictionary<string, string> { ["name"] = "易燃液体" }));
        AddRelationViaReflection(graph, new GraphRelation("chem:苯", "cat:易燃液体",
            RelationType.ClassifiedAs));

        var dot = graph.ExportDOT();

        dot.Should().Contain("\"chem:苯\" -> \"cat:易燃液体\"");
        dot.Should().Contain("ClassifiedAs");
    }

    [Fact]
    public void ExportDOT_IncompatibleWith_UsesRedColor()
    {
        var graph = CreateEmptyGraph();
        AddEntityViaReflection(graph, new GraphEntity("chem:A", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "A" }));
        AddEntityViaReflection(graph, new GraphEntity("chem:B", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "B" }));
        AddRelationViaReflection(graph, new GraphRelation("chem:A", "chem:B",
            RelationType.IncompatibleWith));

        var dot = graph.ExportDOT();

        dot.Should().Contain("color=red");
        dot.Should().Contain("style=bold");
    }

    // ═══════════════════════════════════════
    // Traverse
    // ═══════════════════════════════════════

    [Fact]
    public void Traverse_UnknownName_ReturnsEmptyList()
    {
        var graph = CreateEmptyGraph();
        var result = graph.Traverse("不存在化学品");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Traverse_KnownEntity_ReturnsEntity()
    {
        var graph = CreateEmptyGraph();
        AddEntityViaReflection(graph, new GraphEntity("chem:苯", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "苯" }));

        var result = graph.Traverse("苯");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("chem:苯");
    }

    // ═══════════════════════════════════════
    // 图模型
    // ═══════════════════════════════════════

    [Fact]
    public void GraphEntity_Label_ReturnsNameProperty()
    {
        var entity = new GraphEntity("chem:甲醇", EntityType.Chemical,
            new Dictionary<string, string> { ["name"] = "甲醇", ["cas"] = "67-56-1" });

        entity.Label.Should().Be("甲醇");
    }

    [Fact]
    public void GraphEntity_NoNameProperty_ReturnsId()
    {
        var entity = new GraphEntity("chem:unknown", EntityType.Chemical,
            new Dictionary<string, string>());

        entity.Label.Should().Be("chem:unknown");
    }

    [Fact]
    public void GraphRelation_HasAllProperties()
    {
        var relation = new GraphRelation("A", "B", RelationType.References, "GB15603");

        relation.FromId.Should().Be("A");
        relation.ToId.Should().Be("B");
        relation.Type.Should().Be(RelationType.References);
        relation.Detail.Should().Be("GB15603");
    }
}
