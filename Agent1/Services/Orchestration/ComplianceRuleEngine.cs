using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// 合规规则引擎 — 对标 Dependency-Track 的漏洞匹配引擎。
    /// 
    /// DT 流程: 导入组件 → 自动匹配 NVD/OSV 漏洞库 → 生成 Finding
    /// 化工流程: 导入资产 → 自动匹配法规规则库 → 生成 ComplianceFinding
    /// 
    /// 核心价值：从"人手动查每条合规"变成"系统自动扫描所有资产"。
    /// </summary>
    public class ComplianceRuleEngine
    {
        private readonly AgentDialog _agentDialog;
        private readonly IAuditService _audit;

        // 静态规则库 — 未来可从数据库或配置文件加载
        // 对标 DT 的漏洞数据源（NVD/GitHub Advisory）
        private static readonly List<ComplianceRule> BuiltInRules = new()
        {
            new ComplianceRule
            {
                RuleId = "SR-001",
                Name = "甲/乙类化学品同库储存检查",
                RegulationRef = "GB 15603-1995 §4.2.2",
                Description = "禁忌物料不得同库储存",
                Severity = FindingSeverity.Critical,
                CheckType = CheckType.StorageCompatibility,
                AutoCheckExpression = asset =>
                    asset.Location.Contains("甲类") || asset.Location.Contains("乙类")
            },
            new ComplianceRule
            {
                RuleId = "SD-001",
                Name = "甲类仓库安全距离检查",
                RegulationRef = "GB 50160 §5.2.1",
                Description = "甲类仓库与明火点间距≥30m",
                Severity = FindingSeverity.High,
                CheckType = CheckType.SafetyDistance,
                AutoCheckExpression = asset =>
                    asset.Location.Contains("甲类仓库")
            },
            new ComplianceRule
            {
                RuleId = "MHR-001",
                Name = "重大危险源临界量检查",
                RegulationRef = "GB 18218-2018",
                Description = "存量超过临界量需纳入重大危险源管理",
                Severity = FindingSeverity.Critical,
                CheckType = CheckType.HazardQuantity,
                AutoCheckExpression = asset =>
                    asset.IsMajorHazardSource && asset.QuantityTons > 0
            },
            new ComplianceRule
            {
                RuleId = "GH-001",
                Name = "危化品 GHS 标签合规",
                RegulationRef = "GB 15258-2009",
                Description = "所有危化品包装必须张贴 GHS 标签",
                Severity = FindingSeverity.Medium,
                CheckType = CheckType.GhsLabel,
                AutoCheckExpression = asset => true  // 所有危化品都适用
            },
            new ComplianceRule
            {
                RuleId = "FI-001",
                Name = "仓库消防设施检查",
                RegulationRef = "GB 50140-2005",
                Description = "危化品仓库必须配备相应灭火器且定期检查",
                Severity = FindingSeverity.High,
                CheckType = CheckType.FireEquipment,
                AutoCheckExpression = asset =>
                    asset.Location.Contains("仓库")
            },
        };

        public ComplianceRuleEngine(AgentDialog agentDialog, IAuditService audit)
        {
            _agentDialog = agentDialog;
            _audit = audit;
        }

        // ═══════════════════════════════════════
        // 核心方法: 自动扫描资产 → 生成发现
        // ═══════════════════════════════════════

        /// <summary>
        /// 对标 DT 的"导入 SBOM → 自动匹配漏洞"。
        /// 输入: 化学品资产列表
        /// 输出: 不合规发现列表（仅包含触发规则的条目）
        /// </summary>
        public async Task<ComplianceScanResult> ScanAssetsAsync(
            List<ChemicalAsset> assets, string operator_ = "system")
        {
            var result = new ComplianceScanResult
            {
                ScannedAt = DateTime.Now,
                TotalAssets = assets.Count
            };

            Console.WriteLine($"\n🔍 开始自动合规扫描: {assets.Count} 个资产, {BuiltInRules.Count} 条规则");
            Console.WriteLine(new string('─', 50));

            foreach (var asset in assets)
            {
                // 1. 筛选该资产适用的规则（对标 DT 的 rule-to-component matching）
                var applicableRules = BuiltInRules
                    .Where(r => r.AutoCheckExpression(asset))
                    .ToList();

                if (applicableRules.Count == 0) continue;

                // 2. 对每条适用规则执行检查
                foreach (var rule in applicableRules)
                {
                    var finding = await CheckAssetAgainstRuleAsync(asset, rule);

                    if (finding != null)
                    {
                        result.Findings.Add(finding);
                        result.TotalFindings++;
                        if (finding.Status == FindingStatus.New)
                            result.NewFindings++;
                    }
                }

                result.CheckedAssets++;
                asset.LastCheckedAt = DateTime.Now;

                // 进度显示
                if (result.CheckedAssets % 3 == 0 || result.CheckedAssets == assets.Count)
                    Console.WriteLine($"   进度: {result.CheckedAssets}/{assets.Count} | 发现: {result.NewFindings}个新问题");
            }

            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"✅ 扫描完成: {result.CheckedAssets}/{result.TotalAssets} 资产已检查 | " +
                $"新增 {result.NewFindings} 个发现 | {result.TotalFindings - result.NewFindings} 个已知");

            await _audit.LogOperationAsync(operator_, "ComplianceScan",
                $"自动扫描: {result.CheckedAssets}/{result.TotalAssets}资产 | {result.NewFindings}个新发现",
                isSensitive: true);

            return result;
        }

        /// <summary>
        /// 单项资产 vs 单条规则检查（对标 DT 的 component-vs-vulnerability matching）
        /// </summary>
        private async Task<ComplianceFinding> CheckAssetAgainstRuleAsync(
            ChemicalAsset asset, ComplianceRule rule)
        {
            // 构建检查查询
            var query = BuildCheckQuery(asset, rule);
            var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);

            Console.Write($"   [{asset.Name}] {rule.Name.Truncate(25)} ... ");

            // 走完整的 6 步流水线（含 SafetyGuardService + PipelineMetrics）
            var execResult = await _agentDialog.ExecuteAsync(query, session);

            // 解析合规判定
            var isCompliant = InspectionItemResult.From(0, execResult).IsCompliant;

            if (isCompliant == false)
            {
                Console.WriteLine("❌ 不合规");
                return new ComplianceFinding
                {
                    AssetId = asset.AssetId,
                    RuleId = rule.RuleId,
                    RegulationRef = rule.RegulationRef,
                    Description = $"{asset.Name} @ {asset.Location}: {rule.Description}",
                    Severity = rule.Severity,
                    Status = FindingStatus.New,
                    RemediationPlan = execResult.DisplayOutput.Truncate(200)
                };
            }
            else if (isCompliant == true)
            {
                Console.WriteLine("✅ 合规");
                // 合规不生成 Finding（对标 DT 的 NOT_AFFECTED）
                return null!;
            }
            else
            {
                Console.WriteLine("⚠️ 无法判定");
                // 无法判定时生成低优先级 Finding 供人工复核
                return new ComplianceFinding
                {
                    AssetId = asset.AssetId,
                    RuleId = rule.RuleId,
                    RegulationRef = rule.RegulationRef,
                    Description = $"{asset.Name} @ {asset.Location}: {rule.Description}（AI无法判定，需人工复核）",
                    Severity = FindingSeverity.Low,
                    Status = FindingStatus.New
                };
            }
        }

        // ═══════════════════════════════════════
        // 仪表盘聚合（对标 DT 的 Portfolio Metrics）
        // ═══════════════════════════════════════

        /// <summary>
        /// 从资产列表 + 发现列表计算合规总览指标。
        /// 对标 DT Dashboard 的: 总组件数/漏洞数/修复率/风险分布。
        /// </summary>
        public ComplianceOverview BuildOverview(
            List<ChemicalAsset> assets, List<ComplianceFinding> findings, DateTime? lastScan)
        {
            var overview = new ComplianceOverview
            {
                TotalAssets = assets.Count,
                CheckedAssets = assets.Count(a => a.LastCheckedAt.HasValue),
                CompliantAssets = assets.Count(a => a.LastCheckResult == true),
                NonCompliantAssets = assets.Count(a => a.LastCheckResult == false),
                TotalFindings = findings.Count,
                OpenFindings = findings.Count(f => f.IsOpen),
                LastAutoScanAt = lastScan,
                FindingsBySeverity = findings
                    .GroupBy(f => f.Severity)
                    .ToDictionary(g => g.Key, g => g.Count()),
                FindingsByStatus = findings
                    .GroupBy(f => f.Status)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return overview;
        }

        // ═══════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════

        private static string BuildCheckQuery(ChemicalAsset asset, ComplianceRule rule)
        {
            return rule.CheckType switch
            {
                CheckType.StorageCompatibility =>
                    $"化学品 {asset.Name} 存放在 {asset.Location}，" +
                    $"存量 {asset.QuantityTons} 吨，储存条件 {asset.StorageCondition}。" +
                    $"请检查其储存是否符合 {rule.RegulationRef} 的要求。",

                CheckType.SafetyDistance =>
                    $"化学品 {asset.Name} 存放在 {asset.Location}，" +
                    $"请检查其与周边设施的安全距离是否符合 {rule.RegulationRef}。",

                CheckType.HazardQuantity =>
                    $"{asset.Name} 存量 {asset.QuantityTons} 吨，" +
                    $"存放于 {asset.Location}。请根据 {rule.RegulationRef} 判定是否构成重大危险源。",

                CheckType.FireEquipment =>
                    $"{asset.Location} 存放 {asset.Name}。" +
                    $"请检查消防设施配备是否符合 {rule.RegulationRef}。",

                _ => $"{asset.Name} 在 {asset.Location} 是否符合 {rule.RegulationRef}？"
            };
        }
    }

    // ═══════════════════════════════════════
    // 支撑类型
    // ═══════════════════════════════════════

    /// <summary>
    /// 合规规则 — 对标 DT 的漏洞数据源条目。
    /// 每条规则定义了一种合规检查类型及其触发条件。
    /// </summary>
    public class ComplianceRule
    {
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RegulationRef { get; set; } = "";
        public string Description { get; set; } = "";
        public FindingSeverity Severity { get; set; } = FindingSeverity.Medium;
        public CheckType CheckType { get; set; } = CheckType.StorageCompatibility;
        /// <summary>规则自动触发条件 — true 时该资产需要执行此规则</summary>
        public Func<ChemicalAsset, bool> AutoCheckExpression { get; set; } = _ => true;
    }

    /// <summary>检查类型</summary>
    public enum CheckType
    {
        StorageCompatibility,
        SafetyDistance,
        HazardQuantity,
        GhsLabel,
        FireEquipment,
        EmergencyAccess,
        Custom
    }

    /// <summary>合规扫描结果</summary>
    public class ComplianceScanResult
    {
        public DateTime ScannedAt { get; set; }
        public int TotalAssets { get; set; }
        public int CheckedAssets { get; set; }
        public int TotalFindings { get; set; }
        public int NewFindings { get; set; }
        public List<ComplianceFinding> Findings { get; set; } = new();
    }
}
