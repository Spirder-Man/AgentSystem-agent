# Agent1 业务能力编排层设计方案

> 日期：2026-06-23  
> 目标：将 20 个原子能力收敛为 3 个业务场景入口 + 2 个管理视图  
> 原则：不增加原子功能，只编排现有能力；每个入口对应真实的化工安全业务场景

---

## 一、现状与目标对照

```
══════════════════════════════════════════════════════════════
                      现状 → 目标 对照表
═══════════════════════════════════════════╤══════════════════
  当前的 25 个独立入口                     │  收敛后的 5 个入口
═══════════════════════════════════════════╪══════════════════
CLI:                                       │
  1. CoT推理      2. CoT流式              │
  3. ReAct推理    4. ReAct流式            │  → [隐蔽] 推理引擎按需调用
  5. Reflection   6. RAG                  │    不再作为独立菜单暴露
───────────────────────────────────────────┼──────────────────
  7. 智能对话                              │  → 【对话工作台】
                                           │     (入口1：日常交互)
───────────────────────────────────────────┼──────────────────
  8. 化工合规自查                          │
 14. 整改工单跟进                          │  → 【巡检工作台】
 17. 监管核查辅助                          │     (入口2：巡检全流程)
 15. 多模态GHS标签                         │
───────────────────────────────────────────┼──────────────────
 18. 应急响应方案                          │  → 【应急响应台】
                                           │     (入口3：事故处置)
───────────────────────────────────────────┼──────────────────
  9. RAG测试     10. 数据库验证            │
 11. 切换检索模式 12. 工具调用诊断         │  → [隐蔽] 运维诊断
 13. 合规评测集   16. 知识库增量更新       │    不再作为独立菜单暴露
 19. 知识图谱    20. 告警测试              │
───────────────────────────────────────────┼──────────────────
                                           │
API:                                       │
  /api/auth/login                          │
  /api/auth/refresh                        │  → 重新组织为
  /api/auth/logout                         │    业务域端点
  /api/compliance/check                    │
  /api/compliance/hazard/query             │
  /api/compliance/storage/compatibility    │
═══════════════════════════════════════════╧══════════════════
```

---

## 二、业务能力编排层架构

```
┌─────────────────────────────────────────────────────────┐
│                    CLI 入口层 (收敛到 5 个)               │
│  对话工作台 │ 巡检工作台 │ 应急响应台 │ 合规总览 │ 系统运维 │
└────────────┬────────────────────────────────────────────┘
             │
┌────────────┴────────────────────────────────────────────┐
│              业务能力编排层 (新建)                        │
│                                                         │
│  InspectionOrchestrator  EmergencyOrchestrator          │
│  (巡检编排器)             (应急编排器)                    │
│                                                         │
│  职责：                                                  │
│  · 管理 InspectionPlan 生命周期                         │
│  · 编排 合规检查 → 工单生成 → 报告汇总 流程              │
│  · 统一返回 InspectionReport                            │
│  · 注入安全检测 + 审计日志 + PipelineMetrics             │
└────────────┬────────────────────────────────────────────┘
             │
┌────────────┴────────────────────────────────────────────┐
│              原子能力层 (已有, 不改)                      │
│                                                         │
│  ComplianceCheck   TicketFollowup   RegulatoryAudit     │
│  EmergencyResponse  KnowledgeGraph  UnifiedDialog       │
│  ChemicalRAG       SafetyGuard      AuditService       │
│  AgentDialog(ExecuteAsync) — 6步流水线                   │
└─────────────────────────────────────────────────────────┘
```

**编排层不替代原子层**，而是在原子层之上加一层协调逻辑。原子模块继续独立可用，编排层是对它们的有机组合。

---

## 三、核心业务模型

### 3.1 InspectionPlan（巡检计划）

```csharp
namespace Agent1.Models
{
    /// <summary>
    /// 巡检计划 — 定义一次巡检的范围、检查项、责任人、时间。
    /// 化工园区常见巡检类型：日常周检、月度专项检、节前安全大检查、安监局迎检。
    /// </summary>
    public class InspectionPlan
    {
        public string PlanId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "";          // e.g. "甲类仓库周检"
        public InspectionType Type { get; set; }         // DailyWeekly / Monthly / PreHoliday / Regulatory
        public string Area { get; set; } = "";           // e.g. "甲类仓库A区"
        public string Inspector { get; set; } = "";      // 检查人
        public DateTime ScheduledDate { get; set; }
        public List<InspectionItem> Items { get; set; } = new();
        public InspectionStatus Status { get; set; } = InspectionStatus.Draft;
    }

    public enum InspectionType { DailyWeekly, Monthly, PreHoliday, Regulatory }
    public enum InspectionStatus { Draft, InProgress, Completed, Archived }

    /// <summary>
    /// 巡检项 — 计划中的单条检查项。
    /// 每个检查项映射到一种原子能力（合规检查/监管核查/多模态识别）。
    /// </summary>
    public class InspectionItem
    {
        public int ItemId { get; set; }
        public string Query { get; set; } = "";          // e.g. "苯和丙酮能否同库储存"
        public ItemCategory Category { get; set; }       // StorageCompliance / SafetyDistance / FireEquipment / ...
        public string? ExpectedRegulation { get; set; }  // e.g. "GB15603-1995"
        public InspectionItemResult? Result { get; set; } // 执行后回填
    }

    public enum ItemCategory
    {
        StorageCompliance,    // → ComplianceCheck + AgentDialog
        SafetyDistance,       // → ComplianceCheck + AgentDialog
        FireEquipment,        // → RegulatoryAudit
        EmergencyAccess,      // → RegulatoryAudit
        GhsLabel,             // → Multimodal
        Custom                // → UnifiedDialog
    }
}
```

### 3.2 InspectionRound（巡检执行实例）

```csharp
    /// <summary>
    /// 巡检轮次 — 一次计划的具体执行实例。
    /// 包含所有检查项的执行结果、开始/结束时间、汇总统计。
    /// </summary>
    public class InspectionRound
    {
        public string RoundId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string PlanId { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string ExecutedBy { get; set; } = "";
        public List<InspectionItemResult> Results { get; set; } = new();

        // ── 汇总统计 ──
        public int TotalItems => Results.Count;
        public int CompliantCount => Results.Count(r => r.IsCompliant == true);
        public int NonCompliantCount => Results.Count(r => r.IsCompliant == false);
        public int WarningCount => Results.Sum(r => r.Warnings.Count);
        public double ComplianceRate => TotalItems > 0 ? (double)CompliantCount / TotalItems : 0;
    }

    /// <summary>
    /// 巡检项结果 — 单条检查项的执行结果。
    /// 来自 CliExecutionResult（已有），附加业务上下文。
    /// </summary>
    public class InspectionItemResult
    {
        public int ItemId { get; set; }
        public bool? IsCompliant { get; set; }
        public string Conclusion { get; set; } = "";
        public string RegulationRef { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
        public List<FunctionCallRecord> ToolCalls { get; set; } = new();
        public PipelineMetrics? Metrics { get; set; }
        public List<TicketItem>? Tickets { get; set; }  // 不合规时生成的工单
    }
```

### 3.3 InspectionReport（巡检报告）

```csharp
    /// <summary>
    /// 巡检报告 — 一次巡检的正式输出文档。
    /// 可序列化为 JSON/Markdown/PDF，包含审计签名。
    /// </summary>
    public class InspectionReport
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string RoundId { get; set; } = "";
        public InspectionPlan Plan { get; set; } = null!;
        public InspectionRound Round { get; set; } = null!;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string GeneratedBy { get; set; } = "";

        // ── 报告内容 ──
        public string Summary { get; set; } = "";        // 人读摘要
        public double ComplianceRate { get; set; }
        public List<string> CriticalFindings { get; set; } = new();  // 严重不合规项
        public List<TicketItem> AllTickets { get; set; } = new();    // 本巡检产生的所有工单
        public string AuditHash { get; set; } = "";      // SHA256 签名（防篡改）
    }
}
```

---

## 四、编排器实现

### 4.1 InspectionOrchestrator（巡检编排器）

```csharp
namespace Agent1.Services
{
    /// <summary>
    /// 巡检编排器 — 化工安全巡检业务的核心协调者。
    /// 
    /// 编排流程：
    ///   1. 加载 InspectionPlan → 遍历 InspectionItem
    ///   2. 根据 ItemCategory 路由到对应的原子能力
    ///   3. 收集 CliExecutionResult → 提取合规判定 → 生成工单
    ///   4. 汇总 InspectionRound → 生成 InspectionReport
    ///   5. 每个检查项自动注入 SafetyGuardService + AuditService
    ///   6. 返回完整的 PipelineMetrics（7步耗时 + TraceId）
    /// </summary>
    public class InspectionOrchestrator
    {
        private readonly AgentDialog _agentDialog;
        private readonly IKnowledgeBaseService _kb;
        private readonly IAuditService _audit;

        public InspectionOrchestrator(AgentDialog agentDialog, IKnowledgeBaseService kb, IAuditService audit)
        {
            _agentDialog = agentDialog;
            _kb = kb;
            _audit = audit;
        }

        /// <summary>
        /// 执行完整巡检计划
        /// </summary>
        public async Task<InspectionRound> ExecutePlanAsync(InspectionPlan plan, string executedBy)
        {
            var round = new InspectionRound
            {
                PlanId = plan.PlanId,
                StartedAt = DateTime.Now,
                ExecutedBy = executedBy
            };

            plan.Status = InspectionStatus.InProgress;

            foreach (var item in plan.Items)
            {
                // 每个检查项独立执行（共享同一 Session 但独立的结果对象）
                var result = await ExecuteInspectionItemAsync(item);
                item.Result = result;
                round.Results.Add(result);

                // 不合规项自动生成工单
                if (result.IsCompliant == false)
                {
                    result.Tickets = await GenerateRectificationTicketAsync(item, result);
                }
            }

            round.CompletedAt = DateTime.Now;
            plan.Status = InspectionStatus.Completed;

            await _audit.LogOperationAsync(executedBy, "InspectionComplete",
                $"巡检完成: {plan.Name} | {round.CompliantCount}/{round.TotalItems}合规 | 工单={round.Results.Sum(r => r.Tickets?.Count ?? 0)}个");

            return round;
        }

        /// <summary>
        /// 执行单条检查项 — 路由到合适的原子能力
        /// </summary>
        private async Task<InspectionItemResult> ExecuteInspectionItemAsync(InspectionItem item)
        {
            var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);
            var execResult = await _agentDialog.ExecuteAsync(item.Query, session);

            return new InspectionItemResult
            {
                ItemId = item.ItemId,
                IsCompliant = ParseCompliance(execResult.DisplayOutput),
                Conclusion = execResult.DisplayOutput,
                RegulationRef = ExtractRegulationRef(execResult.DisplayOutput),
                Warnings = execResult.Warnings,
                ToolCalls = execResult.ToolCalls,
                Metrics = execResult.StructuredResult as PipelineMetrics
            };
        }

        /// <summary>
        /// 生成 InspectionReport
        /// </summary>
        public InspectionReport GenerateReport(InspectionPlan plan, InspectionRound round, string generatedBy)
        {
            return new InspectionReport
            {
                RoundId = round.RoundId,
                Plan = plan,
                Round = round,
                GeneratedBy = generatedBy,
                ComplianceRate = round.ComplianceRate,
                Summary = GenerateSummary(plan, round),
                CriticalFindings = round.Results
                    .Where(r => r.IsCompliant == false && r.Warnings.Count > 0)
                    .Select(r => $"[{r.ItemId}] {r.Conclusion.Truncate(100)}")
                    .ToList(),
                AllTickets = round.Results.SelectMany(r => r.Tickets ?? new()).ToList(),
                AuditHash = ComputeAuditHash(round)
            };
        }

        // ── 辅助方法 ──
        private static bool? ParseCompliance(string output)
        {
            if (output.Contains("【合规判断】是") || output.Contains("合规判断】是")) return true;
            if (output.Contains("【合规判断】否") || output.Contains("合规判断】否")) return false;
            return null; // 无法判定
        }

        private static string ExtractRegulationRef(string output) { /* 提取 GB 编号 */ return ""; }
        private async Task<List<TicketItem>> GenerateRectificationTicketAsync(InspectionItem item, InspectionItemResult result) { return new(); }
        private static string GenerateSummary(InspectionPlan plan, InspectionRound round) => $"巡检 {plan.Name} 完成: {round.ComplianceRate:P1} 合规率";
        private static string ComputeAuditHash(InspectionRound round) => ""; // SHA256
    }
}
```

---

## 五、CLI 菜单收敛方案

### 收敛前（20 个菜单）

```
   0. 退出
   1. CoT推理     2. CoT流式     3. ReAct推理    4. ReAct流式
   5. Reflection  6. RAG         7. 智能对话     8. 化工合规自查
   9. RAG测试    10. 数据库验证  11. 切换检索    12. 工具调用诊断
  13. 合规评测   14. 整改工单    15. GHS标签     16. 知识库增量
  17. 监管核查   18. 应急响应    19. 知识图谱    20. 告警测试
```

### 收敛后（5 个菜单 + 内部子选项）

```
═══════════════════════════════════════
        Agent1 化工安全 AI 工作台
═══════════════════════════════════════

  1. 💬 对话工作台  — 智能问答 + 推理模式切换
  2. 🔍 巡检工作台  — 日常检查 / 专项检查 / 监管核查 / 报告管理
  3. 🚨 应急响应台  — 事故场景模拟 / 应急方案生成
  4. 📊 合规总览    — 合规率看板 / 待整改清单 / 历史记录
  5. ⚙️  系统运维    — 数据库验证 / 知识库更新 / 模式切换 / 告警测试

  0. 退出
```

**菜单 2（巡检工作台）内部子选项**：

```
请选择巡检模式:
  1. 新建巡检计划
  2. 从已有计划执行巡检
  3. 快速单次检查（不创建计划）
  4. 查看巡检报告
  5. 整改工单管理
```

**菜单 4（合规总览）内部视图**：

```
  1. 按区域查看合规率
  2. 待整改清单（按优先级）
  3. 历史巡检记录
  4. 导出合规报告
```

---

## 六、API 端点重组

### 收敛前（散落）

```
/auth/login, /auth/refresh, /auth/logout
/compliance/check, /compliance/hazard/query, /compliance/storage/compatibility
```

### 收敛后（按业务域组织）

```
══════════════════════════════════════════════════════
  Controller          端点                        对应原子能力
══════════════════════════════════════════════════════
  AuthController      (保持不变)
    POST /api/auth/login
    POST /api/auth/refresh
    POST /api/auth/logout
──────────────────────────────────────────────────────
  InspectionController  [新建]
    POST   /api/inspection/plans           创建巡检计划
    GET    /api/inspection/plans           列出所有计划
    GET    /api/inspection/plans/{id}      查看计划详情
    POST   /api/inspection/plans/{id}/execute  执行计划
    GET    /api/inspection/rounds/{id}     查看巡检轮次结果
    GET    /api/inspection/reports/{id}    查看/导出报告
──────────────────────────────────────────────────────
  ComplianceController  (扩展)
    POST   /api/compliance/check           [已有] 单次合规检查
    POST   /api/compliance/hazard/query    [已有] 危化品查询
    POST   /api/compliance/storage         [已有] 储存兼容性
    GET    /api/compliance/summary         [新建] 合规状态总览
    GET    /api/compliance/summary?area=   按区域筛选
──────────────────────────────────────────────────────
  TicketController  [新建]
    GET    /api/tickets                    待整改工单列表
    GET    /api/tickets/{id}               工单详情
    PUT    /api/tickets/{id}/status        更新工单状态
    POST   /api/tickets/{id}/verify        验证整改完成
──────────────────────────────────────────────────────
  EmergencyController  [新建]
    POST   /api/emergency/plan             应急方案生成
    POST   /api/emergency/scenario         事故场景模拟
──────────────────────────────────────────────────────
  AdminController  [新建]
    GET    /api/admin/health               健康检查
    POST   /api/admin/knowledgebase/update 知识库增量更新
    POST   /api/admin/eval/run             运行评测集
    POST   /api/admin/alert/test           告警通道测试
```

---

## 七、实施计划

### Phase 1：业务模型落地（新建 4 个文件）

| 文件 | 内容 |
|------|------|
| `Models/InspectionModels.cs` | InspectionPlan / InspectionRound / InspectionReport / 枚举 |
| `Services/Orchestration/InspectionOrchestrator.cs` | 巡检编排器 |
| `Commands/InspectionCommands.cs` | 巡检工作台 CLI 入口 |
| `Commands/DashboardCommands.cs` | 合规总览 CLI 入口 |

### Phase 2：CLI 收敛（修改 3 个文件）

| 文件 | 改动 |
|------|------|
| `Program.cs` | 菜单从 20 项收敛到 5 项，每个项打开子菜单 |
| `Commands/MenuCommands.cs` | 新增 InspectionWorkbenchCommand / DashboardCommand；推理模式改为子选项 |
| `Models/ModuleType.cs` | 新增 InspectionWorkbench = 13（可选） |

### Phase 3：API 扩展（新建 3 个 Controller）

| 文件 | 内容 |
|------|------|
| `Controllers/InspectionController.cs` | 巡检计划 CRUD + 执行 + 报告 |
| `Controllers/TicketController.cs` | 工单状态管理 |
| `Controllers/EmergencyController.cs` | 应急方案 API |

### Phase 4：报告导出 + 合规总览

| 文件 | 内容 |
|------|------|
| `Services/Orchestration/ReportGenerator.cs` | Markdown/JSON 报告生成 |
| `Services/Orchestration/ComplianceDashboard.cs` | 合规率聚合查询 |

---

## 八、不改动的部分

以下原子能力**保持独立可用**，不被编排层替代——它们仍然可以通过 API 直接调用或被编排层内部引用：

- `AgentDialog.ExecuteAsync` — 6步流水线（编排层通过它执行单条检查）
- `SafetyGuardService` — 安全双防线（编排层自动注入）
- `AuditService` — 审计日志（编排层自动记录）
- `ChemicalRAG` — 知识库检索（编排层按需调用）
- `PipelineMetrics` — 7步耗时采集（每个检查项独立采集）
- 推理模块（CoT/ReAct/Reflection）— 通过 UnifiedDialog 统一入口调用

---

> **文档版本**：v1.0  
> **下一步**：确认方案后进入 Phase 1 实施
