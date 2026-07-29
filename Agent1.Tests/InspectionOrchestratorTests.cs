using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Agent1.Tests.Stubs;
using FluentAssertions;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// InspectionOrchestrator 单元测试 — 覆盖巡检编排的核心逻辑。
/// 只测试不依赖 LLM 的纯编排方法（CreatePlan / GetPlan / GetAllPlans / GenerateReport），
/// ExecutePlanAsync 和 ExecuteQuickCheckAsync 需要真实 LLM 服务，归属集成测试。
/// </summary>
public class InspectionOrchestratorTests : IDisposable
{
    private readonly InspectionRepository _repo;
    private readonly InspectionOrchestrator _orchestrator;
    private readonly string _dataFile;
    private readonly Mock<IAuditService> _auditMock;
    private readonly Mock<IKnowledgeBaseService> _kbMock;
    private readonly Mock<ILlmService> _llmMock;
    private readonly Mock<ISessionService> _sessionMock;

    public InspectionOrchestratorTests()
    {
        _dataFile = Path.Combine(AppContext.BaseDirectory, "Data", "inspection-store.json");
        CleanupFile();

        _repo = new InspectionRepository();
        _auditMock = new Mock<IAuditService>(MockBehavior.Loose);
        _kbMock = new Mock<IKnowledgeBaseService>(MockBehavior.Loose);
        _llmMock = new Mock<ILlmService>(MockBehavior.Loose);
        _sessionMock = new Mock<ISessionService>(MockBehavior.Loose);

        // AgentDialog 传 null! — CreatePlan/GetPlan/GenerateReport 不走 LLM，不会触发
        _orchestrator = new InspectionOrchestrator(
            null!, _kbMock.Object, _auditMock.Object,
            _llmMock.Object, _sessionMock.Object,
            _repo, new DeterministicRuleEngine(new StubChemicalKnowledgeGraph(), new ChemicalNamingInference()));
    }

    public void Dispose() => CleanupFile();

    private void CleanupFile()
    {
        try { if (File.Exists(_dataFile)) File.Delete(_dataFile); }
        catch { /* 忽略 */ }
    }

    // ═══════════════════════════════════════════════════════
    // CreatePlan
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void CreatePlan_ShouldSaveAndReturnPlan()
    {
        var items = new List<InspectionItem>
        {
            new() { ItemId = 1, Query = "温度是否≤30℃", StandardValue = "≤30℃", CheckType = InspectionCheckType.NumericCheck },
            new() { ItemId = 2, Query = "通风装置是否正常", CheckType = InspectionCheckType.BooleanCheck }
        };

        var plan = _orchestrator.CreatePlan("甲类仓库周检", InspectionType.DailyWeekly,
            "A区", "张三", items, "测试备注");

        plan.Should().NotBeNull();
        plan.Name.Should().Be("甲类仓库周检");
        plan.Type.Should().Be(InspectionType.DailyWeekly);
        plan.Area.Should().Be("A区");
        plan.Inspector.Should().Be("张三");
        plan.Status.Should().Be(InspectionStatus.Draft);
        plan.Items.Should().HaveCount(2);
        plan.Notes.Should().Be("测试备注");

        // 验证持久化
        var fromRepo = _orchestrator.GetPlan(plan.PlanId);
        fromRepo.Should().NotBeNull();
        fromRepo!.Name.Should().Be("甲类仓库周检");
    }

    [Fact]
    public void CreatePlan_WithDefaultNotes_ShouldUseEmptyString()
    {
        var plan = _orchestrator.CreatePlan("测试计划", InspectionType.Monthly,
            "B区", "李四", new List<InspectionItem> { new() { ItemId = 1, Query = "检查1" } });

        plan.Notes.Should().Be("");
    }

    // ═══════════════════════════════════════════════════════
    // GetAllPlans
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetAllPlans_ShouldReturnAllSavedPlans()
    {
        var plan1 = _orchestrator.CreatePlan("P1", InspectionType.DailyWeekly, "A区", "甲",
            new List<InspectionItem> { new() { ItemId = 1, Query = "Q1" } });
        var plan2 = _orchestrator.CreatePlan("P2", InspectionType.Monthly, "B区", "乙",
            new List<InspectionItem> { new() { ItemId = 1, Query = "Q2" } });

        var plans = _orchestrator.GetAllPlans();
        plans.Select(p => p.Name).Should().Contain(new[] { "P1", "P2" });
        plans.Should().Contain(p => p.PlanId == plan1.PlanId);
        plans.Should().Contain(p => p.PlanId == plan2.PlanId);
    }

    [Fact]
    public void GetAllPlans_EmptyRepo_ShouldReturnEmptyList()
    {
        var plans = _orchestrator.GetAllPlans();
        plans.Should().NotBeNull();
        plans.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════
    // GetPlan
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetPlan_ExistingId_ShouldReturnPlan()
    {
        var plan = _orchestrator.CreatePlan("测试计划", InspectionType.PreHoliday, "C区", "丙",
            new List<InspectionItem> { new() { ItemId = 1, Query = "Q" } });

        var retrieved = _orchestrator.GetPlan(plan.PlanId);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("测试计划");
    }

    [Fact]
    public void GetPlan_NonexistentId_ShouldReturnNull()
    {
        var result = _orchestrator.GetPlan("no-such-id");
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════
    // GenerateReport
    // ═══════════════════════════════════════════════════════

    /// <summary>创建一套完整的计划+轮次数据供 GenerateReport 使用</summary>
    private (InspectionPlan plan, InspectionRound round) CreatePlanAndRound()
    {
        var items = new List<InspectionItem>
        {
            new() { ItemId = 1, Query = "仓库温度是否≤30℃", StandardValue = "≤30℃", CheckType = InspectionCheckType.NumericCheck },
            new() { ItemId = 2, Query = "通风装置是否正常运行", CheckType = InspectionCheckType.BooleanCheck },
            new() { ItemId = 3, Query = "有无禁忌物料混存", CheckType = InspectionCheckType.AIInference }
        };

        var plan = _orchestrator.CreatePlan("甲类仓库A区周检", InspectionType.DailyWeekly,
            "甲类仓库A区", "安全员张三", items);

        // 模拟执行结果
        var round = new InspectionRound
        {
            PlanId = plan.PlanId,
            ExecutedBy = "安全员张三",
            StartedAt = new DateTime(2026, 7, 10, 8, 0, 0),
            CompletedAt = new DateTime(2026, 7, 10, 8, 15, 0),
            Results = new List<InspectionItemResult>
            {
                new() { ItemId = 1, IsCompliant = true, Conclusion = "温度28℃，合规", RegulationRef = "GB 15603-2022 §4.2.2" },
                new() { ItemId = 2, IsCompliant = false, Conclusion = "通风装置故障", RegulationRef = "GB 15603-2022 §5.1.1",
                    Warnings = new List<string> { "通风装置停机超过24小时，需紧急修复" } },
                new() { ItemId = 3, IsCompliant = true, Conclusion = "未发现禁忌混存", RegulationRef = "GB 15603-2022 §4.2.2" }
            }
        };

        _repo.SaveRound(round);
        return (plan, round);
    }

    [Fact]
    public void GenerateReport_ValidRound_ShouldReturnReportWithCorrectProperties()
    {
        var (plan, round) = CreatePlanAndRound();

        var report = _orchestrator.GenerateReport(round.RoundId, "生成人A");

        report.Should().NotBeNull();
        report.RoundId.Should().Be(round.RoundId);
        report.GeneratedBy.Should().Be("生成人A");
        report.Plan.Should().NotBeNull();
        report.Plan.Name.Should().Be(plan.Name);
        report.Round.Should().NotBeNull();
        report.ComplianceRate.Should().BeApproximately(2.0 / 3.0, 0.001);
    }

    [Fact]
    public void GenerateReport_ShouldIncludeCriticalFindings()
    {
        var (_, round) = CreatePlanAndRound();

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");

        // ItemId=2 是不合规且带警告的 → 应出现在 CriticalFindings 中
        report.CriticalFindings.Should().NotBeEmpty();
        report.CriticalFindings.Should().Contain(f => f.Contains("[2]"));
    }

    [Fact]
    public void GenerateReport_ShouldComputeAuditHash()
    {
        var (_, round) = CreatePlanAndRound();

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");

        report.AuditHash.Should().NotBeNullOrEmpty();
        report.AuditHash.Should().NotBe("HASH_ERROR");
        // SHA256 hex string: 64 hex characters
        report.AuditHash.Length.Should().Be(64);
    }

    [Fact]
    public void GenerateReport_SameData_ShouldProduceSameHash()
    {
        var (_, round) = CreatePlanAndRound();

        var report1 = _orchestrator.GenerateReport(round.RoundId, "A");
        var report2 = _orchestrator.GenerateReport(round.RoundId, "A");

        report1.AuditHash.Should().Be(report2.AuditHash);
    }

    [Fact]
    public void GenerateReport_ShouldProduceSummaryWithComplianceRate()
    {
        var (plan, round) = CreatePlanAndRound();

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");

        report.Summary.Should().Contain(plan.Name);
        report.Summary.Should().Contain(plan.Area);
        report.Summary.Should().Contain("2"); // compliant count
        report.Summary.Should().Contain("1"); // non-compliant count
    }

    [Fact]
    public void GenerateReport_NonexistentRound_ShouldThrow()
    {
        Action act = () => _orchestrator.GenerateReport("no-such-round", "Tester");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*不存在*");
    }

    [Fact]
    public void GenerateReport_OrphanRound_PlanNotFound_ShouldThrow()
    {
        // 直接通过 repo 存一个 planId 不存在的 round
        var orphanRound = new InspectionRound
        {
            PlanId = "NONEXISTENT-PLAN",
            ExecutedBy = "Tester",
            Results = new List<InspectionItemResult>()
        };
        _repo.SaveRound(orphanRound);

        Action act = () => _orchestrator.GenerateReport(orphanRound.RoundId, "Tester");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*计划不存在*");
    }

    // ═══════════════════════════════════════════════════════
    // GetOrGenerateReport
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetOrGenerateReport_ShouldDelegateToGenerateReport()
    {
        var (_, round) = CreatePlanAndRound();

        var report = _orchestrator.GetOrGenerateReport(round.RoundId, "Tester");

        report.Should().NotBeNull();
        report.AuditHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetOrGenerateReport_NonexistentRound_ShouldThrow()
    {
        Action act = () => _orchestrator.GetOrGenerateReport("no-such-round", "Tester");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*不存在*");
    }

    // ═══════════════════════════════════════════════════════
    // Summary edge cases: coverage of GenerateSummary branches
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GenerateReport_HighComplianceRate_ShouldIndicateGood()
    {
        var plan = _orchestrator.CreatePlan("高合规计划", InspectionType.DailyWeekly, "X区", "甲",
            new List<InspectionItem>
            {
                new() { ItemId = 1, Query = "Q1" },
                new() { ItemId = 2, Query = "Q2" },
                new() { ItemId = 3, Query = "Q3" }
            });

        var round = new InspectionRound
        {
            PlanId = plan.PlanId,
            ExecutedBy = "甲",
            Results = new List<InspectionItemResult>
            {
                new() { ItemId = 1, IsCompliant = true },
                new() { ItemId = 2, IsCompliant = true },
                new() { ItemId = 3, IsCompliant = true }
            }
        };
        _repo.SaveRound(round);

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");
        report.ComplianceRate.Should().Be(1.0);
        report.Summary.Should().Contain("良好");
    }

    [Fact]
    public void GenerateReport_MediumComplianceRate_ShouldIndicateNeedsFixing()
    {
        var plan = _orchestrator.CreatePlan("中等合规计划", InspectionType.DailyWeekly, "Y区", "乙",
            new List<InspectionItem>
            {
                new() { ItemId = 1, Query = "Q1" },
                new() { ItemId = 2, Query = "Q2" },
                new() { ItemId = 3, Query = "Q3" }
            });

        var round = new InspectionRound
        {
            PlanId = plan.PlanId,
            ExecutedBy = "乙",
            Results = new List<InspectionItemResult>
            {
                new() { ItemId = 1, IsCompliant = true },
                new() { ItemId = 2, IsCompliant = false },
                new() { ItemId = 3, IsCompliant = true }
            }
        };
        _repo.SaveRound(round);

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");
        report.ComplianceRate.Should().BeApproximately(2.0 / 3.0, 0.001);
        report.Summary.Should().Contain("整改");
    }

    [Fact]
    public void GenerateReport_LowComplianceRate_ShouldIndicateUrgent()
    {
        var plan = _orchestrator.CreatePlan("低合规计划", InspectionType.DailyWeekly, "Z区", "丙",
            new List<InspectionItem>
            {
                new() { ItemId = 1, Query = "Q1" },
                new() { ItemId = 2, Query = "Q2" },
                new() { ItemId = 3, Query = "Q3" }
            });

        var round = new InspectionRound
        {
            PlanId = plan.PlanId,
            ExecutedBy = "丙",
            Results = new List<InspectionItemResult>
            {
                new() { ItemId = 1, IsCompliant = false },
                new() { ItemId = 2, IsCompliant = false },
                new() { ItemId = 3, IsCompliant = false }
            }
        };
        _repo.SaveRound(round);

        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");
        report.ComplianceRate.Should().Be(0.0);
        report.Summary.Should().Contain("专项整改");
    }

    // ═══════════════════════════════════════════════════════
    // ToMarkdown: verify report markdown output
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void InspectionReport_ToMarkdown_ShouldContainKeyElements()
    {
        var (plan, round) = CreatePlanAndRound();
        var report = _orchestrator.GenerateReport(round.RoundId, "Tester");

        var md = report.ToMarkdown();

        md.Should().Contain("# 化工安全巡检报告");
        md.Should().Contain(plan.Name);
        md.Should().Contain(plan.Area);
        md.Should().Contain(round.ExecutedBy);
        md.Should().Contain(report.AuditHash);
        md.Should().Contain("合规");
        md.Should().Contain("不合规");
    }
}
