using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 原子能力声明 — 每个模块向编排层暴露"我能干什么"。
    /// 
    /// 范式 4 核心：编排层不依赖具体模块类型，
    /// 只依赖能力名称 + 输入输出契约。
    /// 未来将模块重构为微服务，只需改注册表，编排逻辑不变。
    /// </summary>
    public class Capability
    {
        /// <summary>能力唯一名称（如 "storage-compliance-check"）</summary>
        public string Name { get; set; } = "";

        /// <summary>能力描述（人读）</summary>
        public string Description { get; set; } = "";

        /// <summary>关联的法规引用（如 "GB 15603-1995"）</summary>
        public string? RegulationRef { get; set; }

        /// <summary>输入类型提示（如 "化学品名称+储存位置"）</summary>
        public string InputHint { get; set; } = "";

        /// <summary>执行该能力的处理器</summary>
        public Func<string, SessionContext, Task<CliExecutionResult>>? Handler { get; set; }
    }

    /// <summary>
    /// 能力注册表 — 范式 4 的路由中枢。
    /// 
    /// 对照 Dependency-Track: 每个 Analyzer 注册自己能处理什么类型的漏洞。
    /// 化工映射: 每个模块注册自己能执行什么类型的合规检查。
    /// </summary>
    public class CapabilityRegistry
    {
        private readonly Dictionary<string, Capability> _capabilities = new();
        private readonly AgentDialog _agentDialog;
        private readonly IModuleFactory _moduleFactory;

        public CapabilityRegistry(AgentDialog agentDialog, IModuleFactory moduleFactory)
        {
            _agentDialog = agentDialog;
            _moduleFactory = moduleFactory;
            RegisterBuiltInCapabilities();
        }

        /// <summary>
        /// 注册内置能力 — 每个模块声明自己提供什么能力。
        /// 
        /// 注意：这里不引用具体模块类，只引用 IInferenceModule 接口。
        /// 模块替换时只改注册表这一处。
        /// </summary>
        private void RegisterBuiltInCapabilities()
        {
            // ── 合规检查类 ──
            Register(new Capability
            {
                Name = "storage-compliance",
                Description = "化学品储存合规检查（禁忌物料、同库储存）",
                RegulationRef = "GB 15603-1995",
                InputHint = "化学品A名称 + 化学品B名称 + 储存位置",
                Handler = async (query, session) =>
                    await _agentDialog.ExecuteAsync(query, session)
            });

            Register(new Capability
            {
                Name = "safety-distance",
                Description = "安全距离合规检查（储罐间距、消防通道宽度）",
                RegulationRef = "GB 50160 / GB 50016",
                InputHint = "设施类型 + 当前距离 + 周围环境",
                Handler = async (query, session) =>
                    await _agentDialog.ExecuteAsync(query, session)
            });

            Register(new Capability
            {
                Name = "hazard-category",
                Description = "危化品危险类别查询（GB 30000 分类）",
                RegulationRef = "GB 30000-2013",
                InputHint = "化学品名称",
                Handler = async (query, session) =>
                    await _agentDialog.ExecuteAsync(query, session)
            });

            // ── 监管类 ──
            Register(new Capability
            {
                Name = "regulatory-audit",
                Description = "监管核查辅助（逐条比对法规条款）",
                RegulationRef = "多法规",
                InputHint = "核查项描述",
                Handler = async (query, session) =>
                {
                    // 委托给 RegulatoryAuditModule（通过 ModuleFactory）
                    var module = _moduleFactory.CreateModule(ModuleType.RegulatoryAudit);
                    return await module.RunWithResultAsync(query);
                }
            });

            // ── 应急类 ──
            Register(new Capability
            {
                Name = "emergency-plan",
                Description = "应急响应方案生成（泄漏/火灾/爆炸/中毒）",
                RegulationRef = "ERG 指南 / AQ/T 3043",
                InputHint = "化学品名称 + 事故类型 + 泄漏量 + 环境参数",
                Handler = async (query, session) =>
                {
                    var module = _moduleFactory.CreateModule(ModuleType.EmergencyResponse);
                    return await module.RunWithResultAsync(query);
                }
            });

            // ── 知识类 ──
            Register(new Capability
            {
                Name = "knowledge-graph",
                Description = "知识图谱查询（化学品-法规-事故关联）",
                InputHint = "化学品名称",
                Handler = async (query, session) =>
                {
                    var module = _moduleFactory.CreateModule(ModuleType.KnowledgeGraph);
                    return await module.RunWithResultAsync(query);
                }
            });

            // ── 多模态类 ──
            Register(new Capability
            {
                Name = "ghs-label-check",
                Description = "GHS 标签合规识别",
                RegulationRef = "GB 15258-2009",
                InputHint = "图片路径",
                Handler = async (query, session) =>
                    await _agentDialog.ExecuteAsync(query, session)
            });
        }

        /// <summary>注册一个能力</summary>
        public void Register(Capability capability)
        {
            _capabilities[capability.Name] = capability;
            Serilog.Log.Information("[CapabilityRegistry] 注册能力: {Name} → {Description}",
                capability.Name, capability.Description);
        }

        /// <summary>按名称查找能力</summary>
        public Capability? Get(string name)
            => _capabilities.TryGetValue(name, out var cap) ? cap : null;

        /// <summary>获取所有已注册能力</summary>
        public IReadOnlyList<Capability> GetAll()
            => _capabilities.Values.ToList().AsReadOnly();

        /// <summary>找出匹配输入的能力（模糊匹配）</summary>
        public List<Capability> MatchByInput(string query)
        {
            var lower = query.ToLower();
            return _capabilities.Values
                .Where(c =>
                    c.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                    c.Description.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                    (c.RegulationRef?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    lower.Contains("储存") && c.Name == "storage-compliance" ||
                    lower.Contains("距离") && c.Name == "safety-distance" ||
                    lower.Contains("应急") && c.Name == "emergency-plan" ||
                    lower.Contains("标签") && c.Name == "ghs-label-check")
                .ToList();
        }

        /// <summary>
        /// 执行指定能力 — 编排层的统一入口。
        /// 每个调用走完整的 6 步流水线（AgentDialog.ExecuteAsync），
        /// 自动获得 SafetyGuardService + PipelineMetrics + AuditService。
        /// </summary>
        public async Task<CliExecutionResult> ExecuteAsync(
            string capabilityName, string query, SessionContext session)
        {
            var cap = Get(capabilityName)
                ?? throw new InvalidOperationException($"未注册的能力: {capabilityName}");

            if (cap.Handler == null)
                throw new InvalidOperationException($"能力 {capabilityName} 未绑定 Handler");

            Serilog.Log.Information("[CapabilityRegistry] 执行能力: {Name} | 查询={Query}",
                capabilityName, query.Truncate(60));

            return await cap.Handler(query, session);
        }
    }
}
