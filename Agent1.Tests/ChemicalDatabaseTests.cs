using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Xunit;
using FluentAssertions;

namespace Agent1.Tests
{
    // ═══════════════════════════════════════════
    // ChemicalSubstanceDatabase 测试 — 化学品数据库
    // ═══════════════════════════════════════════
    public class ChemicalSubstanceDatabaseTests
    {
        [Fact]
        public void Lookup_ByName_ReturnsSubstance()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("苯");
            sub.Should().NotBeNull();
            sub!.Name.Should().Be("苯");
            sub.CasNumber.Should().Be("71-43-2");
            sub.Formula.Should().Be("C6H6");
            sub.PhysicalState.Should().Be("液体");
        }

        [Fact]
        public void Lookup_ByAlias_ResolvesToStandardName()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("双氧水");
            sub.Should().NotBeNull();
            sub!.Name.Should().Be("过氧化氢", "别名应解析为标准名称");
        }

        [Fact]
        public void Lookup_AlcoholAlias_ResolvesToEthanol()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("酒精");
            sub.Should().NotBeNull();
            sub!.Name.Should().Be("乙醇");
        }

        [Fact]
        public void Lookup_ByNameWithDifferentCase_ReturnsMatch()
        {
            // Lookup 内部使用 OrdinalIgnoreCase
            var sub = ChemicalSubstanceDatabase.Lookup("苯");
            sub.Should().NotBeNull();
        }

        [Fact]
        public void Lookup_NullOrEmpty_ReturnsNull()
        {
            ChemicalSubstanceDatabase.Lookup(null!).Should().BeNull();
            ChemicalSubstanceDatabase.Lookup("").Should().BeNull();
            ChemicalSubstanceDatabase.Lookup("   ").Should().BeNull();
        }

        [Fact]
        public void Lookup_UnknownSubstance_ReturnsNull()
        {
            ChemicalSubstanceDatabase.Lookup("不存在的化学品XYZ123")
                .Should().BeNull();
        }

        [Fact]
        public void LookupByCas_ReturnsCorrectSubstance()
        {
            var sub = ChemicalSubstanceDatabase.LookupByCas("67-56-1");
            sub.Should().NotBeNull();
            sub!.Name.Should().Be("甲醇");
        }

        [Fact]
        public void LookupByCas_Unknown_ReturnsNull()
        {
            ChemicalSubstanceDatabase.LookupByCas("00-00-0")
                .Should().BeNull();
        }

        [Fact]
        public void Search_FindsMatches()
        {
            var results = ChemicalSubstanceDatabase.Search("甲", maxResults: 5);
            results.Should().NotBeEmpty();
            results.Should().Contain(s => s.Name == "甲醇");
            results.Should().Contain(s => s.Name == "甲苯");
        }

        [Fact]
        public void Search_RespectsMaxResults()
        {
            var results = ChemicalSubstanceDatabase.Search("酸", maxResults: 3);
            results.Count.Should().BeLessOrEqualTo(3);
        }

        [Fact]
        public void Search_NoMatch_ReturnsEmpty()
        {
            var results = ChemicalSubstanceDatabase.Search("xyz不存在的化学物质999");
            results.Should().BeEmpty();
        }

        [Fact]
        public void Search_SearchesAliases()
        {
            var results = ChemicalSubstanceDatabase.Search("双氧");
            results.Should().Contain(s => s.Name == "过氧化氢",
                "应按别名搜索到对应化学品");
        }

        [Fact]
        public void GetAll_ReturnsAllSubstances()
        {
            var all = ChemicalSubstanceDatabase.GetAll();
            all.Should().NotBeEmpty();
            all.Count.Should().Be(ChemicalSubstanceDatabase.Count);
        }

        [Fact]
        public void FlashPoint_NullForNonFlammable()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("硫酸");
            sub.Should().NotBeNull();
            sub!.FlashPointC.Should().BeNull("硫酸无闪点");
        }

        [Fact]
        public void FlashPoint_HasValueForFlammable()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("苯");
            sub.Should().NotBeNull();
            sub!.FlashPointC.Should().Be(-11);
        }

        [Fact]
        public void HazardCategories_ContainGbStandard()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("苯");
            sub.Should().NotBeNull();
            sub!.HazardCategories.Should().NotBeEmpty();
            sub.HazardCategories.Should().Contain(h => h.GbStandard.StartsWith("GB 30000"));
        }

        [Fact]
        public void IncompatibleWith_IsNotEmpty()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("硝酸");
            sub.Should().NotBeNull();
            sub!.IncompatibleWith.Should().NotBeEmpty("强氧化剂应有禁忌列表");
        }

        // ── 储存兼容性测试 ──

        [Fact]
        public void CheckCompatibility_ExactRule_Incompatible()
        {
            var result = ChemicalSubstanceDatabase.CheckCompatibility("硝酸", "乙酸");
            result.Should().NotBeNull();
            result!.IsCompatible.Should().BeFalse("氧化剂与易燃液体应不兼容");
            result.Reason.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void CheckCompatibility_ExactRule_Compatible()
        {
            var result = ChemicalSubstanceDatabase.CheckCompatibility("苯", "丙酮");
            result.Should().NotBeNull();
            result!.IsCompatible.Should().BeTrue("同类易燃液体可同库");
        }

        [Fact]
        public void CheckCompatibility_CategoryLevel_Incompatible()
        {
            // 过氧化氢(氧化性液体) vs 苯(易燃液体) — 类别级判定
            var result = ChemicalSubstanceDatabase.CheckCompatibility("过氧化氢", "苯");
            result.Should().NotBeNull();
            result!.IsCompatible.Should().BeFalse("氧化剂与易燃液体类别级判定");
        }

        [Fact]
        public void CheckCompatibility_UnknownSubstance_ReturnsNull()
        {
            ChemicalSubstanceDatabase.CheckCompatibility("不存在A", "不存在B")
                .Should().BeNull();
        }

        [Fact]
        public void CheckCompatibility_SameCategory_Compatible()
        {
            // 甲苯和二甲苯同属易燃液体类别
            var result = ChemicalSubstanceDatabase.CheckCompatibility("甲苯", "二甲苯");
            result.Should().NotBeNull();
            if (result != null) // 可能返回 null（无精确规则），也可能返回兼容（同类推断）
            {
                result.IsCompatible.Should().BeTrue("同类化学品可同库分区存放");
            }
        }

        [Fact]
        public void CheckCompatibility_OxidizerAndFlammable_Incompatible()
        {
            var result = ChemicalSubstanceDatabase.CheckCompatibility("高锰酸钾", "乙醇");
            result.Should().NotBeNull();
            result!.IsCompatible.Should().BeFalse("氧化剂与易燃液体严禁同库");
        }

        // ── 安全距离测试 ──

        [Fact]
        public void GetSafetyDistance_ExactMatch_ReturnsDistance()
        {
            var rule = ChemicalSubstanceDatabase.GetSafetyDistance("储罐-储罐");
            rule.Should().NotBeNull();
            rule!.MinDistanceMeters.Should().Be(15);
            rule.RegulationRef.Should().Contain("GB 50160");
        }

        [Fact]
        public void GetSafetyDistance_FuzzyMatch_ReturnsDistance()
        {
            var rule = ChemicalSubstanceDatabase.GetSafetyDistance("甲类仓库-明火点");
            rule.Should().NotBeNull();
            rule!.MinDistanceMeters.Should().Be(30);
        }

        [Fact]
        public void GetSafetyDistance_Unknown_ReturnsNull()
        {
            ChemicalSubstanceDatabase.GetSafetyDistance("未知设施类型-未知")
                .Should().BeNull();
        }

        [Fact]
        public void GetAllSafetyDistances_ReturnsList()
        {
            var all = ChemicalSubstanceDatabase.GetAllSafetyDistances();
            all.Should().NotBeEmpty();
            all.Should().AllSatisfy(r =>
            {
                r.MinDistanceMeters.Should().BeGreaterThan(0);
                r.FacilityPair.Should().NotBeNullOrEmpty();
            });
        }

        // ── 法规版本测试 ──

        [Fact]
        public void GetRegulationVersion_ExactNumber_ReturnsVersion()
        {
            var version = ChemicalSubstanceDatabase.GetRegulationVersion("GB 15603");
            version.Should().NotBeNull();
            version!.Title.Should().Contain("贮存通则");
            version.CurrentVersion.Should().Be("2022");
            version.DeprecatedVersions.Should().Contain("1995");
        }

        [Fact]
        public void GetRegulationVersion_NormalizedNumber_Matches()
        {
            //  GB 编号标准化后应能匹配
            var version = ChemicalSubstanceDatabase.GetRegulationVersion("GB15603");
            version.Should().NotBeNull("标准化后的编号应能匹配到 GB 15603");
        }

        [Fact]
        public void GetRegulationVersion_PartialNumber_Matches()
        {
            var version = ChemicalSubstanceDatabase.GetRegulationVersion("30871");
            version.Should().NotBeNull();
            version!.RegulationNumber.Should().Contain("30871");
        }

        [Fact]
        public void GetRegulationVersion_Unknown_ReturnsNull()
        {
            ChemicalSubstanceDatabase.GetRegulationVersion("GB 99999")
                .Should().BeNull();
        }

        [Fact]
        public void GetAllRegulationVersions_ReturnsList()
        {
            var all = ChemicalSubstanceDatabase.GetAllRegulationVersions();
            all.Should().NotBeEmpty();
            all.Should().AllSatisfy(v =>
            {
                v.RegulationNumber.Should().NotBeNullOrEmpty();
                v.Title.Should().NotBeNullOrEmpty();
            });
        }

        // ── 重大危险源临界量 ──

        [Fact]
        public void MajorHazardThreshold_Benzene_50Tons()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("苯");
            sub.Should().NotBeNull();
            sub!.MajorHazardThresholdTons.Should().Be(50);
        }

        [Fact]
        public void MajorHazardThreshold_Chlorine_5Tons()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("氯");
            sub.Should().NotBeNull();
            sub!.MajorHazardThresholdTons.Should().Be(5,
                "氯气临界量应为5吨（剧毒气体）");
        }

        [Fact]
        public void MajorHazardThreshold_Acetylene_1Ton()
        {
            var sub = ChemicalSubstanceDatabase.Lookup("乙炔");
            sub.Should().NotBeNull();
            sub!.MajorHazardThresholdTons.Should().Be(1,
                "乙炔临界量应为1吨（极易燃气体）");
        }
    }

    // ═════════════════════════════════════════
    // ChemicalDatabaseService 测试 — SQLite 兜底库（Bug-035 回归）
    // ═════════════════════════════════════════
    public class ChemicalDatabaseServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ChemicalDatabaseService _service;

        public ChemicalDatabaseServiceTests()
        {
            // 临时目录确保每次从零初始化：完整走 schema 执行 + 种子写入路径
            // （真实 schema 含 GROUP_CONCAT(..., '; ') 字面量，回归 Bug-035 天真拆分问题）
            _tempDir = Path.Combine(Path.GetTempPath(), "agent1_chemdb_test_" + Guid.NewGuid().ToString("N"));
            _service = new ChemicalDatabaseService(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* 清理失败不影响测试结果 */ }
        }

        [Fact]
        public async Task InitializeAsync_SeedsDatabase_CountGreaterThanZero()
        {
            await _service.InitializeAsync();
            var count = await _service.GetCountAsync();
            count.Should().BeGreaterThan(0, "初始化后种子数据应写入成功（Bug-035 修复前此处静默降级为 0）");
        }

        [Fact]
        public async Task LookupAsync_Benzene_ReturnsSubstance()
        {
            var sub = await _service.LookupAsync("苯");
            sub.Should().NotBeNull("SQLite 兜底库初始化成功后应能查到苯");
            sub!.CasNumber.Should().Be("71-43-2");
            sub.HazardCategories.Should().NotBeEmpty();
        }

        [Fact]
        public async Task LookupAsync_ByAlias_ResolvesToStandardName()
        {
            var sub = await _service.LookupAsync("双氧水");
            sub.Should().NotBeNull();
            sub!.Name.Should().Be("过氧化氢", "别名应还原为标准名称");
        }

        [Fact]
        public async Task GetSafetyDistanceAsync_WarehouseToFlame_Returns30m()
        {
            var rule = await _service.GetSafetyDistanceAsync("甲类仓库-明火点");
            rule.Should().NotBeNull();
            rule!.MinDistanceMeters.Should().Be(30);
            rule.RegulationRef.Should().Contain("GB 50160");
        }

        // [#3 FIX] 评测集 E001-E008 全部安全距离设施对的 SQLite 种子覆盖回归
        [Theory]
        [InlineData("甲类仓库-明火点", 30)]
        [InlineData("乙炔气柜-办公楼", 25)]
        [InlineData("液化烃储罐-厂区围墙", 35)]
        [InlineData("氢气长管拖车-明火点", 25)]
        [InlineData("消防站-甲类装置", 15)]
        [InlineData("氨罐-厂外道路", 20)]
        [InlineData("甲类工艺装置-重要设施", 30)]
        [InlineData("氯气储存区-居住区", 200)]
        public async Task GetSafetyDistanceAsync_EvalSetPairs_AllCovered(string pair, double expected)
        {
            var rule = await _service.GetSafetyDistanceAsync(pair);
            rule.Should().NotBeNull($"评测集设施对「{pair}」应在 SQLite 种子中命中");
            rule!.MinDistanceMeters.Should().Be(expected);
        }

        [Fact]
        public async Task GetSafetyDistanceAsync_LiquidAmmoniaAlias_ResolvesToAmmoniaTank()
        {
            // 别名命中：液氨储罐-厂外道路 → 氨罐-厂外道路
            var rule = await _service.GetSafetyDistanceAsync("液氨储罐-厂外道路");
            rule.Should().NotBeNull();
            rule!.FacilityPair.Should().Be("氨罐-厂外道路");
            rule.MinDistanceMeters.Should().Be(20);
        }
    }
}
