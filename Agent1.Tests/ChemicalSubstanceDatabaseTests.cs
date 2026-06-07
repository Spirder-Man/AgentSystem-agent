using System.Linq;
using Agent1.Models;
using Agent1.Services;
using Xunit;
using FluentAssertions;

/// <summary>
/// Task 10: 化学品属性数据库 + 知识库增强测试
/// </summary>
public class ChemicalSubstanceDatabaseTests
{
    // ════════════════════════════════════════
    // 基础查询测试
    // ════════════════════════════════════════

    [Fact]
    public void Lookup_StandardName_ReturnsCorrectData()
    {
        var sub = ChemicalSubstanceDatabase.Lookup("苯");
        sub.Should().NotBeNull();
        sub!.Name.Should().Be("苯");
        sub.CasNumber.Should().Be("71-43-2");
        sub.UnNumber.Should().Be("1114");
        sub.Formula.Should().Be("C6H6");
        sub.FlashPointC.Should().Be(-11);
        sub.BoilingPointC.Should().Be(80.1);
        sub.MajorHazardThresholdTons.Should().Be(50);
        sub.HazardCategories.Should().NotBeEmpty();
        sub.HazardCategories.Should().Contain(h => h.Category == "易燃液体");
        sub.HazardCategories.Should().Contain(h => h.Category == "致癌性");
    }

    [Fact]
    public void Lookup_AliasName_ResolvesToStandardName()
    {
        var sub = ChemicalSubstanceDatabase.Lookup("双氧水");
        sub.Should().NotBeNull();
        sub!.Name.Should().Be("过氧化氢");
        sub.CasNumber.Should().Be("7722-84-1");
    }

    [Fact]
    public void Lookup_MultipleAliases_AllResolveCorrectly()
    {
        ChemicalSubstanceDatabase.Lookup("烧碱")!.Name.Should().Be("氢氧化钠");
        ChemicalSubstanceDatabase.Lookup("火碱")!.Name.Should().Be("氢氧化钠");
        ChemicalSubstanceDatabase.Lookup("苛性钠")!.Name.Should().Be("氢氧化钠");
        ChemicalSubstanceDatabase.Lookup("酒精")!.Name.Should().Be("乙醇");
        ChemicalSubstanceDatabase.Lookup("液氯")!.Name.Should().Be("氯");
        ChemicalSubstanceDatabase.Lookup("氨水")!.Name.Should().Be("氨溶液");
        ChemicalSubstanceDatabase.Lookup("醋酸")!.Name.Should().Be("乙酸");
        ChemicalSubstanceDatabase.Lookup("福尔马林")!.Name.Should().Be("甲醛");
        ChemicalSubstanceDatabase.Lookup("甘油")!.Name.Should().Be("丙三醇");
    }

    [Fact]
    public void Lookup_UnknownSubstance_ReturnsNull()
    {
        var sub = ChemicalSubstanceDatabase.Lookup("不存在的化学品XYZ");
        sub.Should().BeNull();
    }

    [Fact]
    public void Lookup_CaseInsensitive()
    {
        ChemicalSubstanceDatabase.Lookup("BENZENE").Should().BeNull(); // 英文不直接映射
        var sub = ChemicalSubstanceDatabase.Lookup("苯");
        sub.Should().NotBeNull();
    }

    // ════════════════════════════════════════
    // CAS 号查询
    // ════════════════════════════════════════

    [Fact]
    public void LookupByCas_KnownCas_ReturnsCorrectSubstance()
    {
        var sub = ChemicalSubstanceDatabase.LookupByCas("67-56-1");
        sub.Should().NotBeNull();
        sub!.Name.Should().Be("甲醇");
    }

    [Fact]
    public void LookupByCas_UnknownCas_ReturnsNull()
    {
        var sub = ChemicalSubstanceDatabase.LookupByCas("999-99-9");
        sub.Should().BeNull();
    }

    // ════════════════════════════════════════
    // 模糊搜索
    // ════════════════════════════════════════

    [Fact]
    public void Search_PartialName_ReturnsMatches()
    {
        var results = ChemicalSubstanceDatabase.Search("酸");
        results.Should().NotBeEmpty();
        results.Count.Should().BeLessOrEqualTo(5);
        results.Should().Contain(s => s.Name.Contains("硫酸") || s.Name.Contains("硝酸"));
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var results = ChemicalSubstanceDatabase.Search("XYZ不存在的物质");
        results.Should().BeEmpty();
    }

    // ════════════════════════════════════════
    // 储存兼容性测试
    // ════════════════════════════════════════

    [Fact]
    public void CheckCompatibility_Incompatible_ReturnsFalse()
    {
        // 硝酸(氧化剂) + 甲醇(易燃液体) = 不兼容
        var result = ChemicalSubstanceDatabase.CheckCompatibility("硝酸", "甲醇");
        result.Should().NotBeNull();
        result!.IsCompatible.Should().BeFalse();
        result.Reason.Should().Contain("严禁同库");
    }

    [Fact]
    public void CheckCompatibility_Compatible_ReturnsTrue()
    {
        // 甲苯 + 二甲苯 = 同类易燃液体可同库
        var result = ChemicalSubstanceDatabase.CheckCompatibility("甲苯", "二甲苯");
        result.Should().NotBeNull();
        result!.IsCompatible.Should().BeTrue();
    }

    [Fact]
    public void CheckCompatibility_AliasResolved_Works()
    {
        // 双氧水(过氧化氢) + 丙酮 = 不兼容
        var result = ChemicalSubstanceDatabase.CheckCompatibility("双氧水", "丙酮");
        result.Should().NotBeNull();
        result!.IsCompatible.Should().BeFalse();
    }

    [Fact]
    public void CheckCompatibility_AcidBase_ReturnsFalse()
    {
        // 氢氧化钠 + 盐酸 = 酸碱不兼容
        var result = ChemicalSubstanceDatabase.CheckCompatibility("氢氧化钠", "盐酸");
        result.Should().NotBeNull();
        result!.IsCompatible.Should().BeFalse();
        result.Reason.Should().Contain("中和");
    }

    // ════════════════════════════════════════
    // 安全距离测试
    // ════════════════════════════════════════

    [Fact]
    public void GetSafetyDistance_KnownPair_ReturnsCorrectDistance()
    {
        var rule = ChemicalSubstanceDatabase.GetSafetyDistance("甲类仓库-明火点");
        rule.Should().NotBeNull();
        rule!.MinDistanceMeters.Should().Be(30);
        rule.RegulationRef.Should().Contain("GB 50160");
    }

    [Fact]
    public void GetSafetyDistance_PartialMatch_Works()
    {
        var rule = ChemicalSubstanceDatabase.GetSafetyDistance("液化烃储罐");
        rule.Should().NotBeNull();
        rule!.MinDistanceMeters.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetSafetyDistance_AllEntries_HavePositiveDistance()
    {
        var all = ChemicalSubstanceDatabase.GetAllSafetyDistances();
        all.Should().NotBeEmpty();
        all.Count.Should().BeGreaterOrEqualTo(15);
        foreach (var rule in all)
        {
            rule.MinDistanceMeters.Should().BeGreaterThan(0);
            rule.FacilityPair.Should().NotBeNullOrWhiteSpace();
        }
    }

    // ════════════════════════════════════════
    // 法规版本追踪测试
    // ════════════════════════════════════════

    [Fact]
    public void GetRegulationVersion_GB15603_ReturnsCorrectVersion()
    {
        var version = ChemicalSubstanceDatabase.GetRegulationVersion("GB 15603");
        version.Should().NotBeNull();
        version!.CurrentVersion.Should().Be("2022");
        version.DeprecatedVersions.Should().Contain("1995");
        version.HasFullText.Should().BeTrue();
    }

    [Fact]
    public void GetRegulationVersion_GB18218_ReturnsData()
    {
        var version = ChemicalSubstanceDatabase.GetRegulationVersion("GB 18218");
        version.Should().NotBeNull();
        version!.CurrentVersion.Should().Be("2018");
        version.HasFullText.Should().BeFalse();
        version.Title.Should().Contain("重大危险源");
    }

    [Fact]
    public void GetRegulationVersion_UnknownRegulation_ReturnsNull()
    {
        var version = ChemicalSubstanceDatabase.GetRegulationVersion("GB 99999");
        version.Should().BeNull();
    }

    [Fact]
    public void GetAllRegulationVersions_ContainsRequiredStandards()
    {
        var all = ChemicalSubstanceDatabase.GetAllRegulationVersions();
        all.Should().NotBeEmpty();
        var nums = all.Select(v => v.RegulationNumber).ToList();
        nums.Should().Contain("GB 15603");
        nums.Should().Contain("GB 18218");
        nums.Should().Contain("GB 50160");
        nums.Should().Contain("GB 30871");
    }

    // ════════════════════════════════════════
    // 数据质量测试
    // ════════════════════════════════════════

    [Fact]
    public void GetAll_Count_IsAtLeast30()
    {
        var count = ChemicalSubstanceDatabase.Count;
        count.Should().BeGreaterOrEqualTo(30);
    }

    [Fact]
    public void GetAll_EachSubstance_HasRequiredFields()
    {
        var all = ChemicalSubstanceDatabase.GetAll();
        foreach (var sub in all)
        {
            sub.Name.Should().NotBeNullOrWhiteSpace($"Substance {sub.CasNumber} has no name");
            sub.CasNumber.Should().NotBeNullOrWhiteSpace($"Substance {sub.Name} has no CAS number");
            sub.Formula.Should().NotBeNullOrWhiteSpace($"Substance {sub.Name} has no formula");
            sub.PhysicalState.Should().NotBeNullOrWhiteSpace($"Substance {sub.Name} has no physical state");
        }
    }

    [Fact]
    public void DangerousSubstances_HaveHazardCategories()
    {
        var dangerous = Enumerable.Where(ChemicalSubstanceDatabase.GetAll(),
            s => s.MajorHazardThresholdTons > 0 || (s.FlashPointC.HasValue && s.FlashPointC < 60))
            .ToList();
        dangerous.Should().NotBeEmpty();
        foreach (var sub in dangerous)
        {
            sub.HazardCategories.Should().NotBeEmpty(
                $"Dangerous substance {sub.Name} (CAS: {sub.CasNumber}) should have hazard categories");
        }
    }

    [Fact]
    public void MajorHazardThresholds_ArePositiveOrZero()
    {
        var all = ChemicalSubstanceDatabase.GetAll();
        foreach (var sub in all)
        {
            sub.MajorHazardThresholdTons.Should().BeGreaterOrEqualTo(0,
                $"Substance {sub.Name} has negative major hazard threshold");
        }
    }

    // ════════════════════════════════════════
    // 评测集覆盖测试
    // ════════════════════════════════════════

    [Theory]
    [InlineData("过氧化氢")]
    [InlineData("甲醇")]
    [InlineData("氯")]
    [InlineData("硝酸铵")]
    [InlineData("氢氧化钠")]
    [InlineData("苯")]
    [InlineData("硫化氢")]
    [InlineData("丙酮")]
    [InlineData("高锰酸钾")]
    [InlineData("环氧乙烷")]
    [InlineData("甲苯")]
    [InlineData("氨溶液")]
    [InlineData("乙炔")]
    [InlineData("硫酸")]
    [InlineData("氢氟酸")]
    [InlineData("硝酸")]
    [InlineData("乙酸")]
    [InlineData("盐酸")]
    [InlineData("氨")]
    [InlineData("氯化氢")]
    [InlineData("氧气")]
    [InlineData("氰化钠")]
    [InlineData("铝粉")]
    [InlineData("硫磺")]
    [InlineData("苯乙烯")]
    [InlineData("二甲苯")]
    [InlineData("乙醇")]
    [InlineData("三氯甲烷")]
    [InlineData("二氧化硫")]
    [InlineData("丙三醇")]
    [InlineData("甲醛")]
    public void EvalSetChemicals_AreInDatabase(string chemicalName)
    {
        var sub = ChemicalSubstanceDatabase.Lookup(chemicalName);
        sub.Should().NotBeNull(
            $"评测集化学品「{chemicalName}」应在数据库中");
    }
}
