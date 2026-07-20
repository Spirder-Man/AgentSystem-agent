using System;
using System.Collections.Generic;
using System.Linq;

namespace Agent1.Models
{
    // ═══════════════════════════════════════════════════
    // Dependency-Track 业务模型映射 → 化工安全场景
    //
    // DT 闭环:
    //   SBOM导入 → 组件清单 → 漏洞匹配 → Finding生成 → 修复跟踪 → 验证关闭 → 指标总览
    //
    // 化工安全闭环:
    //   化学品台账 → 资产清单 → 法规匹配 → 不合规发现 → 整改跟踪 → 验收关闭 → 合规总览
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 化工资产 — 对标 Dependency-Track 的 Component（SBOM 条目）。
    /// 
    /// 每个 ChemicalAsset 代表园区内实际存储/使用的化学品实例。
    /// 与 ChemicalSubstanceModels 的区别：
    ///   ChemicalSubstance = 化学品标准属性（CAS号、闪点、危险类别）— 静态知识库
    ///   ChemicalAsset = 化学品在园区内的实际存储情况（位置、数量、储存条件）— 动态台账
    /// </summary>
    public class ChemicalAsset
    {
        /// <summary>资产唯一ID</summary>
        public string AssetId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>化学品名称（对应 ChemicalSubstance.Name）</summary>
        public string Name { get; set; } = "";

        /// <summary>CAS 号</summary>
        public string CasNumber { get; set; } = "";

        /// <summary>存储位置（如 "甲类仓库A区3排2号位"）</summary>
        public string Location { get; set; } = "";

        /// <summary>当前存量（吨）</summary>
        public double QuantityTons { get; set; }

        /// <summary>储存条件（如 "常温常压"、"2-8℃冷藏"、"氮气保护"）</summary>
        public string StorageCondition { get; set; } = "";

        /// <summary>责任人</summary>
        public string ResponsiblePerson { get; set; } = "";

        /// <summary>是否在重大危险源清单内</summary>
        public bool IsMajorHazardSource { get; set; }

        /// <summary>最近一次合规检查时间</summary>
        public DateTime? LastCheckedAt { get; set; }

        /// <summary>最近一次合规状态（true=合规, false=不合规, null=未检查）</summary>
        public bool? LastCheckResult { get; set; }

        /// <summary>关联的适用法规列表（工厂方法初始化时可从 ChemicalSubstance 推断）</summary>
        public List<string> ApplicableRegulations { get; set; } = new();

        /// <summary>共存的化学品名称列表（用于自动触发储存兼容性检查）</summary>
        public List<string> CoLocatedWith { get; set; } = new();

        // ── 工厂方法 ──

        /// <summary>从化学品标准属性创建资产实例</summary>
        public static ChemicalAsset FromSubstance(string name, string cas, string location,
            double quantityTons, string responsible = "未分配")
        {
            return new ChemicalAsset
            {
                Name = name,
                CasNumber = cas,
                Location = location,
                QuantityTons = quantityTons,
                ResponsiblePerson = responsible
            };
        }

        /// <summary>生成批量资产的简表（用于快速搭建演示台账）</summary>
        public static List<ChemicalAsset> CreateDemoInventory()
        {
            return new List<ChemicalAsset>
            {
                FromSubstance("苯", "71-43-2", "甲类仓库A区1号位", 15, "张三"),
                FromSubstance("丙酮", "67-64-1", "甲类仓库A区2号位", 8, "张三"),
                FromSubstance("甲醇", "67-56-1", "甲类仓库B区1号位", 20, "李四"),
                FromSubstance("硝酸", "7697-37-2", "乙类仓库C区3号位", 5, "王五"),
                FromSubstance("硫酸", "7664-93-9", "乙类仓库C区1号位", 12, "王五"),
                FromSubstance("甲苯", "108-88-3", "甲类仓库A区3号位", 10, "张三"),
                FromSubstance("盐酸", "7647-01-0", "乙类仓库C区2号位", 8, "赵六"),
                FromSubstance("氢氧化钠", "1310-73-2", "丁类仓库D区1号位", 25, "赵六"),
            };
        }
    }

    /// <summary>
    /// 不合规发现 — 对标 Dependency-Track 的 Finding 和 PolicyViolation。
    /// 
    /// 这是化工安全闭环的核心单元：一条规则 + 一个资产 → 一次判定结果。
    /// DT 的 Finding 生命周期: NEW → ANALYSIS → FALSE_POSITIVE / NOT_AFFECTED / VULNERABLE → REMEDIATED
    /// 化工安全生命周期:    New → Confirmed → Assigned → InProgress → Remediated → VerifiedClosed → Closed | FalsePositive
    /// </summary>
    public class ComplianceFinding
    {
        /// <summary>发现唯一ID</summary>
        public string FindingId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>关联的资产ID</summary>
        public string AssetId { get; set; } = "";

        /// <summary>关联的规则ID</summary>
        public string RuleId { get; set; } = "";

        /// <summary>法规引用（如 "GB 15603-1995 §4.2.2"）</summary>
        public string RegulationRef { get; set; } = "";

        /// <summary>发现描述（人读）</summary>
        public string Description { get; set; } = "";

        /// <summary>严重级别</summary>
        public FindingSeverity Severity { get; set; } = FindingSeverity.Medium;

        /// <summary>当前状态（状态机）</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>发现时间</summary>
        public DateTime DiscoveredAt { get; set; } = DateTime.Now;

        /// <summary>最后状态变更时间</summary>
        public DateTime LastStatusChangeAt { get; set; } = DateTime.Now;

        /// <summary>负责人</summary>
        public string Assignee { get; set; } = "";

        /// <summary>整改截止日期</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>整改建议</summary>
        public string RemediationPlan { get; set; } = "";

        /// <summary>整改验证人</summary>
        public string? VerifiedBy { get; set; }

        /// <summary>整改验证时间</summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>状态流转日志</summary>
        public List<FindingStatusLog> StatusLog { get; set; } = new();

        /// <summary>判断是否为该发现需要关注（未关闭的发现）</summary>
        public bool IsOpen => Status != FindingStatus.Closed &&
                              Status != FindingStatus.FalsePositive &&
                              Status != FindingStatus.VerifiedClosed;

        // ── 状态变更方法（状态机封装）──

        public void Confirm(string assignee, DateTime? deadline = null)
        {
            TransitionTo(FindingStatus.Confirmed, assignee);
            Assignee = assignee;
            Deadline = deadline ?? DateTime.Now.AddDays(7);
        }

        public void StartRemediation()
        {
            TransitionTo(FindingStatus.InProgress, Assignee);
        }

        public void MarkRemediated()
        {
            TransitionTo(FindingStatus.Remediated, Assignee);
        }

        public void VerifyAndClose(string verifiedBy)
        {
            TransitionTo(FindingStatus.VerifiedClosed, verifiedBy);
            VerifiedBy = verifiedBy;
            VerifiedAt = DateTime.Now;
        }

        public void Close()
        {
            TransitionTo(FindingStatus.Closed, Assignee);
        }

        public void MarkFalsePositive(string reason)
        {
            TransitionTo(FindingStatus.FalsePositive, Assignee);
            Description += $" [误报: {reason}]";
        }

        private void TransitionTo(FindingStatus newStatus, string operatorName)
        {
            StatusLog.Add(new FindingStatusLog
            {
                FromStatus = Status,
                ToStatus = newStatus,
                ChangedAt = DateTime.Now,
                ChangedBy = operatorName
            });
            Status = newStatus;
            LastStatusChangeAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 发现状态 — 对标 DT 的 Finding 状态机。
    /// 
    /// 流转规则:
    ///   New → Confirmed (确认/指派)
    ///   Confirmed → InProgress (开始整改)
    ///   InProgress → Remediated (整改完成)
    ///   Remediated → VerifiedClosed (验收通过) 或 InProgress (驳回)
    ///   VerifiedClosed → Closed (归档)
    ///   任意状态 → FalsePositive (误报)
    /// </summary>
    public enum FindingStatus
    {
        New,              // 新发现（待确认）
        Confirmed,        // 已确认（已指派责任人）
        InProgress,       // 整改中
        Remediated,       // 已整改（待验收）
        VerifiedClosed,   // 已验收关闭
        Closed,           // 已归档
        FalsePositive     // 误报
    }

    /// <summary>发现严重级别</summary>
    public enum FindingSeverity
    {
        Critical,   // 立即整改（如甲乙类同库储存）
        High,       // 7天内整改
        Medium,     // 30天内整改
        Low,        // 纳入下期计划
        Info        // 建议性提示
    }

    /// <summary>状态变更日志</summary>
    public class FindingStatusLog
    {
        public FindingStatus FromStatus { get; set; }
        public FindingStatus ToStatus { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangedBy { get; set; } = "";
    }

    /// <summary>
    /// 合规总览 — 对标 DT 的 Dashboard 指标（Portfolio Metrics）。
    /// 
    /// 提供跨所有资产的合规状态聚合视图。
    /// </summary>
    public class ComplianceOverview
    {
        /// <summary>资产总数</summary>
        public int TotalAssets { get; set; }

        /// <summary>已检查的资产数</summary>
        public int CheckedAssets { get; set; }

        /// <summary>合规资产数</summary>
        public int CompliantAssets { get; set; }

        /// <summary>不合规资产数</summary>
        public int NonCompliantAssets { get; set; }

        /// <summary>合规率</summary>
        public double ComplianceRate => TotalAssets > 0 ? (double)CompliantAssets / TotalAssets : 0;

        /// <summary>总发现数</summary>
        public int TotalFindings { get; set; }

        /// <summary>未关闭的发现数（需要关注）</summary>
        public int OpenFindings { get; set; }

        /// <summary>按严重级别分组的发现数</summary>
        public Dictionary<FindingSeverity, int> FindingsBySeverity { get; set; } = new();

        /// <summary>按状态分组的发现数</summary>
        public Dictionary<FindingStatus, int> FindingsByStatus { get; set; } = new();

        /// <summary>整改率（已关闭的发现 / 总发现）</summary>
        public double RemediationRate =>
            TotalFindings > 0
                ? (double)(FindingsByStatus.GetValueOrDefault(FindingStatus.Closed, 0) +
                           FindingsByStatus.GetValueOrDefault(FindingStatus.VerifiedClosed, 0) +
                           FindingsByStatus.GetValueOrDefault(FindingStatus.FalsePositive, 0)) / TotalFindings
                : 0;

        /// <summary>最近一次自动扫描时间</summary>
        public DateTime? LastAutoScanAt { get; set; }

        /// <summary>是否已有资产台账</summary>
        public bool HasInventory => TotalAssets > 0;
    }

    /// <summary>
    /// DeterministicRuleEngine 的 LLM 降级查询结果。
    /// 当 LLM 不可用时（熔断器打开 / 全部重试耗尽），
    /// 规则引擎使用 ChemicalSubstanceDatabase 直接回答合规查询。
    /// </summary>
    public class ComplianceFallbackResult
    {
        /// <summary>确定性回答文本</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>涉及法规编号列表（已通过数据库验证）</summary>
        public List<string> RegulationRefs { get; set; } = new();

        /// <summary>数据质量标记：DATABASE_HIT=结构化数据库命中, DICTIONARY_HIT=硬编码字典命中</summary>
        public string Quality { get; set; } = "DATABASE_HIT";
    }
}
