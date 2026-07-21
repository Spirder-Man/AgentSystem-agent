# Agent1 项目全景分析：现状、目标与整改进度

> 生成日期：2026-06-16
> 最后核查：2026-07-20（第四轮核查版，前端 E2E/单元测试 零实施、后端测试 665+63 条、覆盖率 21%）
> 项目：化工园区危化品合规审核 AI Agent (.NET 8 + Semantic Kernel + GPU + Vue 3)
> 整体完成度：~85%（P0-P2 全部清完，测试体系为当前最大短板）

---

## 一、当前项目的功能入口和架构层次

### 1.1 三通道入口体系

```
┌──────────────────────────────────────────────────────────────────┐
│                         Agent1 项目                               │
├──────────────────────┬───────────────────────┬───────────────────┤
│  入口1：Console 控制台│  入口2：REST API       │  入口3：Web SPA    │
│  Program.cs           │  Agent1.Api/Program.cs│  agent1-web/       │
├──────────────────────┼───────────────────────┼───────────────────┤
│  17项交互菜单         │  15个 Controller       │  17 个页面组件     │
│  ├─ 1-7: 通用推理模块 │  ├─ AuthController     │  ├─ LoginPage      │
│  │  CoT/ReAct/        │  ├─ ComplianceController│  ├─ DashboardPage  │
│  │  Reflection/RAG    │  ├─ InspectionController│  ├─ ComplianceCheck│
│  ├─ 8-14: 化工功能     │  ├─ TicketsController  │  ├─ TicketList/    │
│  │  合规/工单/RAG/    │  ├─ KnowledgeBase      │  │   Detail         │
│  │  诊断/评测/多模态   │  │   Controller         │  ├─ AssetList/     │
│  ├─ 15: 多模态视觉分析 │  ├─ KnowledgeGraph     │  │   Detail         │
│  ├─ 16: 知识库增量更新 │  │   Controller         │  ├─ Inspection*(6) │
│  └─ 17: 监管核查辅助   │  ├─ EmergencyController│  ├─ AuditLogPage   │
│                       │  ├─ DashboardController │  ├─ SystemStatus   │
│  命令模式重构          │  ├─ AuditController    │  └─ ForbiddenPage  │
│  (IMenuCommand 字典)   │  └─ HealthController   │                   │
│                       │                        │  Vue 3 + Vite 5   │
│                       │  5个 Middleware         │  Element Plus     │
│                       │  ├─ GlobalException     │  Pinia (8 Stores) │
│                       │  ├─ RateLimiting        │  MSW Mock         │
│                       │  ├─ RequestId           │  ECharts 图表     │
│                       │  ├─ RequestMetrics      │  Playwright (规划)│
│                       │  └─ TokenBlacklist      │                   │
└──────────────────────┴───────────────────────┴───────────────────┘
```

### 1.2 六层架构层次

| 层 | 职责 | 核心文件 | 行数 |
|---|------|---------|------|
| **① 表现层** | Console菜单 + REST API + JWT认证 | `Program.cs`, `Agent1.Api/Program.cs`, `Controllers/` | ~1800 |
| **② 模块调度层** | 模块工厂 + 懒加载 + 策略路由 | `ModuleDispatcher.cs`, `ModuleFactory.cs`, `IntentRouter.cs` | ~180 |
| **③ 业务编排层** | 对话流水线 + 评测引擎 | `AgentDialog.cs` (6步流水线), `EvalEngine.cs` (50条评测) | ~1330 |
| **④ 核心业务层** | AI推理 + 化工工具 + 知识库 | `LlmService.cs`, `ChemicalComplianceTools.cs`, `HybridKnowledgeBaseService.cs` | ~2300 |
| **⑤ 基础设施层** | 数据库 + 会话 + 记忆 + 审计 + 指标 | `DatabaseService.cs`, `MemoryService.cs`, `AuditService.cs`, `MetricsCollector.cs` | ~2100 |
| **⑥ 外部系统层** | PostgreSQL + Ollama + Embedding | pgvector, llama-server, nomic-embed-text | -- |

### 1.3 模块类型（ModuleType 枚举 — 已扩展至 12 种）

| 枚举值 | 模块名称 | 说明 | 状态 |
|--------|---------|------|:--:|
| `CoTSolid = 1` | 思维链推理（标准输出） | Chain-of-Thought 非流式 | ✅ |
| `CoTStream = 2` | 思维链推理（流式输出） | CoT + SSE 流式输出 | ✅ |
| `ReActSolid = 3` | ReAct 推理（标准输出） | Reasoning-Action 非流式 | ✅ |
| `ReActStream = 4` | ReAct 推理（流式输出） | ReAct + SSE 流式输出 | ✅ |
| `Reflection = 5` | Reflection 自我反思 | LLM 自检 + 纠错循环 | ✅ |
| `RAG = 6` | RAG 检索增强生成 | BM25 + Vector 混合检索 | ✅ |
| `UnifiedDialog = 7` | 智能对话系统 | 多轮对话 + 记忆 + 意图路由 | ✅ |
| `ComplianceCheck = 8` | **化工合规自查** ★ | 巡检内容 → 合规判断 | ✅ |
| `TicketFollowup = 9` | 整改工单跟进 | 状态机流转 + KB辅助 | ✅ |
| `RegulatoryAudit = 10` | 监管核查辅助 | 逐条比对法规+结构化报告 | ✅ |
| `EmergencyResponse = 11` | 应急响应方案 | 🆕 泄漏处置+疏散计算+消防匹配 | 🔶 预留 |
| `KnowledgeGraph = 12` | 知识图谱查询 | 🆕 化学品-法规-事故关联网 | 🔶 预留 |

> **架构收敛验证**：7个核心验证点（[架构收敛专项测试说明](../architecture/架构收敛专项测试说明.md)）覆盖 ModuleType 1-7。
> 扩展 ModuleType 8-10 已通过单独测试验证，11-12 为 v4.3 预留扩展。

---

## 二、真正的化工安全 AI Agent 应该具备的核心功能

### 2.1 当前已完成 vs 应具备

| 功能域 | 核心能力 | 当前状态 | 差距 |
|--------|---------|---------|------|
| **合规检查** | 危化品分类识别、储存兼容性判断、安全距离计算 | ✅ 已实现（7个 `[KernelFunction]`） | 缺少 MSDS 解析、GHS 标签识别 |
| **法规检索** | 国标（GB）、行标、园区规则、历史案例检索 | ✅ 已实现（BM25+Vector混合+RRF融合） | 法规版本演进追踪、法规冲突检测 |
| **风险评估** | 基于物质+工艺+环境的综合风险量化 | ✅ 已实现 3×3 矩阵 MVP | 缺 HAZOP/LOPA 高级分析模型 |
| **应急响应** | 泄漏处置方案、疏散距离计算、消防资源匹配 | ❌ 未实现 | 需结合化学品应急处置指南 |
| **巡检报告** | 自动生成规范格式的合规检查报告 | ⚠️ 部分实现 | 缺少PDF导出、电子签章 |
| **整改跟踪** | 工单生成、整改闭环、到期提醒 | ✅ 已实现 `TicketFollowupModule` | 缺工单持久化存储 |
| **多模态识别** | 储罐照片分析、管道图纸理解 | ✅ 已实现 `MultimodalService` (HttpClient直调) | 需 qwen-vl 模型就绪 |
| **监管核查** | 安监清单逐条比对法规 | ✅ 已实现 `RegulatoryAuditModule` | -- |
| **实时监测** | 与DCS/PLC系统对接，实时读取温度/压力/液位 | ⚠️ Integration配置已预留 | 无实际对接实现 |
| **知识图谱** | 化学品-法规-事故案例的知识关联网络 | ❌ 未实现 | -- |

### 2.2 合规工具覆盖度（7个 `[KernelFunction]`）

```
ChemicalComplianceTools (621行)
├─ CheckHazardCategory       ← 危化品分类查询（RAG + 数据库 + 硬编码三层降级）
├─ CheckStorageCompatibility ← 同库储存兼容性
├─ GetSafetyDistance         ← 安全间距查询（5个正则模式提取）
├─ GetCurrentTime            ← 通用时间
├─ Calculate                 ← 通用计算
├─ LookupChemicalProperties  ← 物化性质查询
└─ LookupRegulationReferences← 法规引用查询（GB编号提取+去重）
```

### 2.3 三层降级策略

```
查询请求
  │
  ├─ Tier 1: RAG 知识库检索 (HybridKnowledgeBaseService)
  │   ├─ BM25 关键词 + Vector 语义 → RRF 融合
  │   └─ 命中 → 返回结果 (最佳质量)
  │
  ├─ Tier 2: 结构化数据库查询 (ChemicalSubstanceDatabase)
  │   ├─ PostgreSQL chemical_substances 表
  │   └─ 命中 → 返回结果
  │
  └─ Tier 3: 硬编码字典 / 诚实声明
      ├─ 预置常见危化品字典（58种）
      └─ 字典无 → "数据库中暂无记录，建议查阅国标原文"
```

---

## 三、化工安全 Agent 应有的架构特性

### 3.1 当前架构 vs 目标架构对比

| 架构特性 | 目标要求 | 当前状态 | 差距评估 |
|---------|---------|---------|---------|
| **等保三级审计** | 操作日志≥180天留存、不可篡改、敏感信息脱敏 | ✅ 已实现 `AuditService` + PostgreSQL持久化 + `SensitiveDataMasker` + SHA256哈希链 | -- |
| **JWT认证** | RBAC角色控制（admin/auditor/viewer） | ✅ 已实现 3级角色 + BCrypt密码升级 | -- |
| **配置外化** | 12-Factor App，敏感信息环境变量注入 | ✅ 已实现 `appsettings.json` + 环境变量覆盖 + Fail-Fast验证 | -- |
| **结构化日志** | Serilog → Console + File + Seq | ✅ 已实现 三路输出 | -- |
| **可观测性** | OpenTelemetry (Metrics + Traces) | ✅ 已集成 | -- |
| **DI容器** | Microsoft.Extensions.DI | ✅ 已实现，30+服务注册 | 循环依赖已改用 Lazy\<T\> |
| **速率限制** | SemaphoreSlim(2,2) CPU推理并发保护 | ✅ 已实现 | -- |
| **工业系统集成** | ERP/WMS/EHS 对接接口 | ⚠️ `IntegrationConfig` 已预留 | 无实际实现 |
| **数据加密** | 传输加密 + 存储加密 | ⚠️ `EnableDataEncryption=true` 已配置 | 未验证实际加密链路 |
| **容器化部署** | Docker + docker-compose | ✅ 已实现（含GPU运行时配置） | -- |
| **GPU加速** | 批量向量嵌入 + Reranker GPU推理 | ✅ 已实现（batch_size=32, 超时降级） | -- |
| **评测体系** | 50条业务评测 + 六维评分 | ✅ 已实现 `EvalEngine` | -- |
| **多模态支持** | 图像理解 + GHS标签识别 | ✅ HttpClient 直调 Ollama `/api/chat` | 需 qwen-vl 模型 |
| **健康检查** | K8s liveness/readiness + Prometheus | ✅ 已实现 | -- |
| **安全加固** | Prompt注入防护 + 输出高危断言拦截 | ✅ `SafetyGuardService` 双防线 | -- |
| **命令模式** | MenuCommand 字典替代 if-else | ✅ `IMenuCommand` + 8 个命令类 | -- |
| **增量更新** | 知识库文件新增/修改/删除全追踪 | ✅ 含删除文件分块清理 | -- |

### 3.2 关键设计模式应用

| 设计模式 | 应用位置 | 说明 |
|---------|---------|------|
| **Builder 建造者** | `ConfigurationBuilder`, `LoggerConfiguration`, `ServiceCollection` | 声明式配置 → Build产出 |
| **Singleton 单例** | `AppConfig.Instance`, DI 所有服务 | 控制台应用全进程生命周期 |
| **Decorator 装饰器** | `ConsoleTeeWriter` (TextWriter) | 双写输出流，透明代理 |
| **Factory 工厂** | `ModuleFactory.CreateModule()` | 根据 ModuleType 创建对应模块 |
| **Strategy 策略** | `ModuleDispatcher` 路由, `IntentRouter` | 不同意图 → 不同处理策略 |
| **Facade 门面** | `ToolService`, `ChemicalRAG`, `ModelConfig` | 简化复杂子系统的访问接口 |
| **Adapter 适配器** | `DatabaseService` → Npgsql | 将 PostgreSQL 访问适配为业务接口 |
| **Circuit Breaker 断路器** | `LlmService.CheckCircuitBreaker()` | 3次连续失败 → 30s冷却 |

---

## 四、项目整改进度总览

### 4.1 已完成项 ✅

```
配置与基础设施
├─ ✅ 配置外部化 (appsettings.json + 环境变量 + Fail-Fast验证)
├─ ✅ Serilog 结构化日志 (Console + File + Seq 三路)
├─ ✅ DI 容器 (30+ 服务，Singleton 生命周期)
├─ ✅ JWT 认证 (3级角色 RBAC + BCrypt 密码哈希自动升级)
├─ ✅ OpenTelemetry 可观测性集成
├─ ✅ GlobalException / RateLimiting / RequestId / RequestMetrics 中间件

化工合规核心
├─ ✅ ChemicalComplianceTools (7个 [KernelFunction])
├─ ✅ 三层降级策略 (RAG → 数据库 → 硬编码/诚实声明)
├─ ✅ ConclusionVerifier (法规编号格式验证 + KB反向检索验证)
├─ ✅ ReflectionVerifier ("不用LLM,零幻觉"的事实验证)
├─ ✅ ChemicalRAG (三级知识体系: 国标 > 园区规则 > 历史案例)
├─ ✅ 混合检索 (BM25 + Vector + RRF融合 + QueryCache)

AI推理引擎
├─ ✅ SK Auto Function Calling (Semantic Kernel + Ollama)
├─ ✅ OllamaThinkingHandler (反射注入 think 参数)
├─ ✅ 断路器模式 (3次连续失败 → 30s冷却)
├─ ✅ GPU 批量嵌入 (batch_size=32, 超时降级)
├─ ✅ 流式推理 (think标签过滤状态机)

数据与持久化
├─ ✅ PostgreSQL + pgvector (HNSW索引)
├─ ✅ 会话记忆 (短期+长期双层 + 上下文压缩)
├─ ✅ 审计日志 (180天留存 + 敏感信息脱敏 + SHA256哈希链防篡改)
├─ ✅ MetricsCollector (Interlocked 原子计数器)

生产环境
├─ ✅ Docker 容器化 + docker-compose
├─ ✅ GPU 运行时配置 (NVIDIA Container Toolkit)
├─ ✅ Agent1.Api REST API (合规检查 + JWT认证)
├─ ✅ API 健康检查 (/health + /health/ready + /health/live + /metrics + /cache/stats)
├─ ✅ 50条业务评测体系 (六维评分: 工具匹配/参数准确/结论准确/Precision@K/Recall@K/Faithfulness)
├─ ✅ ConsoleTeeWriter (双写防缓冲区溢出)
├─ ✅ Program.cs 六维深度注释 (语法/框架/架构/业务/生产/建议)

本轮新增 (2026-06-16 第二轮修复)
├─ ✅ [P0] 循环依赖修复 — null!+SetKnowledgeBaseService → Lazy<T> 延迟解析
├─ ✅ [P1] 审计日志哈希链 — SHA256 + VerifyIntegrityAsync() + DB 持久化
├─ ✅ [P1] 知识库增量更新 — 文件追踪 + 新增/修改/删除全周期清理
├─ ✅ [P1] TicketFollowup 整改工单模块 — 173行
├─ ✅ [P2] 命令模式重构 — IMenuCommand 字典替代 if-else
├─ ✅ [P2] 多模态识别 — MultimodalService (HttpClient 直调 Ollama)
├─ ✅ [P2] 风险评估模型 MVP — RiskAssessmentService (3×3矩阵, 163行)
├─ ✅ [P2] 检索模式 enum 化 — SearchModeType 替代字符串
├─ ✅ [P3] 安全加固 — SafetyGuardService (注入防护+输出高危断言)
├─ ✅ [P3] 审计链 DB — chain_hash 写入 PostgreSQL + ALTER TABLE
├─ ✅ [P3] RegulatoryAudit 监管核查模块 — 187行
├─ ✅ [修补] 版本号启动横幅、增量更新菜单+API入口
├─ ✅ [核查] 全景分析文档代码级验证 — 9测试文件~59用例
```

### 4.2 待完成项 ⚠️

```
功能缺口
├─ ⚠️ 应急响应方案 — 未启动（泄漏处置+疏散计算+消防匹配）
├─ ⚠️ 工业系统集成 (ERP/WMS/EHS) — IntegrationService 为空壳
├─ ⚠️ 知识图谱构建 — 未启动（化学品-法规-事故关联网）

测试短板（🔴 当前最大风险）
├─ 🔴 前端 E2E 测试 — 0 条实施（Playwright 配置空缺，5条关键路径未覆盖）
├─ 🔴 前端单元测试 — 0 条实施（vitest.config.ts + MSW就绪但无测试文件）
├─ 🔴 后端覆盖率 — 仅 21% 行覆盖（核心模块 LlmService/HybridKB/AgentDialog 无直接测试）
├─ 🟡 CI 架构收敛卡口 — continue-on-error:true 不阻断退化
├─ 🟡 CI 覆盖率门禁 — 60%门禁在实际 21% 下可能失效

架构改进
├─ ⚠️ 数据加密验证 — EnableDataEncryption 配置就绪, 未端到端验证
├─ ⚠️ 循环依赖 — null!+SetService 临时方案，建议迁移到 Lazy<T>

生产加固
├─ ⚠️ API 时间窗口限流 — SemaphoreSlim(2,2) 仅有并发控制
├─ ⚠️ 前端 CIPipeline 空跑 — Job1b 无实际测试执行
```

### 4.3 完成度评估（2026-07-20 更新）

| 维度 | 完成度 | 变化 | 说明 |
|------|:----:|:----:|------|
| 基础设施与配置 | **95%** | — | DI/日志/认证/可观测性/健康检查/安全加固已就绪 |
| 化工合规核心 | **90%** | +2% | 7工具+混合检索+结论验证+风险评估+监管核查已就绪 |
| AI引擎 | **90%** | — | SK FC + Lazy<T> + 断路器 + GPU嵌入 + 多模态已成熟 |
| 数据持久化 | **95%** | — | pgvector + 审计哈希链(DB) + 记忆体系 + 增量更新完整 |
| 评测体系 | **85%** | — | 64条评测 + 六维评分已就绪 |
| **前端 Web SPA** | **60%** | 🆕 | 页面和组件大部完成, Pinia Store + MSW 已就绪, 测试为 0 |
| 工业集成 | **10%** | — | 仅有接口定义和配置预留 |
| 测试覆盖率 | **25%** | 🔴 | 后端665条但核心无覆盖, 前端0条, E2E空白 |
| **总体** | **~85%** | -3pp | P0-P2清完, 测试体系为当前最大短板, 前端E2E为零 |

---

## 五、技术栈全景

| 层级 | 技术 | 用途 |
|------|------|------|
| **运行时** | .NET 8 + C# 12 | 应用框架 |
| **AI编排** | Microsoft.SemanticKernel 1.74.0 | LLM 编排 + Auto Function Calling |
| **模型服务** | Ollama (Qwen3-8B / nomic-embed-text) | 本地推理 + 向量嵌入 |
| **数据库** | PostgreSQL + pgvector + Dapper | 结构化数据 + 向量检索 |
| **日志** | Serilog + Seq | 结构化日志 + 集中查看 |
| **可观测性** | OpenTelemetry | Metrics + Traces |
| **认证** | JWT Bearer + BCrypt | API 认证 |
| **容器化** | Docker + docker-compose + NVIDIA Container Toolkit | 部署 + GPU直通 |
| **测试** | xUnit + Moq | 单元测试 |
| **GPU加速** | llama.cpp CUDA (RTX 3090, sm_86) | 批量嵌入 + Reranker |

---

## 六、下一步建议优先级

| 优先级 | 任务 | 预估工作量 | 影响范围 |
|--------|------|-----------|---------|
| P0 | API 健康检查端点 /health /ready | 2h | 生产运维 | ✅ 已存在 |
| P0 | 循环依赖改用 Lazy\<T\> | 4h | 代码质量 | ✅ 已完成 |
| P1 | 审计日志哈希链完整性校验 | 8h | 等保合规 | ✅ 已完成 |
| P1 | 知识库增量更新机制 | 16h | 运维效率 | ✅ 已完成 |
| P1 | TicketFollowup 模块实现 | 24h | 业务闭环 | ✅ 已完成 |
| P2 | 命令模式重构 Program.cs 菜单 | 8h | 代码可维护性 | ✅ 已完成 |
| P2 | 多模态识别调用链路 | 16h | 功能增强 | ✅ 已完成 |
| P2 | 风险评估模型 (3×3矩阵 MVP) | 8h | 业务核心 | ✅ 已完成 |
| P3 | 监管核查辅助 RegulatoryAudit | 8h | 业务闭环 | ✅ 已完成 |
| P3 | 安全加固 Prompt注入+输出检测 | 8h | 生产安全 | ✅ 已完成 |
| P3 | 工业系统集成 (ERP/WMS/EHS) | 80h+ | 生产落地 | 待启动 |
| P3 | 应急响应方案 | 40h+ | 业务核心 | 待启动 |
| P3 | 知识图谱构建 | 80h+ | 高级特性 | 待启动 |

---

## 七、事实核查与补充修正（2026-06-16 代码级验证）

### 7.1 测试覆盖率核查（2026-07-20 更新）

后端测试现状（来源：[测试总纲](../testing/测试总纲.md)）：

| 维度 | 当前值 | 目标 | 差距 |
|------|:---:|:---:|:---:|
| 单元测试 | **665 条** (63类) | — | ✅ 数量充足 |
| 集成测试 | **63 条** (13 xUnit + 25 API + 25 CLI) | — | ⚠️ 偏少 |
| E2E 测试 (前端) | **0 条** | 5 条关键路径 | 🔴 完全缺失 |
| 行覆盖率 | **~21%** | ≥80% | 🔴 -59pp |
| 分支覆盖率 | **~10%** | ≥70% | 🔴 -60pp |
| 架构收敛测试 | 7/7 通过 | 7/7 | ✅ |
| 前端单元测试 | **0 条** (vitest.config.ts 存在) | 8 Stores + 6 组件 | 🔴 未启动 |

**核心问题**：
- 665条后端单元测试集中在纯逻辑方法，核心模块（LlmService 1035行、HybridKBService 632行、AgentDialog 332行）**仍无直接单元测试**
- 前端 `vitest.config.ts` 和 `@playwright/test` 依赖已就绪，但 `src/__tests__/` 和 `e2e/` 目录均为空
- CI 中 `frontend-test` Job 的 `npx vitest run` 实际空跑
- 架构收敛测试在 CI 中设置 `continue-on-error: true`，架构退化不会阻断构建

### 7.2 多模态调用链路核查

原分析标为"MultimodalModelId 配置就绪，无实际调用"。经代码追踪：

```
配置流：
appsettings.json → AppConfig.Llm.MultimodalModelId ("qwen-vl:latest")
                   → ModelConfig.MultimodalModelId (静态属性转发)

代码引用：
Grep 全项目搜索 "MultimodalModelId" → 仅在 AppConfig.cs + ModelConfig.cs + appsettings.json 定义
                   → 无任何 .cs 文件中实际调用 ModelConfig.MultimodalModelId

阻点分析：
① SK Kernel 初始化用 AddOpenAIChatCompletion(ModelConfig.ModelId, ...)，固定用文本模型
② 当前仅有一个 Kernel 实例，未区分文本/视觉模型
③ SK Connector 的 ChatCompletion API 不支持 image_url 类型的 content（需用 IChatClient 底层接口）
④ Ollama 的 qwen-vl 模型支持 OpenAI 兼容 /v1/chat/completions 带 image_url
```

**核查结论**：阻点在 **SK 高层 API 限制** + **无代码调用路径**。需用 `HttpClient` 直调 Ollama `/api/chat` 端点（带 `images` 字段），而非经过 SK Kernel。

### 7.3 IntegrationService 接口契约核查

`IIntegrationService` 定义 5 个方法，`IntegrationService` 实现全部返回空/Task.CompletedTask：

| 接口方法 | 返回类型 | 实际实现 | 化工场景缺口 |
|---------|---------|---------|------------|
| `GetWarehouseRecordsAsync` | `List<WarehouseRecord>` | 返回空列表 | 缺 DCS 实时点位数据、PLC 报警信号 |
| `GetEHSTicketsAsync` | `List<EHSTicket>` | 返回空列表 | 缺与安监平台的数据交换格式 |
| `SyncERPDataAsync` | `Task` | Task.CompletedTask | 缺 REST API 端点配置、认证方式 |
| `SyncWMSDataAsync` | `Task` | Task.CompletedTask | 同上 |
| `SyncEHSDataAsync` | `Task` | Task.CompletedTask | 同上 |

**核查结论**：接口契约已完整定义（含 WarehouseRecord/EHSTicket 数据模型），但实现为空壳。`appsettings.json` 中 `Integration` 段已预留 `ERPApiBaseUrl/WMSApiBaseUrl/EHSApiBaseUrl` 配置，三个开关 `EnableERPSync/EnableWMSSync/EnableEHSSync` 默认 false。接口设计可用于 DCS/PLC 扩展。

### 7.4 SK Kernel 依赖版本核查

| 包名 | 版本 | 备注 |
|------|------|------|
| `Microsoft.SemanticKernel` | **1.74.0** | 稳定版 |
| `Microsoft.SemanticKernel.Connectors.Ollama` | **1.74.0-alpha** | ⚠️ 预发布版！生产风险 |
| `Microsoft.SemanticKernel.Connectors.OpenAI` | 1.74.0 | 稳定版 |

**风险提示**：`Connectors.Ollama` 为 alpha 预发布版本，API 可能在中途 breaking change。SK 2.x 已发布，迁移路径需评估。

---

## 八、补充识别的风险项

### 8.1 Prompt 注入攻击防护 ✅ 已实现

- `SafetyGuardService.ValidateInput()` 拦截 7 类注入模式
- Console + API 双入口已集成

### 8.2 知识库版本管理

- `ChemicalRAG.LoadKnowledgeBaseAsync()` 全量扫描 knowledgebase/ 目录，无版本标识
- 法规更新（新 GB 标准替代旧标准）时，旧文档分块仍留在索引中
- 建议：每个文档记录 `source_file + last_modified` 元数据，索引按 source 去重更新

### 8.3 模型输出安全 ✅ 已实现

- `SafetyGuardService.ValidateOutput()` 拦截 3 类高危断言（存储兼容性/安全距离/绝对安全）
- Console 入口黄色警告 + API 响应合并到 Warnings 列表

### 8.4 数据库密码残留风险

- `appsettings.json` 中 `Database.Password` 为空字符串（正确），但早期版本曾在 `AppConfig.cs` 硬编码
- 环境变量 `DB_PASSWORD` 未设置时系统使用空密码连接，可能导致误连其他 PostgreSQL 实例
- 建议：启动时强制检查 `DB_PASSWORD` 环境变量非空（生产环境）
