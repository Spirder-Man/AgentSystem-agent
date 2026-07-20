using System;
using System.Reflection;
using System.Runtime.Serialization;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-1a: RAG.cs 纯逻辑测试 — 无需 LLM 依赖。
///
/// 覆盖 public static 方法:
///   - ExtractSubstanceStatic: 物质名称提取
///   - ExtractTwoSubstancesStatic: 双物质拆分
///   - ExtractFacilityTypeStatic: 设施类型推断
/// 以及 private 方法 (反射):
///   - ParseToolCalls: LLM 工具调用标记解析
///   - CleanSubstanceStatic: 物质名称清理
/// </summary>
public class RAGTests
{
    // ═══════════════════════════════════════
    // ExtractSubstanceStatic: 物质名称提取
    // ═══════════════════════════════════════

    [Fact]
    public void ExtractSubstance_SimpleName_ReturnsName()
    {
        var result = RAG.ExtractSubstanceStatic("苯");
        result.Should().Be("苯");
    }

    [Fact]
    public void ExtractSubstance_WithCategoryQuestion_StripsQuestionWords()
    {
        var result = RAG.ExtractSubstanceStatic("苯属于什么危险类别");
        result.Should().Be("苯");
    }

    [Fact]
    public void ExtractSubstance_WithChemicalSuffix_StripsSuffix()
    {
        var result = RAG.ExtractSubstanceStatic("苯的危化品");
        result.Should().Be("苯");
    }

    [Fact]
    public void ExtractSubstance_WithWhatIs_StripsAllQuestionWords()
    {
        // "是什么类别" → 去掉"是什么" → "类别" → 再去掉"什么" → ""  
        var result = RAG.ExtractSubstanceStatic("硫酸是什么类别");
        result.Should().Be("硫酸");
    }

    [Fact]
    public void ExtractSubstance_WithQuestionMark_StripsMark()
    {
        var result = RAG.ExtractSubstanceStatic("丙酮？");
        result.Should().Be("丙酮");
    }

    [Fact]
    public void ExtractSubstance_WithMultipleSuffixes_StripsAll()
    {
        var result = RAG.ExtractSubstanceStatic("甲苯属于什么危险类别的危化品吗？");
        result.Should().Be("甲苯");
    }

    // ═══════════════════════════════════════
    // ExtractTwoSubstancesStatic: 双物质拆分
    // ═══════════════════════════════════════

    [Fact]
    public void ExtractTwoSubstances_ByHe_ReturnsTwo()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("苯和硝酸能一起存吗");
        a.Should().Be("苯");
        b.Should().Be("硝酸");
    }

    [Fact]
    public void ExtractTwoSubstances_ByYu_ReturnsTwo()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("爆炸物与易燃气体");
        a.Should().Be("爆炸物");
        b.Should().Be("易燃气体");
    }

    [Fact]
    public void ExtractTwoSubstances_ByDunhao_ReturnsTwo()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("硫酸、氢氧化钠");
        a.Should().Be("硫酸");
        b.Should().Be("氢氧化钠");
    }

    [Fact]
    public void ExtractTwoSubstances_NoSeparator_FallbackToSingle()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("苯");
        a.Should().Be("苯");
        b.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTwoSubstances_ThreeSubstances_Split2PreservesRest()
    {
        // Split with count=2 只切一次，剩余内容保留在第二部分
        var (a, b) = RAG.ExtractTwoSubstancesStatic("苯和硝酸和硫酸");
        a.Should().Be("苯");
        b.Should().Contain("硝酸");
        b.Should().Contain("硫酸");
    }

    [Fact]
    public void ExtractTwoSubstances_CleansResidualWords()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("苯能一起存和硝酸可以一起吗？");
        a.Should().Be("苯");
        b.Should().Be("硝酸");
    }

    // ═══════════════════════════════════════
    // ExtractFacilityTypeStatic: 设施类型推断
    // ═══════════════════════════════════════

    [Fact]
    public void ExtractFacility_LpgTank_ReturnsLpgTankPair()
    {
        var result = RAG.ExtractFacilityTypeStatic("液化烃储罐间距");
        result.Should().Be("液化烃储罐-储罐");
    }

    [Fact]
    public void ExtractFacility_WarehouseVsOpenFlame()
    {
        var result = RAG.ExtractFacilityTypeStatic("甲类仓库与明火点的间距");
        result.Should().Be("甲类仓库-明火点");
    }

    [Fact]
    public void ExtractFacility_WarehouseVsBuilding()
    {
        var result = RAG.ExtractFacilityTypeStatic("仓库与建筑间距要求");
        result.Should().Be("甲类仓库-建筑");
    }

    [Fact]
    public void ExtractFacility_TankVsBuilding()
    {
        var result = RAG.ExtractFacilityTypeStatic("储罐到建筑的安全距离");
        result.Should().Be("储罐-建筑");
    }

    [Fact]
    public void ExtractFacility_TankVsFireLane()
    {
        var result = RAG.ExtractFacilityTypeStatic("储罐与消防通道");
        result.Should().Be("储罐-消防通道");
    }

    [Fact]
    public void ExtractFacility_TankVsBoundary()
    {
        var result = RAG.ExtractFacilityTypeStatic("储罐距厂区边界");
        result.Should().Be("储罐-厂区边界");
    }

    [Fact]
    public void ExtractFacility_TankOnly_DefaultTankPair()
    {
        var result = RAG.ExtractFacilityTypeStatic("储罐间距");
        result.Should().Be("储罐-储罐");
    }

    [Fact]
    public void ExtractFacility_NoMatch_ReturnsInput()
    {
        var result = RAG.ExtractFacilityTypeStatic("未知设施类型");
        result.Should().Be("未知设施类型");
    }

    // ═══════════════════════════════════════
    // ParseToolCalls: LLM 工具调用标记解析 (反射)
    // ═══════════════════════════════════════

    [Fact]
    public void ParseToolCalls_SingleTool_ReturnsIt()
    {
        var output = "需要调用的工具：CheckHazardCategory";
        var tools = CallParseToolCalls(output);
        tools.Should().ContainSingle().Which.Should().Be("CheckHazardCategory");
    }

    [Fact]
    public void ParseToolCalls_MultipleTools_ReturnsAll()
    {
        var output = "需要调用的工具：CheckHazardCategory,CheckStorageCompatibility";
        var tools = CallParseToolCalls(output);
        tools.Should().Contain("CheckHazardCategory");
        tools.Should().Contain("CheckStorageCompatibility");
        tools.Should().HaveCount(2);
    }

    [Fact]
    public void ParseToolCalls_NoMarker_ReturnsEmpty()
    {
        var output = "这是普通的回答，没有任何工具调用标记";
        var tools = CallParseToolCalls(output);
        tools.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolCalls_MarkerWithNone_ReturnsEmpty()
    {
        var output = "需要调用的工具：无";
        var tools = CallParseToolCalls(output);
        tools.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolCalls_MarkerWithNoneAndPeriod_ReturnsEmpty()
    {
        var output = "需要调用的工具：无。";
        var tools = CallParseToolCalls(output);
        tools.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolCalls_NullOrEmpty_ReturnsEmpty()
    {
        CallParseToolCalls(null!).Should().BeEmpty();
        CallParseToolCalls("").Should().BeEmpty();
    }

    [Fact]
    public void ParseToolCalls_MarkerWithGetCurrentTime()
    {
        var output = "需要调用的工具：GetCurrentTime";
        var tools = CallParseToolCalls(output);
        tools.Should().ContainSingle().Which.Should().Be("GetCurrentTime");
    }

    [Fact]
    public void ParseToolCalls_MarkerWithCalculate()
    {
        var output = "需要调用的工具：Calculate";
        var tools = CallParseToolCalls(output);
        tools.Should().ContainSingle().Which.Should().Be("Calculate");
    }

    [Fact]
    public void ParseToolCalls_OnlyMarkerLineScanned()
    {
        // 确保只有标记行中的工具名被匹配，思考过程中提到的工具名不被误匹配
        var output = "我需要考虑是否使用 CheckHazardCategory 和 GetSafetyDistance\n" +
                     "但最终决定不需要调用工具\n" +
                     "需要调用的工具：无";
        var tools = CallParseToolCalls(output);
        tools.Should().BeEmpty("只有标记行'无'才决定不调用工具");
    }

    [Fact]
    public void ParseToolCalls_MarkerLineWithTrailingNewlines_Works()
    {
        var output = "需要调用的工具：CheckStorageCompatibility\n\n其他思考内容";
        var tools = CallParseToolCalls(output);
        tools.Should().ContainSingle().Which.Should().Be("CheckStorageCompatibility");
    }

    // ═══════════════════════════════════════
    // CleanSubstanceStatic: 物质名称清理 (反射)
    // ═══════════════════════════════════════

    [Fact]
    public void CleanSubstance_RemovesStorageWords()
    {
        var result = CallCleanSubstanceStatic("苯能一起存");
        result.Should().Be("苯");
    }

    [Fact]
    public void CleanSubstance_RemovesCanTogether()
    {
        var result = CallCleanSubstanceStatic("爆炸物可以一起");
        result.Should().Be("爆炸物");
    }

    [Fact]
    public void CleanSubstance_RemovesQuestionWords()
    {
        var result = CallCleanSubstanceStatic("硝酸可以吗？");
        result.Should().Be("硝酸");
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static string[] CallParseToolCalls(string modelOutput)
    {
        var method = typeof(RAG).GetMethod("ParseToolCalls",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        // ParseToolCalls 不依赖任何实例字段，使用未初始化对象跳过构造函数
        var instance = FormatterServices.GetUninitializedObject(typeof(RAG));
        return (string[])method.Invoke(instance, new object[] { modelOutput })!;
    }

    private static string CallCleanSubstanceStatic(string text)
    {
        var method = typeof(RAG).GetMethod("CleanSubstanceStatic",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { text })!;
    }
}
