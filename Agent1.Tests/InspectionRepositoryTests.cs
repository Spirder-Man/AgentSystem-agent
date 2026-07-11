using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent1.Models;
using Agent1.Services.Orchestration;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// InspectionRepository CRUD 测试 — 覆盖巡检计划、轮次、资产、合规发现的持久化操作。
/// 测试通过公开 API 的 roundtrip（Save → Get）验证数据完整性。
/// </summary>
public class InspectionRepositoryTests : IDisposable
{
    private readonly string _dataFile;

    public InspectionRepositoryTests()
    {
        _dataFile = Path.Combine(AppContext.BaseDirectory, "Data", "inspection-store.json");
        // 清理前一���测试的残留文件，确保每个测试从干净状态开始
        CleanupFile();
    }

    public void Dispose()
    {
        CleanupFile();
    }

    private void CleanupFile()
    {
        try { if (File.Exists(_dataFile)) File.Delete(_dataFile); }
        catch { /* 忽略清理失败 */ }
    }

    // ═══════════════════════════════════════════════════════
    // 巡检计划 CRUD
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SavePlan_NewPlan_ShouldStore()
    {
        var repo = new InspectionRepository();
        var plan = new InspectionPlan
        {
            Name = "甲类仓库周检",
            Type = InspectionType.DailyWeekly,
            Area = "A区",
            Inspector = "张工",
            Items = new List<InspectionItem>
            {
                new() { ItemId = 1, Query = "温度是否≤30℃", StandardValue = "≤30℃", CheckType = InspectionCheckType.NumericCheck }
            }
        };

        repo.SavePlan(plan);

        var retrieved = repo.GetPlan(plan.PlanId);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("甲类仓库周检");
        retrieved.Area.Should().Be("A区");
        retrieved.Inspector.Should().Be("张工");
        retrieved.Status.Should().Be(InspectionStatus.Draft);
        retrieved.Items.Should().HaveCount(1);
    }

    [Fact]
    public void SavePlan_UpdateExisting_ShouldOverwrite()
    {
        var repo = new InspectionRepository();
        var plan = new InspectionPlan { Name = "原计划", Area = "B区" };
        repo.SavePlan(plan);

        // 修改后再次保存
        plan.Name = "更新后计划";
        plan.Status = InspectionStatus.Completed;
        repo.SavePlan(plan);

        var retrieved = repo.GetPlan(plan.PlanId);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("更新后计划");
        retrieved.Status.Should().Be(InspectionStatus.Completed);
    }

    [Fact]
    public void GetPlan_NonexistentId_ShouldReturnNull()
    {
        var repo = new InspectionRepository();
        var result = repo.GetPlan("nonexistent-id");
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllPlans_MultiplePlans_ShouldReturnAll()
    {
        var repo = new InspectionRepository();
        repo.SavePlan(new InspectionPlan { Name = "计划A" });
        repo.SavePlan(new InspectionPlan { Name = "计划B" });
        repo.SavePlan(new InspectionPlan { Name = "计划C" });

        var plans = repo.GetAllPlans();
        plans.Should().HaveCount(3);
        plans.Select(p => p.Name).Should().Contain(new[] { "计划A", "计划B", "计划C" });
    }

    // ═══════════════════════════════════════════════════════
    // 巡检轮次 CRUD
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SaveRound_NewRound_ShouldStore()
    {
        var repo = new InspectionRepository();
        var round = new InspectionRound
        {
            PlanId = "plan-001",
            ExecutedBy = "巡检员A",
            StartedAt = DateTime.Now,
            Results = new List<InspectionItemResult>
            {
                new() { ItemId = 1, IsCompliant = true, Conclusion = "合格", RegulationRef = "GB 15603" }
            }
        };

        repo.SaveRound(round);

        var retrieved = repo.GetRound(round.RoundId);
        retrieved.Should().NotBeNull();
        retrieved!.PlanId.Should().Be("plan-001");
        retrieved.ExecutedBy.Should().Be("巡检员A");
        retrieved.Results.Should().HaveCount(1);
    }

    [Fact]
    public void GetRoundsByPlan_MultipleRounds_ShouldFilterByPlanId()
    {
        var repo = new InspectionRepository();
        repo.SaveRound(new InspectionRound { PlanId = "plan-X", ExecutedBy = "A" });
        repo.SaveRound(new InspectionRound { PlanId = "plan-X", ExecutedBy = "B" });
        repo.SaveRound(new InspectionRound { PlanId = "plan-Y", ExecutedBy = "C" });

        var roundsX = repo.GetRoundsByPlan("plan-X");
        roundsX.Should().HaveCount(2);
        roundsX.All(r => r.PlanId == "plan-X").Should().BeTrue();
    }

    [Fact]
    public void GetRound_NonexistentId_ShouldReturnNull()
    {
        var repo = new InspectionRepository();
        var result = repo.GetRound("nonexistent-round");
        result.Should().BeNull();
    }

    [Fact]
    public void SaveRound_UpdateExisting_ShouldOverwrite()
    {
        var repo = new InspectionRepository();
        var round = new InspectionRound { PlanId = "plan-001", ExecutedBy = "A" };
        repo.SaveRound(round);

        round.CompletedAt = DateTime.Now;
        round.ExecutedBy = "B";
        repo.SaveRound(round);

        var retrieved = repo.GetRound(round.RoundId);
        retrieved.Should().NotBeNull();
        retrieved!.ExecutedBy.Should().Be("B");
        retrieved.CompletedAt.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════
    // 化学品资产台账
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetAllAssets_FirstCall_ShouldReturnDemoData()
    {
        var repo = new InspectionRepository();

        var assets = repo.GetAllAssets();

        // 首次调用应加载演示数据（CreateDemoInventory）
        assets.Should().NotBeNull();
        assets.Should().NotBeEmpty("首次启动应加载演示资产数据");
    }

    [Fact]
    public void SaveAsset_NewAsset_ShouldStore()
    {
        var repo = new InspectionRepository();
        var asset = new ChemicalAsset
        {
            AssetId = "ASSET-001",
            Name = "苯",
            CasNumber = "71-43-2",
            QuantityTons = 500,
            Location = "甲类仓库A区",
            StorageCondition = "常温常压"
        };

        repo.SaveAsset(asset);
        var retrieved = repo.GetAsset("ASSET-001");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("苯");
        retrieved.Location.Should().Be("甲类仓库A区");
    }

    [Fact]
    public void GetAsset_NonexistentId_ShouldReturnNull()
    {
        var repo = new InspectionRepository();
        // 先加载演示数据，确保 Assets 列表已初始化
        repo.GetAllAssets();

        var result = repo.GetAsset("NO-SUCH-ASSET");
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════
    // 合规发现
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SaveFinding_NewFinding_ShouldStore()
    {
        var repo = new InspectionRepository();
        var finding = new ComplianceFinding
        {
            FindingId = "F-001",
            Description = "甲类仓库温度超标",
            Severity = FindingSeverity.High,
            Status = FindingStatus.New
        };

        repo.SaveFinding(finding);
        var findings = repo.GetAllFindings();
        findings.Should().ContainSingle(f => f.FindingId == "F-001");
    }

    [Fact]
    public void GetOpenFindings_ShouldFilterClosedOnes()
    {
        var repo = new InspectionRepository();
        repo.SaveFinding(new ComplianceFinding { FindingId = "F1", Status = FindingStatus.New });
        repo.SaveFinding(new ComplianceFinding { FindingId = "F2", Status = FindingStatus.Closed });
        repo.SaveFinding(new ComplianceFinding { FindingId = "F3", Status = FindingStatus.Confirmed });

        var openFindings = repo.GetOpenFindings();
        openFindings.Should().HaveCount(2);
        openFindings.All(f => f.IsOpen).Should().BeTrue();
    }

    [Fact]
    public void SaveFindings_Batch_ShouldUpsert()
    {
        var repo = new InspectionRepository();
        // 先存一条
        repo.SaveFinding(new ComplianceFinding { FindingId = "F1", Description = "旧描述", Status = FindingStatus.New });

        // 批量保存（含更新和新增）
        var findings = new List<ComplianceFinding>
        {
            new() { FindingId = "F1", Description = "更新描述", Status = FindingStatus.Closed }, // 更新
            new() { FindingId = "F2", Description = "新发现", Status = FindingStatus.New }     // 新增
        };
        repo.SaveFindings(findings);

        var all = repo.GetAllFindings();
        all.Should().ContainSingle(f => f.FindingId == "F1" && f.Description == "更新描述");
        all.Should().ContainSingle(f => f.FindingId == "F2" && f.Description == "新发现");
    }

    // ═══════════════════════════════════════════════════════
    // 扫描时间
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetLastScanTime_NewRepo_ShouldReturnNull()
    {
        var repo = new InspectionRepository();
        repo.GetLastScanTime().Should().BeNull();
    }

    [Fact]
    public void SetLastScanTime_ShouldPersist()
    {
        var repo = new InspectionRepository();
        var now = new DateTime(2026, 7, 10, 14, 30, 0);

        repo.SetLastScanTime(now);
        repo.GetLastScanTime().Should().Be(now);
    }

    // ═══════════════════════════════════════════════════════
    // 统计信息
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetStats_ShouldReflectCurrentState()
    {
        var repo = new InspectionRepository();
        repo.SavePlan(new InspectionPlan { Name = "P1" });
        repo.SaveRound(new InspectionRound { PlanId = "any" });
        repo.SaveFinding(new ComplianceFinding { FindingId = "F1", Status = FindingStatus.New });
        repo.SaveFinding(new ComplianceFinding { FindingId = "F2", Status = FindingStatus.VerifiedClosed });

        dynamic stats = repo.GetStats();

        ((int)stats.Plans).Should().Be(1);
        ((int)stats.Rounds).Should().Be(1);
        // Assets 在首次 GetAllAssets() 调用前为 0，GetStats 不触发懒加载
        ((int)stats.Findings).Should().Be(2);
        ((int)stats.OpenFindings).Should().Be(1);
    }
}
