using System;
using System.Collections.Generic;
using System.Linq;
using Agent1.Models;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// 巡检数据仓储 — P0 持久化层。
    /// 
    /// 存储巡检计划、资产台账、合规发现。
    /// 所有数据以 JSON 文件持久化到 Data/ 目录。
    /// 接口设计允许未来替换为 PostgreSQL 实现。
    /// </summary>
    public class InspectionRepository
    {
        private readonly InspectionStoreRepository _store;

        // 内存缓存（从文件加载后驻留）
        private InspectionStore _cache = new();

        public InspectionRepository()
        {
            _store = new InspectionStoreRepository("inspection-store.json");
            _cache = _store.Load();
        }

        // ═══════════════════════════════════════
        // 巡检计划 CRUD
        // ═══════════════════════════════════════

        public List<InspectionPlan> GetAllPlans() => _cache.Plans;

        public InspectionPlan? GetPlan(string planId)
            => _cache.Plans.FirstOrDefault(p => p.PlanId == planId);

        public void SavePlan(InspectionPlan plan)
        {
            var existing = _cache.Plans.FindIndex(p => p.PlanId == plan.PlanId);
            if (existing >= 0)
                _cache.Plans[existing] = plan;
            else
                _cache.Plans.Add(plan);
            Save();
        }

        /// <summary>删除巡检计划，返回是否成功</summary>
        public bool DeletePlan(string planId)
        {
            var removed = _cache.Plans.RemoveAll(p => p.PlanId == planId);
            if (removed > 0) Save();
            return removed > 0;
        }

        // ═══════════════════════════════════════
        // 巡检轮次
        // ═══════════════════════════════════════

        public List<InspectionRound> GetAllRounds() => _cache.Rounds;

        public InspectionRound? GetRound(string roundId)
            => _cache.Rounds.FirstOrDefault(r => r.RoundId == roundId);

        public void SaveRound(InspectionRound round)
        {
            var existing = _cache.Rounds.FindIndex(r => r.RoundId == round.RoundId);
            if (existing >= 0)
                _cache.Rounds[existing] = round;
            else
                _cache.Rounds.Add(round);
            Save();
        }

        /// <summary>查找某个计划的所有轮次</summary>
        public List<InspectionRound> GetRoundsByPlan(string planId)
            => _cache.Rounds.Where(r => r.PlanId == planId).ToList();

        // ═══════════════════════════════════════
        // 化学品资产台账
        // ═══════════════════════════════════════

        public List<ChemicalAsset> GetAllAssets()
        {
            if (_cache.Assets.Count == 0)
            {
                // 首次启动加载演示数据
                _cache.Assets = ChemicalAsset.CreateDemoInventory();
                Save();
            }
            return _cache.Assets;
        }

        public ChemicalAsset? GetAsset(string assetId)
            => _cache.Assets.FirstOrDefault(a => a.AssetId == assetId);

        public void SaveAsset(ChemicalAsset asset)
        {
            var existing = _cache.Assets.FindIndex(a => a.AssetId == asset.AssetId);
            if (existing >= 0)
                _cache.Assets[existing] = asset;
            else
                _cache.Assets.Add(asset);
            Save();
        }

        public void SaveAssets(List<ChemicalAsset> assets)
        {
            _cache.Assets = assets;
            Save();
        }

        // ═══════════════════════════════════════
        // 合规发现
        // ═══════════════════════════════════════

        public List<ComplianceFinding> GetAllFindings() => _cache.Findings;

        public List<ComplianceFinding> GetOpenFindings()
            => _cache.Findings.Where(f => f.IsOpen).ToList();

        public void SaveFinding(ComplianceFinding finding)
        {
            var existing = _cache.Findings.FindIndex(f => f.FindingId == finding.FindingId);
            if (existing >= 0)
                _cache.Findings[existing] = finding;
            else
                _cache.Findings.Add(finding);
            Save();
        }

        public void SaveFindings(List<ComplianceFinding> findings)
        {
            foreach (var f in findings)
            {
                var existing = _cache.Findings.FindIndex(x => x.FindingId == f.FindingId);
                if (existing >= 0)
                    _cache.Findings[existing] = f;
                else
                    _cache.Findings.Add(f);
            }
            Save();
        }

        // ═══════════════════════════════════════
        // 扫描时间
        // ═══════════════════════════════════════

        public DateTime? GetLastScanTime() => _cache.LastScanTime;

        public void SetLastScanTime(DateTime time)
        {
            _cache.LastScanTime = time;
            Save();
        }

        /// <summary>获取数据存储统计信息</summary>
        public object GetStats() => new
        {
            Plans = _cache.Plans.Count,
            Rounds = _cache.Rounds.Count,
            Assets = _cache.Assets.Count,
            Findings = _cache.Findings.Count,
            OpenFindings = _cache.Findings.Count(f => f.IsOpen),
            LastScan = _cache.LastScanTime?.ToString("yyyy-MM-dd HH:mm")
        };

        // ── 内部 ──

        private void Save() => _store.Persist(_cache);
    }

    /// <summary>持久化存储结构</summary>
    public class InspectionStore
    {
        public List<InspectionPlan> Plans { get; set; } = new();
        public List<InspectionRound> Rounds { get; set; } = new();
        public List<ChemicalAsset> Assets { get; set; } = new();
        public List<ComplianceFinding> Findings { get; set; } = new();
        public DateTime? LastScanTime { get; set; }
    }

    /// <summary>具体化的泛型仓储（C# 泛型约束要求）</summary>
    internal class InspectionStoreRepository : JsonFileRepository<InspectionStore>
    {
        public InspectionStoreRepository(string fileName) : base(fileName) { }
        public new InspectionStore Load() => base.Load();
        public void Persist(InspectionStore data) => SaveData(data);
    }
}
