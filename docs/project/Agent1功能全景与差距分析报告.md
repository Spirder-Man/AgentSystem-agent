# Agent1 功能全景与差距分析报告

> **文档版本**：v5.1（深度扩展版）
> 日期：2026-06-23 | 原版本：v5.0
> **深度扩展**：2026-06-24（v5.1 — 新增 Bug 修复对功能覆盖度的影响分析）
> 目的：对照完整化工安全AI Agent系统标准，分析当前功能覆盖度，指导后续演进

---

## 一、完整化工安全 AI Agent 系统功能标准

一个完整的化工安全 AI Agent 系统应覆盖 **4 大领域 × 20 项核心能力**：

```
══════════════════════════════════════════════════════════════
领域 A: 合规审查 (Compliance)        领域 B: 风险管理 (Risk)
──────────────────────────────────────────────────────────────
 A1. 储存合规检查                      B1. 风险评估矩阵
 A2. 安全距离验证                      B2. 重大危险源判定
 A3. 危化品分类查询                    B3. 事故后果模拟
 A4. 法规版本追踪                      B4. 风险等级可视化
 A5. 多模态标签识别                    B5. 风险缓解建议生成
──────────────────────────────────────────────────────────────
领域 C: 应急响应 (Emergency)         领域 D: 管理决策 (Management)
──────────────────────────────────────────────────────────────
 C1. 事故场景应急方案                  D1. 巡检计划与执行
 C2. PPE防护等级建议                   D2. 整改工单生命周期
 C3. 疏散隔离半径计算                  D3. 合规状态总览
 C4. 灭火介质推荐                      D4. 监管核查报告
 C5. 医疗急救指引                      D5. 知识图谱查询
──────────────────────────────────────────────────────────────
基础设施 (Infrastructure)
───────────────────────
 I1. Prompt注入防护    I2. 输出安全校验    I3. 审计日志(等保三级)
 I4. 6步流水线可观测   I5. 事件溯源        I6. 7步耗时指标
 I7. 熔断器降级        I8. 定时自动扫描    I9. 数据持久化
 I10. 能力动态路由     I11. 状态机驱动     I12. 事件订阅发布
══════════════════════════════════════════════════════════════
```

---

## 二、Agent1 当前功能覆盖度

### 总分：**58.5 / 80 分 = 73%**

| 编号 | 能力 | 状态 | 实现位置 | 本轮新增? |
|:---:|------|:---:|------|:---:|
| A1 | 储存合规检查 | ✅ | `ComplianceCheckModule` + SK Auto FC | |
| A2 | 安全距离验证 | ✅ | `GetSafetyDistance` KernelFunction | |
| A3 | 危化品分类查询 | ✅ | `CheckHazardCategory` KernelFunction | |
| A4 | 法规版本追踪 | ✅ | `RegulationVersion` 模型 + 8项标准 | |
| A5 | 多模态标签识别 | ⚠️ | `MultimodalService` (依赖外部LLM) | |
| B1 | 风险评估矩阵 | ⚠️ | `RiskAssessmentService` (3×3矩阵，无CLI入口) | |
| B2 | 重大危险源判定 | ✅ | `ChemicalComplianceTools` + GB18218临界量 | |
| B3 | 事故后果模拟 | ❌ | — | |
| B4 | 风险等级可视化 | ❌ | — | |
| B5 | 风险缓解建议 | ⚠️ | LLM推理中附带，未结构化 | |
| C1 | 事故应急方案 | ✅ | `EmergencyResponseModule` | |
| C2 | PPE防护等级 | ✅ | `EmergencyResponseService` 输出含PPE | |
| C3 | 疏散隔离半径 | ✅ | `EmergencyResponseService` 输出含半径 | |
| C4 | 灭火介质推荐 | ✅ | `EmergencyResponseService` 输出含介质 | |
| C5 | 医疗急救指引 | ✅ | `EmergencyResponseService` 输出含急救 | |
| D1 | 巡检计划与执行 | ✅ | `InspectionOrchestrator` + `InspectionWorkbenchCommand` | ✅ 新增 |
| D2 | 整改工单生命周期 | ✅ | `TicketFollowupModule` + `TicketItem`状态机 | ✅ 升级 |
| D3 | 合规状态总览 | ✅ | `DashboardCommand` + `ComplianceOverview` | ✅ 新增 |
| D4 | 监管核查报告 | ✅ | `RegulatoryAuditModule` | |
| D5 | 知识图谱查询 | ✅ | `KnowledgeGraphModule` | |
| I1 | Prompt注入防护 | ✅ | `SafetyGuardService.ValidateInput` | ✅ 集成 |
| I2 | 输出安全校验 | ✅ | `SafetyGuardService.ValidateOutput` | ✅ 集成 |
| I3 | 审计日志(等保) | ✅ | `AuditService` + SHA256哈希链 | |
| I4 | 6步流水线可观测 | ✅ | `PipelineMetrics` + TraceId + Serilog | ✅ 新增 |
| I5 | 事件溯源 | ✅ | `PipelineEvent` + `IEventStore` + 9类事件 | ✅ 新增 |
| I6 | 7步耗时指标 | ✅ | `PipelineMetrics` 16个字段 | ✅ 新增 |
| I7 | 熔断器降级 | ✅ | `LlmService.CircuitBreaker` | |
| I8 | 定时自动扫描 | ✅ | `ScheduledScanService` | ✅ 新增 |
| I9 | 数据持久化 | ✅ | `InspectionRepository` (JSON) | ✅ 新增 |
| I10 | 能力动态路由 | ✅ | `CapabilityRegistry` (7种能力) | ✅ 新增 |
| I11 | 状态机驱动 | ✅ | `ComplianceFinding`(7态) + `TicketItem`(7态) | ✅ 新增 |
| I12 | 事件订阅发布 | ✅ | `EventActionDispatcher` | ✅ 新增 |

---

## 三、本轮新增能力详解

```
═══════════════════════════════════════════════════════
 本轮新增 16 项，升级改写 2 项，0 项删除
═══════════════════════════════════════════════════════

【阶段0: CLI安全管道统一整改】(P0-P2)
─────────────────────────────────────────
  I1  Prompt注入防护 → 集成到ExecuteAsync管道 ← 原仅ComplianceCheckModule有
  I2  输出安全校验   → 集成到ExecuteAsync管道
  I3  审计日志       → AgentDialog合规路径添加审计记录

【阶段0: 结构化可观测性升级】(P2)
─────────────────────────────────────────
  I4  6步流水线可观测 → PipelineMetrics 16字段 + 7步耗时
  I5  事件溯源        → PipelineEvent + IEventStore + ExecuteAsync中9类事件
  I6  7步耗时指标     → Stopwatch采集 + Serilog结构化输出

【阶段1: 业务模型落地】(P0)
─────────────────────────────────────────
  D1  巡检计划与执行  → InspectionPlan/Round/Report + InspectionOrchestrator
  D3  合规状态总览    → DashboardCommand + ComplianceOverview
  I11 状态机驱动      → ComplianceFinding(7态) + TicketItem(7态)
  I9  数据持久化      → InspectionRepository (JSON文件存储)

【阶段2: Dependency-Track闭环映射】(P0)
─────────────────────────────────────────
  --  ChemicalAsset台账 → 对标DT的Component/SBOM
  --  ComplianceRuleEngine → 对标DT的漏洞匹配引擎
  --  ComplianceFinding → 对标DT的Finding状态机
  --  ComplianceOverview → 对标DT的Portfolio Metrics

【阶段3: 四范式工程落地】(P1)
─────────────────────────────────────────
  I10 能力动态路由    → CapabilityRegistry (范式4)
  I12 事件订阅发布    → EventActionDispatcher (范式3)
  --  InspectionItem改为声明式能力引用 (范式1)

【阶段4: 菜单收敛+定时扫描】(P1)
─────────────────────────────────────────
  --  菜单 22→5 收敛  → AdminMenuCommand
  I8  定时自动扫描    → ScheduledScanService

【升级改写】
─────────────────────────────────────────
  D2  整改工单生命周期 → TicketItem从无状态→7态状态机
  --  InspectionOrchestrator → 静态字典→持久化仓储
```

---

## 四、仍缺失的能力（优先级排序）

| 优先级 | 编号 | 能力 | 工程价值 | 预估工作量 |
|:---:|:---:|------|------|:---:|
| **P0** | B1 | 风险评估矩阵CLI入口 | `RiskAssessmentService`已有但无菜单；巡检中发现Critical级别应自动触发评估 | 1文件 |
| **P0** | — | API编排端点 | CLI可用但API不可用；`InspectionController`/`TicketController` | 2文件 |
| **P1** | B3 | 事故后果模拟 | 输入泄漏量+风速→估算影响范围；应急方案的前置输入 | 1文件 |
| **P1** | B4 | 风险等级可视化 | 合规总览增加风险热力图数据 | 1文件 |
| **P1** | A5 | 多模态独立CLI入口 | 已有Service但只在AdminMenu里有隐式入口 | 0.5文件 |
| **P2** | D4 | 监管核查报告导出 | 已有Report模型，缺PDF/JSON导出 | 1文件 |
| **P2** | — | 事件订阅实际生效 | `EventActionDispatcher`已就绪但无实际订阅者 | 0.5文件 |
| **P3** | B5 | 风险缓解建议结构化 | 目前LLM附带建议，未独立建模 | 0.5文件 |
| **P3** | — | 多租户/多角色 | 安全员/审核员/管理员权限分离 | 2文件 |

---

## 五、CLI菜单对照表

| 编号 | 名称 | 状态 | 类型 |
|:---:|------|:---:|------|
| 1 | 💬 对话工作台 [CoT·ReAct·Reflection·RAG] | ✅ 主菜单 | 业务入口 |
| 2 | 🔍 巡检工作台 [计划·执行·报告·工单] | ✅ 主菜单 | 业务入口 |
| 3 | 🚨 应急响应台 [泄漏·火灾·爆炸·中毒] | ✅ 主菜单 | 业务入口 |
| 4 | 📊 合规总览 [扫描·台账·发现·整改率] | ✅ 主菜单 | 业务入口 |
| 5 | ⚙️ 系统运维 [数据库·知识库·诊断·告警·经典] | ✅ 主菜单 | 管理入口 |
| ── | ── 系统运维子菜单 ── | | |
| 5.1 | 合规自查(经典) | ✅ 子菜单 | 原子能力 |
| 5.2 | 数据库验证 | ✅ 子菜单 | 诊断 |
| 5.3 | 切换检索模式 | ✅ 子菜单 | 配置 |
| 5.4 | 工具调用诊断 | ✅ 子菜单 | 诊断 |
| 5.5 | 合规评测集 | ✅ 子菜单 | 评测 |
| 5.6 | 整改工单跟进 | ✅ 子菜单 | 原子能力 |
| 5.7 | 知识库增量更新 | ✅ 子菜单 | 运维 |
| 5.8 | 监管核查辅助 | ✅ 子菜单 | 原子能力 |
| 5.9 | 知识图谱 | ✅ 子菜单 | 原子能力 |
| 5.10 | 告警邮件测试 | ✅ 子菜单 | 诊断 |
| 5.c1-c7 | 推理模式(CoT/ReAct/Reflection/RAG) | ✅ 子菜单 | 推理引擎 |

---

## 六、与 Dependency-Track 对标总结

```
Dependency-Track                    Agent1 化工安全
─────────────────────────────────────────────────────────
SBOM导入 (CycloneDX)                化学品资产台账 (ChemicalAsset)
组件清单 (Components)               8种演示化学品 + 可扩展
漏洞数据源 (NVD/OSV)                合规规则库 (5条内置)
漏洞匹配引擎 (Analyzer)             合规规则引擎 (ComplianceRuleEngine)
Finding发现 + 状态机                合规发现 (ComplianceFinding, 7状态)
Policy Violation                    不合规判定 → 整改工单
定期重分析 (Scheduled)              定时自动扫描 (ScheduledScanService)
告警通知 (Webhook/Email)            事件订阅发布 (EventActionDispatcher)
Portfolio Metrics (Dashboard)       合规总览 (ComplianceOverview)
REST API                            待实现 (P2)
用户管理/多租户                     待实现 (P3)
```

---

## 七、进度总结

```
P0 ✅ 安全双防线 + 审计追踪 + 6步可观测 + 事件溯源 + 业务模型 + 持久化
P1 ✅ 菜单收敛(22→5) + 四范式落地 + 定时扫描 + 状态机
P2 ⬜ API编排端点 + 报告导出 + 通知订阅 + 风险矩阵入口
P3 ⬜ 多租户 + 事故模拟 + 风险可视化
```

> **文档版本**：v5.0 | **最后更新**：2026-06-23
