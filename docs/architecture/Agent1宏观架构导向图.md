# 🏗️ Agent1 项目宏观架构导向图

> 生成日期：2026-06-13  
> 最后更新：2026-07-20  
> 项目：化工园区危化品合规审核 AI Agent (.NET 8 + Semantic Kernel + GPU + Vue 3)  
> 涵盖：六层宏观架构 + 前端表现层 / 模块耦合关系 / 数据产生链路 / 代码文件引用 / 请求全链路 / CI/CD 持续集成

---

## 一、六层宏观架构总览

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│                               Agent1 化工园区危化品合规审核 AI Agent                              │
│                                    .NET 8 + Semantic Kernel + GPU                              │
└──────────────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│  [第1层] 表现层 (Presentation) — 三通道入口                                                 │
│  ┌─────────────────────────┐  ┌──────────────────────────────────────┐  ┌──────────────────┐ │
│  │  Console 控制台          │  │  ASP.NET Core REST API (Agent1.Api)  │  │  Web 前端 SPA     │ │
│  │  Program.cs (663行)      │  │  Program.cs (438行)                  │  │  agent1-web/      │ │
│  │  ├─ 17项菜单循环          │  │  ├─ 15 个 Controllers               │  │  ├─ Vue 3 + Vite 5│ │
│  │  ├─ IMenuCommand 命令模式 │  │  │   Auth / Compliance / Inspection │  │  ├─ 17 页面组件   │ │
│  │  ├─ 评测/测试/对话 入口    │  │  │   Tickets / KnowledgeBase /      │  │  ├─ 8 个 Pinia    │ │
│  │  └─ ConsoleTeeWriter     │  │  │   KnowledgeGraph / Emergency /   │  │  │   Store        │ │
│  └─────────┬───────────────┘  │  │   Dashboard / Audit / System      │  │  ├─ Element Plus  │ │
│            │                  │  ├─ 5 个 Middleware:                   │  │  │   + Tailwind   │ │
│            │                  │  │   GlobalException / RateLimiting /  │  │  ├─ MSW Mock      │ │
│            │                  │  │   RequestId / RequestMetrics /      │  │  ├─ ECharts 图表  │ │
│            │                  │  │   TokenBlacklist                    │  │  └─ Playwright    │ │
│            │                  │  ├─ JWT Bearer 认证 (3级 RBAC)         │  │     (E2E 规划中) │ │
│            │                  │  ├─ OpenTelemetry (Metrics+Traces)     │  └────────┬─────────┘ │
│            │                  │  └─ Serilog + Seq 结构化日志           │           │           │
│            │                  └──────────────────┬───────────────────┘           │           │
│            │                                     │                               │           │
│            │              HTTP/REST (JWT Auth)   │         HTTP (Vite Proxy)     │           │
│            │                  ┌──────────────────┘                               │           │
│            │                  │                                                  │           │
│            │              ┌───▼──────────────────────────────────────────────────▼───┐       │
│            │              │         SSH Tunnel (远程GPU环境联调)                     │       │
│            │              └──────────────────────────────────────────────────────────┘       │
├────────────┼─────────────────────────────────────────────────────────────────────────────────┤
│  [第2层]  模块调度层 (Module Dispatch) — 12 种 ModuleType 统一调度                           │
│            │                                                                                  │
│  ┌─────────▼──────────────────────────────────────────────────────────────────────────────────┐
│  │  ModuleDispatcher (Services/Infrastructure/ModuleDispatcher.cs, 54行)                       │
│  │  ┌─────────────────────────────────────────────────────────────────────────────────────┐   │
│  │  │  ExecuteModuleAsync(ModuleType) → 懒创建 + Dictionary 缓存                             │   │
│  │  │  if (!_modules.TryGetValue(type))  →  _factory.CreateModule(type) → 缓存              │   │
│  │  │  ↓                                                                                    │   │
│  │  │  ModuleFactory (Services/Infrastructure/ModuleFactory.cs, 67行)                        │   │
│  │  │  ┌──────┬───────┬───────┬───────┬────────┬────┬─────────┬──────────┬───────┬─────┐   │   │
│  │  │  │CoT   │CoT    │ReAct  │ReAct  │Reflect-│RAG │Unified  │Compliance│Ticket │Regu-│   │   │
│  │  │  │Solid │Stream │Solid  │Stream │ion     │    │Dialog   │Check     │Follow-│latory│   │   │
│  │  │  │(1)   │(2)    │(3)    │(4)    │(5)     │(6) │(7)      │(8)☆      │up (9) │Audit│   │   │
│  │  │  │      │       │       │       │        │    │         │          │       │(10)  │   │   │
│  │  │  ├──────┼───────┼───────┼───────┼────────┼────┼─────────┼──────────┼───────┼─────┤   │   │
│  │  │  │ LLM  │ LLM   │ LLM   │ LLM   │ LLM+KB │LLM │KB+LLM   │KB+LLM    │状态机  │法规 │   │   │
│  │  │  │+KB   │ +KB   │       │       │        │    │+Audit   │+Tool     │+KB    │比对 │   │   │
│  │  │  └──────┴───────┴───────┴───────┴────────┴────┴─────────┴──────────┴───────┴─────┘   │   │
│  │  │  ┌──────────┐  ┌──────────────────┐                                                    │   │
│  │  │  │Emergency │  │KnowledgeGraph    │  ← 🆕 v4.3 预留扩展                                │   │
│  │  │  │Response  │  │(12)              │                                                    │   │
│  │  │  │(11)      │  │                  │                                                    │   │
│  │  │  │应急响应   │  │知识图谱可视化     │                                                    │   │
│  │  │  └──────────┘  └──────────────────┘                                                    │   │
│  │  └─────────────────────────────────────────────────────────────────────────────────────┘   │
│  └────────────────────────────────────────────────────────────────────────────────────────────┘
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│  [第3层] 业务编排层 (Orchestration)                                                           │
│                                                                                               │
│  ┌───────────────────────────────────────┐  ┌────────────────────────────────────────┐        │
│  │  AgentDialog (333行)                  │  │  EvalEngine (999行)                     │        │
│  │  Services/Dialog/AgentDialog.cs       │  │  Services/Eval/EvalEngine.cs           │        │
│  │                                       │  │                                        │        │
│  │  6步线性流水线:                        │  │  评测流程:                              │        │
│  │  [1/6] PreprocessAsync 输入清理        │  │  Layer1: FC就绪性检查 (5条诊断用例)     │        │
│  │  [2/6] RouteIntent 意图分类            │  │  Layer2: 逐条业务评测 (63条)           │        │
│  │  [3/6] LoadContextAsync 上下文加载     │  │    ├─ 工具匹配 → CheckParams           │        │
│  │  [4/6] ExecuteBusinessAsync 业务执行   │  │    ├─ 结论判断 → CheckConclusion       │        │
│  │  [5/6] SaveSessionAsync 会话保存       │  │    ├─ RAG检索 → Precision/Recall/MRR   │        │
│  │  [6/6] FormatOutput 结果输出          │  │    ├─ 忠实度 → ReflectionVerifier        │        │
│  │                                       │  │    ├─ AnswerRelevance (LLM评分1-5)      │        │
│  │  评测快速通道:                          │  │    └─ CitationAccuracy (法规引用验证)  │        │
│  │   ExecuteEvalFastAsync()              │  │                                        │        │
│  │   → 跳过流水线,直接非流式FC调用         │  │  输出: eval_reportXXX.json             │        │
│  └───────────┬───────────────────────────┘  └────────────────────────────────────────┘        │
│              │                                                                                 │
├──────────────┼─────────────────────────────────────────────────────────────────────────────┤──┤
│  [第4层]    核心业务层 (Core Business)                                                        │  │
│              │                                                                                │  │
│  ┌───────────▼──────────────┐ ┌────────────────────┐ ┌──────────────────────────────────┐    │  │
│  │  LlmService (1035行)     │ │ ChemicalCompliance  │ │  知识库 (Knowledge)              │    │  │
│  │  Services/AI/LlmService  │ │ Tools (598行)       │ │                                  │    │  │
│  │                          │ │ Services/Compliance │ │  ┌────────────────────────────┐  │    │  │
│  │  ┌─ SK Auto FC ──────┐   │ │                    │ │  │HybridKnowledgeBaseService  │  │    │  │
│  │  │ InvokeStreamAsync │   │ │ 7个 [KernelFunc]:  │ │  │  (614行)                   │  │    │  │
│  │  │ FunctionChoice    │   │ │ ┌────────────────┐ │ │  │  - BM25 + Vector 双路检索  │  │    │  │
│  │  │ Behavior.Required │   │ │ │CheckHazard     │ │ │  │  - RRF融合(k=60)           │  │    │  │
│  │  └───────┬───────────┘   │ │ │  Category      │ │ │  │  - 查询缓存 (LRU)          │  │    │  │
│  │          │               │ │ ├────────────────┤ │ │  │  - 批量嵌入 (GPU)          │  │    │  │
│  │  ┌───────▼───────────┐   │ │ │CheckStorage    │ │ │  │  - 查询扩展 (同义词)       │  │    │  │
│  │  │ OllamaThinking    │   │ │ │  Compatibility │ │ │  └──────┬─────────────────────┘  │    │  │
│  │  │ Handler (反射注入) │   │ │ ├────────────────┤ │ │         │                        │    │  │
│  │  │ 拦截/api/chat     │   │ │ │GetSafety       │ │ │  ┌──────▼─────────────────────┐  │    │  │
│  │  │ 注入think参数      │   │ │ │  Distance      │ │ │  │ KnowledgeBaseService       │  │    │  │
│  │  └───────────────────┘   │ │ ├────────────────┤ │ │  │  (663行)                   │  │    │  │
│  │                          │ │ │LookupChemical  │ │ │  │  - BM25算法 (K1=1.5,B=0.75)│  │    │  │
│  │  ┌─ 嵌入服务 ─────────┐  │ │ │  Properties    │ │ │  │  - 倒排索引构建            │  │    │  │
│  │  │ GetEmbeddingAsync  │  │ │ ├────────────────┤ │ │  │  - GB编号标准化            │  │    │  │
│  │  │ GetEmbeddingsBatch │  │ │ │GetMajorHazard  │ │ │  │  - N-gram中文分词          │  │    │  │
│  │  │ (GPU批处理)        │  │ │ │  Threshold     │ │ │  │  - 智能分块(语义边界)      │  │    │  │
│  │  └────────────────────┘  │ │ ├────────────────┤ │ │  └────────────────────────────┘  │    │  │
│  │                          │ │ │CheckRegulation │ │ │                                  │    │  │
│  │  ┌─ 熔断器 ───────────┐  │ │ │  Version       │ │ │  ┌────────────────────────────┐  │    │  │
│  │  │ CircuitBreaker     │  │ │ ├────────────────┤ │ │  │ ChemicalSubstanceDatabase  │  │    │  │
│  │  │ 3次失败→30s冷却    │  │ │ │GetCurrentTime  │ │ │  │  (983行, 50+化学品)        │  │    │  │
│  │  └────────────────────┘  │ │ │ Calculate       │ │ │  │  - 结构化属性 (CAS/UN/闪点) │  │    │  │
│  │                          │ │ └────────────────┘ │ │  │  - 别名映射 (液氯→氯)      │  │    │  │
│  │  ┌─ FC诊断 ──────────┐  │ │                    │ │  │  - 储存兼容性规则           │  │    │  │
│  │  │ FunctionCall       │  │ │ 三级降级策略:      │ │  │  - 安全距离规则             │  │    │  │
│  │  │ DiagnosticsFilter  │  │ │ RAG → 结构化DB     │ │  │  - 法规版本追踪             │  │    │  │
│  │  │ → LastFunctionCalls│  │ │ → 硬编码字典       │ │  └────────────────────────────┘  │    │  │
│  │  └────────────────────┘  │ │                    │ │                                  │    │  │
│  └──────────────────────────┘ └────────────────────┘ │  ┌────────────────────────────┐  │    │  │
│                                                       │  │ QueryCacheService          │  │    │  │
┌───────────────────────────────────────────────────────┤  │  (193行)                   │  │    │  │
│  [第4层] 续 — 评估验证层                               │  │  - ConcurrentDictionary    │  │    │  │
│                                                       │  │  - TTL=5min (默认)         │  │    │  │
│  ┌──────────────────────┐ ┌────────────────────────┐  │  │  - LRU淘汰 (Max=500)       │  │    │  │
│  │ ReflectionVerifier   │ │ ConclusionVerifier     │  │  │  - 后台定时清理(2min)      │  │    │  │
│  │ (279行)              │ │ (197行)                │  │  └────────────────────────────┘  │    │  │
│  │ 代码级事实核查:       │ │ 法规幻觉检测:           │  │                                  │    │  │
│  │ - 提取法规声明(3种正)│ │ - 提取GB编号            │  │  ┌────────────────────────────┐  │    │  │
│  │ - KB反向检索验证     │ │ - KB反向检索验证        │  │  │ RerankerService             │  │    │  │
│  │ - FactualPrecision   │ │ - 空数据检测            │  │  │  (214行)                   │  │    │  │
│  │ - BuildCorrectedPrompt│ │ - 合规标签校验          │  │  │  - Cross-Encoder (远程)    │  │    │  │
│  └──────────────────────┘ └────────────────────────┘  │  │  - 降级: 本地启发式        │  │    │  │
│                                                       │  │  - 位置加权+关键词密度      │  │    │  │
│  ┌──────────────────────┐ ┌────────────────────────┐  │  └────────────────────────────┘  │    │  │
│  │ ToolService (184行)  │ │ IntentRouter           │  │                                  │    │  │
│  │ [Obsolete] 降级兜底  │ │ (P0补充)               │  │  ┌────────────────────────────┐  │    │  │
│  │ - LLM工具选择         │ │ 意图分类:              │  │  │ GpuVectorIndexService       │  │    │  │
│  │ - 关键词Fallback      │ │ ChemicalCompliance     │  │  │  (347行)                   │  │    │  │
│  │ - switch路由执行      │ │ / GeneralChat / ...    │  │  │  - 内存向量索引(全量加载)  │  │    │  │
│  └──────────────────────┘ └────────────────────────┘  │  │  - 余弦相似度暴力搜索       │  │    │  │
│                                                       │  │  - 5分钟后台同步pgvector    │  │    │  │
│  ┌──────────────────────┐ ┌────────────────────────┐  │  │  - ReaderWriterLockSlim     │  │    │  │
│  │ SensitiveDataMasker  │ │ DocExtractor (228行)   │  │  └────────────────────────────┘  │    │  │
│  │ (敏感信息脱敏)        │ │ PdfExtractor (219行)   │  │                                  │    │  │
│  └──────────────────────┘ └────────────────────────┘  │  ┌────────────────────────────┐  │    │  │
│                                                       │  │ TextCleaner (276行)         │  │    │  │
│  ┌──────────────────────────────────────────────────┐ │  │ SemanticChunker (329行)     │  │    │  │
│  │  MetricsCollector (145行, static, 线程安全)      │ │  └────────────────────────────┘  │    │  │
│  │  RecordLlmCall / RecordRagSearch / RecordApiReq  │ │                                  │    │  │
│  │  Interlocked.Increment (无锁原子操作)            │ └──────────────────────────────────┘    │  │
│  └──────────────────────────────────────────────────┘                                        │  │
├──────────────────────────────────────────────────────────────────────────────────────────────┤──┤
│  [第5层] 基础设施层 (Infrastructure)                                                          │  │
│                                                                                               │  │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  ┌──────────────┐  ┌──────────────┐   │  │
│  │DatabaseService│  │SessionService│  │ MemoryService  │  │ResponseCache  │  │Integration   │   │  │
│  │              │  │              │  │               │  │  Service      │  │  Service     │   │  │
│  │-pgvector连接 │  │-对话轮次管理  │  │-短期记忆管理   │  │-LLM响应缓存   │  │-外部系统集成 │   │  │
│  │-向量CRUD     │  │-会话上下文    │  │-关键词匹配     │  │-TTL淘汰      │  │-告警通知    │   │  │
│  │-化学文档管理 │  │              │  │-事实提取       │  │               │  │             │   │  │
│  └──────┬───────┘  └──────────────┘  └───────────────┘  └──────────────┘  └──────────────┘   │  │
│         │                                                                                     │  │
├─────────┼─────────────────────────────────────────────────────────────────────────────────────┤──┤
│  [第6层] 外部系统层 (External)                                                                │  │
│         │                                                                                     │  │
│  ┌──────▼───────┐  ┌──────────────┐  ┌───────────────┐  ┌──────────────┐  ┌──────────────┐    │  │
│  │ PostgreSQL   │  │ llama-server  │  │ nomic-embed   │  │ bge-reranker │  │ KnowledgeBase│    │  │
│  │  + pgvector  │  │ (GPU推理)     │  │ (GPU嵌入)     │  │ (精排)       │  │ Filesystem   │    │  │
│  │              │  │              │  │               │  │              │  │              │    │  │
│  │-向量存储     │  │Qwen3-8B      │  │/v1/embeddings │  │Python sidecar│  │国标/园区规则/  │    │  │
│  │-结构化数据   │  │Q4_K_M GGUF   │  │               │  │/rerank API   │  │历史案例      │    │  │
│  │-全文索引     │  │              │  │               │  │              │  │              │    │  │
│  │-评测集存储   │  │/api/chat     │  │               │  │              │  │              │    │  │
│  └──────────────┘  └──────────────┘  └───────────────┘  └──────────────┘  └──────────────┘    │  │
│                                                                                               │  │
│  配置文件: appsettings.json ← DB_PASSWORD(环境变量)  ModelConfig(Endpoint+ModelId)          │    │  │
│                                                                                               │    │  │
├───────────────────────────────────────────────────────────────────────────────────────────────┤──┘──┤
│  [CI/CD 持续集成层] — GitHub Actions 七阶段质量流水线                                              │  │
│                                                                                                  │  │
│  ┌──────────────────────────────────────────────────────────────────────────────────────────┐    │  │
│  │  push → [Job1] build-and-test (compile + unit tests + 覆盖率≥60%门禁)                      │    │  │
│  │      → [Job1b] frontend-test (Vitest 单元测试)                                            │    │  │
│  │      → [Job2] integration-test (PostgreSQL service容器 + Category=Integration)            │    │  │
│  │      → [Job3] docker (main分支 → GHCR构建推送)                                            │    │  │
│  │      → [Job4] benchmark (API性能回归: P95退步>50%阻断 + 错误率>5%阻断)                    │    │  │
│  │      → [Job5] staging-deploy (SSH → docker-compose up)                                    │    │  │
│  │      → [Job7] notify (QQ邮箱双通道: 成功摘要 vs 失败告警)                                  │    │  │
│  └──────────────────────────────────────────────────────────────────────────────────────────┘    │  │
│                                                                                                  │  │
│  [架构收敛验证层] — ArchitectureTest 7点收敛 + 12 ModuleType 覆盖                                │  │
│  ┌──────────────────────────────────────────────────────────────────────────────────────────┐    │  │
│  │  验证: 文件收敛 / IntentRouter / 统一调度 / 6步流水线 / 基础设施共享 / 无硬编码 / 范式集成  │    │  │
│  │  CI状态: dotnet run --project ArchitectureTest (continue-on-error待修复→硬阻断)           │    │  │
│  └──────────────────────────────────────────────────────────────────────────────────────────┘    │  │
│                                                                                                  │  │
│  配置文件: appsettings.json ← DB_PASSWORD(环境变量)  ModelConfig(Endpoint+ModelId)          │    │  │
│                                                                                               │  │
└───────────────────────────────────────────────────────────────────────────────────────────────┘──┘
```

---

## 二、功能模块关系图 — 耦合关系与依赖链路

```
                          ┌──────────────────────────────────────┐
                          │              Program.cs              │
                          │        (DI容器装配 + 菜单循环)         │
                          │  ┌─────────────────────────────────┐ │
                          │  │  ServiceCollection               │ │
                          │  │  ┌───┬───┬───┬───┬───┬───┬───┐ │ │
                          │  │  │Db │Ses│Mem│Llm│Dia│KB │Fac│ │ │
                          │  │  └───┴───┴───┴───┴───┴───┴───┘ │ │
                          │  └─────────────────────────────────┘ │
                          │  llmSvc.SetKnowledgeBaseService(kb)  │ ← 延迟注入打破循环依赖
                          └────┬────┬────┬────┬────┬────┬────────┘
                               │    │    │    │    │    │
          ┌────────────────────┼────┼────┼────┼────┼────┼─────────────────────┐
          │                    │    │    │    │    │    │                     │
          ▼                    │    │    ▼    │    ▼    │                     ▼
┌─────────────────┐            │    │  ┌──────────────────┐    │    ┌─────────────────┐
│  AgentDialog    │◄───────────┼────┼──│ ChemicalCompli-  │    │    │ HybridKnowledge │
│  (业务编排)      │            │    │  │ anceTools (7工具)│    │    │ BaseService     │
│                 │            │    │  │                  │    │    │ (混合检索核心)   │
│ 6步流水线:       │            │    │  │ @GetCachedOr-    │────┼────┤                 │
│ ExecuteAsync()  │            │    │  │   RetrieveAsync  │    │    │ RetrieveAsync() │
│   ↓             │            │    │  │   ↓              │    │    │   ├─Cache查询    │
│ ExecuteChemical │            │    │  │ RAG → 硬编码     │    │    │   ├─BM25(并发)   │
│ ComplianceAsync │────────────┼────┼──│   ↓              │    │    │   ├─Vector(并发) │
│   ↓             │            │    │  │ 7个KernelFunc    │    │    │   ├─RRF融合(k=60)│
│ ExecuteEvalFast │            │    │  │   - Hazard ✓     │    │    │   └─Cache写入    │
│ Async()         │            │    │  │   - Storage ✓    │    │    │                 │
│                 │            │    │  │   - Distance ✓   │    │    │ ExpandQuery()  │
└────────┬────────┘            │    │  │   - Properties ✓ │    │    │   ├─同义词扩展  │
         │                     │    │  │   - Threshold ✓  │    │    │   └─GB编号追加  │
         │ ┌───────────────────┘    │  │   - Regulation✓  │    │    │                 │
         │ │                        │  │   - Time ✓       │    │    │ 子组件:         │
         ▼ ▼                        │  │   - Calculate ✓  │    │    │  ┌────────────┐ │
┌────────────────────────┐          │  │                  │    │    │  │QueryCache  │ │
│     LlmService         │          │  │                  │    │    │  │Service     │ │
│     (AI核心)           │          │  │ 依赖:             │    │    │  │(LRU+TTL)   │ │
│                        │          │  │  - IKBService     │    │    │  └────────────┘ │
│ ┌────────────────────┐ │          │  │  - RerankerService│    │    │  ┌────────────┐ │
│ │SK Kernel           │ │          │  │  - ChemSubstanceDB│    │    │  │GpuVector   │ │
│ │ ├─AddOpenAIChat... │ │          │  │  - RagCache       │    │    │  │IndexService│ │
│ │ ├─Plugins.AddFrom  │ │          │  │                  │    │    │  │(内存余弦)  │ │
│ │ │  Object(chemTools)│ │          │  └────────┬─────────┘    │    │  └────────────┘ │
│ │ └─FunctionInvoc    │ │          │           │              │    │  ┌────────────┐ │
│ │    ationFilters    │ │          │           │              │    │  │Reranker    │ │
│ └────────┬───────────┘ │          │           │              │    │  │Service     │ │
│          │              │          │           └──────────────┼────┤  │(Cross-Enc) │ │
│ ┌────────▼───────────┐ │          │                          │    │  └────────────┘ │
│ │ InvokeStreamAsync  │ │          │                          │    │  ┌────────────┐ │
│ │ (FC=Required)      │─┼──────────┼────────────────────┐     │    │  │BM25(KBSvc) │ │
│ │  └─EnableThinking  │ │          │                    │     │    │  │(倒排索引)  │ │
│ └────────┬───────────┘ │          │                    │     │    │  └────────────┘ │
│          │              │          │                    │     │    └────────┬────────┘
│ ┌────────▼───────────┐ │          │                    │     │             │
│ │InvokeNonStreaming  │ │          │                    │     │             │
│ │WithRetryAsync      │ │          │                    │     │    ┌────────▼────────┐
│ │ 第1次:FC模式       │ │          │                    │     │    │ KnowledgeBase   │
│ │ 第2-3次:内联工具结果│ │          │                    │     │    │ Service (纯BM25)│
│ └────────────────────┘ │          │                    │     │    │                 │
│                        │          │                    │     │    │ K1=1.5, B=0.75  │
│ ┌────────────────────┐ │          │                    │     │    │ 倒排索引         │
│ │ OllamaThinking     │ │          │                    │     │    │ N-gram分词      │
│ │ Handler            │ │          │                    │     │    └────────────────┘
│ │ (DelegatingHandler)│ │          │                    │     │
│ └────────────────────┘ │          │                    │     │
│                        │          │                    │     │    ┌────────────────┐
│ ┌────────────────────┐ │          │                    │     │    │DatabaseService │
│ │ 熔断器 Check/Record│ │          │                    │     │    │(PostgreSQL+    │
│ │ 3次失败→30s冷却    │ │          │                    │     │    │ pgvector)      │
│ └────────────────────┘ │          │                    │     │    └────────────────┘
│                        │          │                    │     │
└──────────┬─────────────┘          │                    │     │
           │                        │                    │     │
    ┌──────▼──────┐          ┌──────▼──────┐      ┌──────▼──┐  │
    │llama-server │          │nomic-embed  │      │bge-      │  │
    │/api/chat    │          │/embeddings  │      │reranker  │  │
    └─────────────┘          └─────────────┘      └──────────┘  │
                                                                 │
    评估验证层:                                                    │
    ┌─────────────────┐  ┌──────────────────┐  ┌───────────────┐ │
    │ EvalEngine      │  │ReflectionVerifier│  │Conclusion     │ │
    │ RunCompliance   │──│ VerifyBusiness   │──│ Verifier      │ │
    │ EvalAsync()     │  │ FactsAsync()     │  │ VerifyAsync() │ │
    │                 │  │                  │  │               │ │
    │ EvaluateRetr-   │  │ 提取法规声明      │  │ 提取GB编号    │ │
    │ ievalQuality    │  │ KB反向检索       │  │ KB验证        │ │
    │   →P@K,R@K,MRR  │  │ FactualPrecision │  │ Hallucination  │ │
    └─────────────────┘  └──────────────────┘  └───────────────┘ │
                                                                 │
    DI容器关系:                         ┌───────────────────────┘
    ┌──────────────────────────────────┐
    │ 循环依赖解耦:                      │
    │                                  │
    │ LlmService ──→ ChemicalTools     │
    │     ↑              ↓             │
    │     │         IKnowledgeBase      │
    │     │              ↓             │
    │     └──── HybridKBService ───────┘
    │                                  │
    │ 解法:                              │
    │ 1. LlmService 先以 null! 注册       │
    │ 2. DI完成后:                       │
    │   llmSvc.SetKnowledgeBaseService  │
    │   chemTools.SetRerankerService    │
    └──────────────────────────────────┘
```

---

## 三、数据产生链路图 — 每次评测/对话的数据全生命周期

### 3.1 用户查询 → RAG检索 → LLM回答 主链路

```
                         用户输入: "苯属于什么危险类别"
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          │                           │                           │
          ▼                           ▼                           ▼
  ┌───────────────┐          ┌───────────────┐          ┌───────────────┐
  │ [1] Preprocess│          │ [2] RouteIntent│          │ [3] LoadContext│
  │ 输入清理       │          │ IntentRouter   │          │ 会话+记忆+用户  │
  │ input.Trim()  │          │ .Route(input)  │          │ 画像加载       │
  │               │          │                │          │               │
  │ AgentDialog   │          │ 返回:           │          │ PipelineContext│
  │ .Preprocess   │          │ Chemical       │          │ { Session,     │
  │ Async() :L82  │          │ Compliance     │          │   History,     │
  └───────────────┘          └───────────────┘          │   Memory,      │
                                                        │   UserProfile }│
                                                        │ LoadContext    │
                                                        │ Async() :L91   │
                                                        └───────┬───────┘
                                                                │
          ┌─────────────────────────────────────────────────────▼──────────────┐
          │ [4] ExecuteBusinessAsync (AgentDialog.cs:L110)                      │
          │                                                                     │
          │  ┌─────────────────────────────────────────────────────────────┐    │
          │  │ ExecuteChemicalComplianceAsync (AgentDialog.cs:L164)         │    │
          │  │                                                             │    │
          │  │  prompt = $"{SystemRole}\n{History}\n{UserInput}\n{Output}" │    │
          │  │                │                                            │    │
          │  │                ▼                                            │    │
          │  │  ┌───────────────────────────────────────────────────┐      │    │
          │  │  │ LlmService.InvokeStreamWithRetryAsync(prompt)     │      │    │
          │  │  │         │                                         │      │    │
          │  │  │         ▼                                         │      │    │
          │  │  │  ┌─────────────────────────────────────┐          │      │    │
          │  │  │  │ InvokeStreamAsync(prompt) :L85      │          │      │    │
          │  │  │  │                                     │          │      │    │
          │  │  │  │  settings = {                       │          │      │    │
          │  │  │  │    FunctionChoiceBehavior           │          │      │    │
          │  │  │  │      = Required(),                  │          │      │    │
          │  │  │  │    Temperature = 0.3                │          │      │    │
          │  │  │  │  }                                  │          │      │    │
          │  │  │  │         │                           │          │      │    │
          │  │  │  │  ┌──────▼──────────────────────┐     │          │      │    │
          │  │  │  │  │ SK Kernel.InvokePrompt      │     │          │      │    │
          │  │  │  │  │ StreamingAsync(prompt)      │     │          │      │    │
          │  │  │  │  │         │                   │     │          │      │    │
          │  │  │  │  │  ┌──────▼────────────┐      │     │          │      │    │
          │  │  │  │  │  │ OllamaThinking    │      │     │          │      │    │
          │  │  │  │  │  │ Handler           │      │     │          │      │    │
          │  │  │  │  │  │ 拦截POST /api/chat│      │     │          │      │    │
          │  │  │  │  │  │ EnableThinking?   │      │     │          │      │    │
          │  │  │  │  │  │ → inject "think"  │      │     │          │      │    │
          │  │  │  │  │  │    param in body  │      │     │          │      │    │
          │  │  │  │  │  └──────┬────────────┘      │     │          │      │    │
          │  │  │  │  │         │                   │     │          │      │    │
          │  │  │  │  │  ┌──────▼────────────┐      │     │          │      │    │
          │  │  │  │  │  │ llama-server      │      │     │          │      │    │
          │  │  │  │  │  │ GPU 推理 Qwen3-8B │      │     │          │      │    │
          │  │  │  │  │  │ → 返回流式token   │      │     │          │      │    │
          │  │  │  │  │  │   + FC tool_calls │      │     │          │      │    │
          │  │  │  │  │  └──────┬────────────┘      │     │          │      │    │
          │  │  │  │  │         │                   │     │          │      │    │
          │  │  │  │  │  ┌──────▼──────────────────────┐  │          │      │    │
          │  │  │  │  │  │ SK 自动解析 tool_calls,     │  │          │      │    │
          │  │  │  │  │  │ 调用 ChemicalCompliance 插件 │  │          │      │    │
          │  │  │  │  │  │         │                   │  │          │      │    │
          │  │  │  │  │  └──┬──┬──┬──┬──┬──┬──┬──┘    │  │          │      │    │
          │  │  │  │  │     │  │  │  │  │  │  │       │  │          │      │    │
          │  │  │  │  └─────┼──┼──┼──┼──┼──┼──┼───────┘  │          │      │    │
          │  │  │  │        │  │  │  │  │  │  │          │          │      │    │
          │  │  │  └────────┼──┼──┼──┼──┼──┼──┼──────────┘          │      │    │
          │  │  │           │  │  │  │  │  │  │                      │      │    │
          │  │  └───────────┼──┼──┼──┼──┼──┼──┼──────────────────────┘      │    │
          │  └──────────────┼──┼──┼──┼──┼──┼──┼─────────────────────────────┘    │
          └─────────────────┼──┼──┼──┼──┼──┼──┼──────────────────────────────────┘
                            │  │  │  │  │  │  │
            ┌───────────────┼──┼──┼──┼──┼──┼──┼─────────────────────┐
            │               │  │  │  │  │  │  │                     │
            ▼               ▼  ▼  ▼  ▼  ▼  ▼  ▼                     ▼
┌──────────────────────────────────────────────────────┐  ┌────────────────────┐
│ 7个 KernelFunction 的 RAG 检索链路                    │  │ 降级路径            │
│                                                      │  │                    │
│ ┌─ CheckHazardCategory("苯") ───────────────────┐    │  │ 1.RAG检索0条        │
│ │  1. NormalizeSubstanceName (别名→标准名)       │    │  │   → 硬编码字典     │
│ │  2. GetCachedOrRetrieveAsync(                  │    │  │                    │
│ │       "苯 危险类别 分类 规范", topK:3)          │    │  │ 2.ChemicalSubstance │
│ │     ├─ RagCache 命中? → 直接返回                │    │  │   Database查询     │
│ │     ├─ _kbService.RetrieveChemicalRegulation  │    │  │   (CAS/UN/闪点等)  │
│ │     │  Async()                                 │    │  │                    │
│ │     │  ├─ RetrieveAsync() 双路并行              │    │  │ 3.StorageIncompat-  │
│ │     │  │  ├─ BM25 (KnowledgeBaseService)       │    │  │   ibilities字典    │
│ │     │  │  └─ Vector (HybridKBService)          │    │  │                    │
│ │     │  │     ├─ Query Embedding (nomic-embed)  │    │  │ 4.SafetyDistances  │
│ │     │  │     └─ CosineSimilarity(GPU)          │    │  │   字典             │
│ │     │  ├─ RRF融合(k=60)                         │    │  │                    │
│ │     │  └─ 化工重排序(priority+termBonus)         │    │  │                    │
│ │     ├─ Reranker精排 (candidate>topK时)          │    │  │                    │
│ │     │  ├─ RemoteRerankAsync(bge-reranker-v2)   │    │  │                    │
│ │     │  └─ LocalHeuristicRerank (降级)           │    │  │                    │
│ │     └─ 缓存结果 → RagCache[key]=chunks          │    │  │                    │
│ │  3. FormatRagResult 格式化输出                   │    │  │                    │
│ │     ├─ ExtractRegulationRefs (提取GB编号)        │    │  │                    │
│ │     └─ TruncateChunk(MAX=300) (截断防幻觉)       │    │  │                    │
│ └─────────────────────────────────────────────────┘    │  │                    │
│                                                      │  │                    │
│ ┌─ GetSafetyDistance("储罐-建筑") ──────────────┐     │  │                    │
│ │  (额外处理)                                    │     │  │                    │
│ │  1. ExtractDistanceFromText (上下文定位+正则)  │     │  │                    │
│ │     regex: 不小于X米 / ≥Xm / 最小安全距离为Xm  │     │  │                    │
│ │  2. 输出: [DISTANCE: Xm] + [REGULATIONS: ...] │     │  │                    │
│ └───────────────────────────────────────────────┘     │  │                    │
└──────────────────────────────────────────────────────┘  └────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────┐
│ [5] 工具结果返回 → LLM 二次生成 → 最终回答            │
│                                                      │
│ SK 将工具返回的文本注入上下文 → Qwen3生成最终回答      │
│ → 流式返回给 LlmService                              │
│ → think标签过滤 (Qwen3: 自动non-thinking)            │
│ → Console输出 / API返回                               │
└──────────────────────────────────────────────────────┘
```

### 3.2 评测数据链路 (EvalEngine 全链路)

```
 ┌─────────────────────────────────────────────────────────────────────┐
 │                    评测入口: EvalEngine.RunComplianceEvalAsync()      │
 │                              Services/Eval/EvalEngine.cs :L32        │
 └───────────────┬─────────────────────────────────────────────────────┘
                 │
    ┌────────────▼────────────┐
    │ Layer1: FC就绪性检查     │
    │ LlmService              │
    │ .RunFcReadinessCheck    │
    │ Async() :L80            │
    │                         │
    │ 5条诊断用例:             │
    │ "苯属于什么危险类别"     │
    │ "苯和丙酮能同库储存吗"   │
    │ "甲类仓库与明火点的间距" │
    │ "现在几点"               │
    │ "甲醇和硝酸同库是否合规" │
    │                         │
    │ 至少1/5触发 → 进入Layer2│
    └────────────┬────────────┘
                 │
    ┌────────────▼─────────────────────────────────────────────────────┐
    │ Layer2: 逐条业务评测 (63条)                                       │
    │                                                                  │
    │  for i in 1..63:                                                 │
    │  ┌───────────────────────────────────────────────────────────┐   │
    │  │ Step A: 调用LLM                                            │   │
    │  │  ├─ info_query?  → AgentDialog.ExecuteEvalFastQueryAsync │   │
    │  │  ├─ compliance?  → AgentDialog.ExecuteEvalFastAsync      │   │
    │  │  │   → LlmService.InvokeNonStreamingWithRetryAsync       │   │
    │  │  │     ├─ 第1次: FC=Required, Temp=0.0, MaxTokens=512   │   │
    │  │  │     ├─ 第2-3次: FC=null, 工具结果内联重试              │   │
    │  │  │     └─ 超时捕获: 保存已成功工具结果供重试              │   │
    │  │  └─ result.LatencyMs = Stopwatch计时                     │   │
    │  │      result.TokenCount = 中文/2 + 英文/4 估算             │   │
    │  └───────────────────────────────────────────────────────────┘   │
    │  ┌───────────────────────────────────────────────────────────┐   │
    │  │ Step B: 工具匹配 (L175-208)                                │   │
    │  │  llmSvc.LastFunctionCalls → calledTools                    │   │
    │  │  result.tool_match = calledTools.Contains(expectedTool)    │   │
    │  │  result.param_match = CheckParams(actualArgs, expected)    │   │
    │  │    → 子串匹配 (大小写不敏感)                                │   │
    │  └───────────────────────────────────────────────────────────┘   │
    │  ┌───────────────────────────────────────────────────────────┐   │
    │  │ Step C: 结论匹配 (L195-201)                                │   │
    │  │  result.conclusion_match = CheckConclusion(               │   │
    │  │    response, expected, tool_match, category, intent,       │   │
    │  │    toolResult)                                             │   │
    │  │                                                            │   │
    │  │  信息查询路径:                                              │   │
    │  │    ├─ 空数据声明 → true                                    │   │
    │  │    ├─ 安全距离: CheckSafetyDistanceMatch(5%容差)           │   │
    │  │    └─ 法规编号: CheckRegulationMatch(GB归一化匹配)          │   │
    │  │                                                            │   │
    │  │  合规判断路径:                                              │   │
    │  │    ├─ [判定:is_compliant=true/false] 标签匹配              │   │
    │  │    └─ 关键词兜底: 合规/允许 ←→ 不合规/禁止/禁忌            │   │
    │  └───────────────────────────────────────────────────────────┘   │
    │  ┌───────────────────────────────────────────────────────────┐   │
    │  │ Step D: RAG检索质量评估 (L230)                             │   │
    │  │  EvaluateRetrievalQualityAsync(result, tc)                 │   │
    │  │                                                            │   │
    │  │  1. 独立检索: kbService.RetrieveChemicalRegulation        │   │
    │  │     Async(retrievalQuery, topK:5)                          │   │
    │  │                                                            │   │
    │  │  2. 构建relevanceIndicators:                               │   │
    │  │     预期相关文档 + 预期法规编号 + 预期参数值                │   │
    │  │                                                            │   │
    │  │  3. GB编号标准化后Contains匹配                              │   │
    │  │                                                            │   │
    │  │  4. 计算:                                                  │   │
    │  │     Precision@K = relevantCount / K                       │   │
    │  │     Recall@K = relevantCount / min(indicators, K)         │   │
    │  │     MRR = 1.0 / firstRelevantRank                         │   │
    │  └───────────────────────────────────────────────────────────┘   │
    │  ┌───────────────────────────────────────────────────────────┐   │
    │  │ Step E: 忠实度评估 (L242-294)                              │   │
    │  │                                                            │   │
    │  │ 1. ConclusionVerifier.VerifyAsync (法规幻觉检测)            │   │
    │  │    ├─ 空数据检测 (无数据/未检索到/数据不足)                 │   │
    │  │    ├─ GB编号格式提取 (正则)                                 │   │
    │  │    └─ KB反向检索验证每个GB编号                              │   │
    │  │       → 存在=VerifiedRegulations                           │   │
    │  │       → 不存在=HallucinatedRegulations                    │   │
    │  │                                                            │   │
    │  │ 2. EvaluateFaithfulnessAsync (逐条声明验证)                │   │
    │  │    ├─ ExtractConclusionContent: 移除RAG原文引用块           │   │
    │  │    ├─ ReflectionVerifier.VerifyBusinessFactsAsync          │   │
    │  │    │  ├─ 提取法规声明 (3种正则:国标/行政法规/条款)          │   │
    │  │    │  ├─ 去重 → KB反向检索 → FoundInSource标记             │   │
    │  │    │  └─ FactualPrecision = verified/regClaims             │   │
    │  │    └─ FaithfulnessScore = bizReport.FactualPrecision       │   │
    │  │                                                            │   │
    │  │ 3. EvaluateAnswerRelevanceAsync (LLM评分1-5)               │   │
    │  │    └─ 降级: 关键词匹配比例 → 映射到1-5                     │   │
    │  │                                                            │   │
    │  │ 4. EvaluateCitationAccuracy (法规引用验证)                  │   │
    │  │    └─ 提取回答中GB编号 → 验证是否在检索结果中出现           │   │
    │  └───────────────────────────────────────────────────────────┘   │
    │                                                                  │
    │  汇总 → EvalReport → JSON文件                                    │
    └──────────────────────────────────────────────────────────────────┘
```

---

## 四、代码文件引用关系图

```
                          ┌──────────────────────────────────────────────────────────────────┐
                          │                          引用关系图例                               │
                          │  ──→  依赖引用          ──▷  接口实现         ──►  继承/装饰       │
                          │  ═══  循环依赖(已解耦)  ...>  反射调用                                │
                          └──────────────────────────────────────────────────────────────────┘

Agent1/                                Agent1.Api/                        Agent1.Tests/
├── Program.cs ──→ AppConfig.cs        ├── Program.cs ──→ Agent1/        ├── ChemicalCompli-
│   │              Models/*.cs          │   │              Services/*      │   anceToolsTests.cs
│   │              Services/*           │   │              Models/*        ├── ChemicalSubstance
│   └──→ Services/                     │   │              Config/*         │   DatabaseTests.cs
│        │                             │   │              Middleware/*     ├── ConclusionVerifier
│        ├── AI/                       │   │                              │   Tests.cs
│        │   ├── ILlmService.cs        │   ├── Controllers/              ├── EvalEngineTests.cs
│        │   ├── LlmService.cs ──→     │   │   ├── AuthController.cs ──→ ├── IntentRouterTests.cs
│        │   │   │    Kernel           │   │   │   JWT(DB_PASSWORD)      ├── KnowledgeBase
│        │   │   │    ChemicalTools    │   │   └── ComplianceController  │   ServiceTests.cs
│        │   │   │    OllamaHandler    │   │       ──→ AgentDialog       ├── MetricsCollector
│        │   │   │    FunctionFilter   │   │           (FC调用)           │   Tests.cs
│        │   │   │    CircuitBreaker   │   ├── Middleware/               ├── SensitiveData
│        │   │   └──→ IToolService.cs  │   │   ├── GlobalException.cs    │   MaskerTests.cs
│        │   │                        │   │   ├── RateLimiting.cs       └── ToolServiceTests.cs
│        │   ├── IToolService.cs       │   │   ├── RequestId.cs
│        │   ├── ToolService.cs ──→    │   │   └── RequestMetrics.cs
│        │   │   tools:Chemical        │   │
│        │   │   llm:ILlmService       │   └── Services/  (复用 Agent1/)
│        │   │   kb:IKnowledgeBase     │
│        │   └── ReflectionVerifier.cs │
│        │       kb:IKnowledgeBase ──→ │
│        │                             │
│        ├── Compliance/               │
│        │   ├── ChemicalCompliance    │
│        │   │   Tools.cs ──→          │
│        │   │   kb:IKnowledgeBase     │
│        │   │   reranker:RerankerSvc  │
│        │   │   db:ChemSubstanceDB    │
│        │   ├── ConclusionVerifier    │
│        │   │   .cs (static) ──→      │
│        │   │   kb:IKnowledgeBase     │
│        │   └── SensitiveDataMasker   │
│        │                             │
│        ├── Dialog/                   │
│        │   └── AgentDialog.cs ──→    │
│        │       session:ISession      │
│        │       memory:IMemory        │
│        │       llm:ILlmService       │
│        │       tool:IToolService     │
│        │       coord:MemoryCoordinator│
│        │                             │
│        ├── Eval/                     │
│        │   └── EvalEngine.cs ──→     │
│        │       agentDialog           │
│        │       llm:ILlmService       │
│        │       kb:IKnowledgeBase     │
│        │       verifier:Reflection   │
│        │       Models/EvalModels.cs   │
│        │                             │
│        ├── Knowledge/                │
│        │   ├── IKnowledgeBaseSvc.cs  │
│        │   │        △ (接口)          │
│        │   ├─────────────────────    │
│        │   │                         │
│        │   ├── HybridKBService.cs ──▷│
│        │   │   db:IDatabase          │
│        │   │   llm:ILlmService       │
│        │   │   bm25:KnowledgeBaseSvc │
│        │   │   cache:QueryCacheSvc   │
│        │   │   ↓ 调用                │
│        │   ├── KnowledgeBaseSvc.cs   │
│        │   │   (纯BM25, 倒排索引)     │
│        │   ├── GpuVectorIndexSvc.cs  │
│        │   │   db:IDatabase          │
│        │   │   (全量内存向量索引)     │
│        │   ├── QueryCacheService.cs  │
│        │   │   (LRU+TTL, 线程安全)   │
│        │   ├── RerankerService.cs    │
│        │   │   (Cross-Encoder)       │
│        │   ├── ChemicalSubstance     │
│        │   │   Database.cs (static)  │
│        │   ├── DocExtractor.cs       │
│        │   ├── PdfExtractor.cs       │
│        │   ├── TextCleaner.cs        │
│        │   ├── SemanticChunker.cs    │
│        │   └── RetrievedChunk.cs     │
│        │                             │
│        └── Infrastructure/           │
│            ├── IInferenceModule.cs   │
│            ├── ModuleFactory.cs ──→  │
│            │   8个模块构造函数注入     │
│            ├── ModuleDispatcher.cs   │
│            │   (懒创建+字典缓存)      │
│            ├── DatabaseService.cs    │
│            │   (PostgreSQL+pgvector) │
│            ├── SessionService.cs     │
│            ├── MemoryService.cs      │
│            ├── ResponseCacheSvc.cs   │
│            ├── MetricsCollector.cs   │
│            └── MemoryCoordinator.cs  │
│                                      │
├── Config/                            │
│   ├── AppConfig.cs (350行) ← 单例    │
│   │   所有配置段的访问入口             │
│   └── ModelConfig.cs                 │
│                                      │
└── Models/                            │
    ├── EvalModels.cs (391行)          │
    ├── ChemicalSubstanceModels.cs     │
    ├── DialogTypes.cs                 │
    ├── LongTermMemoryModels.cs        │
    ├── ModuleType.cs                  │
    └── IntentRouter (意图分类)         │
```

---

## 五、传输导向图 — 请求全链路处理流程

### 5.1 Console 评测请求链路

```
  Program.Main() [Program.cs :L19]
  │
  ├── AppConfig.Load(configuration)           // 加载appsettings.json + 环境变量
  ├── Serilog 日志初始化                       // Console + File 双写
  ├── ConsoleTeeWriter (双写到 full-YYYYMMDD.log)
  │
  ├── ServiceCollection 注册                  // 15+ 单例服务
  │   ├── DatabaseService
  │   ├── SessionService, MemoryService
  │   ├── LlmService (null! → 延迟绑定)
  │   ├── ToolService, AgentDialog
  │   ├── HybridKnowledgeBaseService
  │   ├── IntegrationService, AuditService
  │   ├── ModuleFactory, ModuleDispatcher
  │   ├── ResponseCacheService
  │   └── LongTermMemoryService, MemoryCoordinator
  │
  ├── 延迟绑定 (循环依赖解耦)    [Program.cs]
  │   llmSvc.SetKnowledgeBaseService(kbService)
  │
  ├── 知识库加载 (快速模式)
  │   hybridKB.RebuildBm25FromDatabaseAsync()  // 从DB秒级重建BM25索引
  │   └── 如果数据库为空 → LoadChemicalKnowledgeBaseAsync()
  │       └── AddDocumentsBatchAsync()         // GPU批量嵌入+写入pgvector
  │
  ├── GPU向量索引初始化
  │   gpuIndex.InitializeAsync()               // 全量加载到内存
  │
  ├── 菜单循环 (用户选择)
  │   ├─ [12] "评测 → 50条业务评测"
  │   │   │
  │   │   └── EvalEngine.RunComplianceEvalAsync()
  │   │       │
  │   │       ├── Layer1: FC就绪性检查
  │   │       │   └── RunFcReadinessCheckAsync()
  │   │       │       ├── 5条诊断用例
  │   │       │       └── 每条: InvokeNonStreamingWithRetryAsync(prompt)
  │   │       │           ├── 第1次: SK Kernel (FC=Required)
  │   │       │           │   → llama-server /api/chat → tool_calls
  │   │       │           │   → SK 自动执行 ChemicalCompliance 插件
  │   │       │           │   → LastFunctionCalls 记录 (FunctionCallDiagnosticsFilter)
  │   │       │           └── 第2-3次: 禁用FC, 工具结果内联到prompt
  │   │       │
  │   │       └── Layer2: 逐条业务评测 (for i in 0..62)
  │   │           │
  │   │           └── AgentDialog.ExecuteEvalFastAsync(tc.Query)
  │   │               └── ExecuteEvalInternalAsync(prompt, "评测")
  │   │                   └── LlmService.InvokeNonStreamingWithRetryAsync(prompt)
  │   │                       │
  │   │                       ├── [检查] CheckCircuitBreaker()
  │   │                       │   ├── 连续失败≥3次? → 冷却期未过?
  │   │                       │   │   → CircuitBreakerOpenException → 快速失败
  │   │                       │   └── 冷却已过 → 半开, 允许试探
  │   │                       │
  │   │                       ├── [第1次] SK Auto Function Calling
  │   │                       │   └── _kernel.InvokePromptAsync(prompt, settings)
  │   │                       │       │ settings: {FC=Required, Temp=0.0, MaxTokens=512}
  │   │                       │       │
  │   │                       │       ├── [HTTP层] OllamaThinkingHandler
  │   │                       │       │   拦截 POST → llama-server /api/chat
  │   │                       │       │   EnableThinking=false → 不注入think参数
  │   │                       │       │   Qwen3自动non-thinking模式
  │   │                       │       │
  │   │                       │       ├── [LLM层] llama-server GPU推理
  │   │                       │       │   解析prompt → 判断需要调用工具
  │   │                       │       │   → 返回 tool_calls JSON
  │   │                       │       │
  │   │                       │       ├── [SK层] 解析tool_calls, 自动执行
  │   │                       │       │   FunctionCallDiagnosticsFilter.OnFunctionInvocation
  │   │                       │       │   → ChemicalCompliance.* (7个KernelFunction之一)
  │   │                       │       │   → LastFunctionCalls.Add(record)
  │   │                       │       │
  │   │                       │       ├── [工具层] 以 CheckHazardCategory("苯") 为例
  │   │                       │       │   ├─ NormalizeSubstanceName("苯")
  │   │                       │       │   ├─ GetCachedOrRetrieveAsync("苯 危险类别 分类 规范")
  │   │                       │       │   │   ├─ RagCache 查询 (同化学品不重复检索)
  │   │                       │       │   │   ├─ HybridKBService.RetrieveChemicalRegulation
  │   │                       │       │   │   │   ├─ RetrieveAsync() → Cache查询
  │   │                       │       │   │   │   ├─ ExpandQuery() → 同义词+GB编号
  │   │                       │       │   │   │   ├─ [并行] BM25 检索 (倒排索引)
  │   │                       │       │   │   │   │   Tokenize → ngram → BM25打分
  │   │                       │       │   │   │   ├─ [并行] Vector 检索
  │   │                       │       │   │   │   │   GetEmbeddingAsync(nomic-embed)
  │   │                       │       │   │   │   │   GpuVectorSearchAsync(余弦相似度)
  │   │                       │       │   │   │   ├─ RRF融合 (k=60)
  │   │                       │       │   │   │   └─ Cache写入
  │   │                       │       │   │   ├─ 化工重排序 (priority+termBonus)
  │   │                       │       │   │   └─ RerankerService.RerankAsync (可选)
  │   │                       │       │   ├─ FormatRagResult (格式化+截断MAX=300)
  │   │                       │       │   └─ 降级: 硬编码HazardCategories字典
  │   │                       │       │
  │   │                       │       └── [结果回传] LLM收到工具返回文本 → 二次生成
  │   │                       │           → 最终回答 (含合规结论)
  │   │                       │
  │   │                       ├── [熔断成功] RecordCircuitSuccess() → 重置计数器
  │   │                       ├── [失败处理] RecordCircuitFailure()
  │   │                       │   consecutiveFailures++ → 达阈值时 CircuitOpen
  │   │                       │
  │   │                       ├── [第2-3次重试] (若首次成功但返回为空或超时)
  │   │                       │   ├─ 禁用FC (FunctionChoiceBehavior=null)
  │   │                       │   └─ 首次工具结果内联到prompt
  │   │                       │       prompt + "\n【已获取的工具调用结果】\n" + toolResults
  │   │                       │
  │   │                       └── [记录] MetricsCollector.RecordLlmCall(duration, success)
  │   │
  │   └─ [其他菜单] "对话/ReAct/Reflection/CoT/RAG/合规检查"
  │       └── ModuleDispatcher.ExecuteModuleAsync(type)
  │           └── ModuleFactory.CreateModule(type) → module.RunAsync()
  │
  └── 运行结束
```

### 5.2 API 请求链路

```
  HTTP Client
  │
  ├── [Middleware Pipeline]
  │   ├─ RequestIdMiddleware     → 注入 X-Request-Id
  │   ├─ RequestMetricsMiddleware → MetricsCollector.RecordApiRequest()
  │   ├─ RateLimitingMiddleware  → SemaphoreSlim(2,2) 并发限流
  │   └─ GlobalExceptionMiddleware
  │       ├─ CircuitBreakerOpenException → HTTP 503
  │       ├─ JWT验证失败 → HTTP 401
  │       └─ 其他异常 → HTTP 500
  │
  ├── POST /api/auth/login
  │   AuthController.Login()
  │   → JWT Token 生成 (密钥来自 DB_PASSWORD 环境变量)
  │
  ├── POST /api/compliance/check  [需 Bearer Token]
  │   ComplianceController.Check()
  │   ├─ JWT 验证 (ValidateToken)
  │   ├─ 构造 prompt (EvalFastPrompt 模板)
  │   └─ AgentDialog.ExecuteEvalFastAsync(userInput)
  │       └── [与Console评测相同链路]
  │           LlmService.InvokeNonStreamingWithRetryAsync
  │           → SK Auto FC → ChemicalCompliance 工具
  │           → 返回 JSON { result, tools, metrics }
  │
  └── [OpenTelemetry]
      ├─ Metrics: /metrics (Prometheus)
      └─ Traces: 全链路追踪
```

---

## 六、实现细节速查表

| 模块 | 文件 | 关键方法 | 行号 |
|------|------|----------|------|
| **DI容器** | `Agent1/Program.cs` | `Main()`, ServiceCollection 注册 | 19-130 |
| **配置** | `Agent1/Config/AppConfig.cs` | `Load()`, `Validate()` | 1-350 |
| **FC调用** | `Agent1/Services/AI/LlmService.cs` | `InvokeStreamAsync()` | 85-216 |
| **非流式FC** | `Agent1/Services/AI/LlmService.cs` | `InvokeNonStreamingWithRetryAsync()` | 359-484 |
| **FC诊断** | `Agent1/Services/AI/LlmService.cs` | `FunctionCallDiagnosticsFilter` | 992-1032 |
| **Thinking控制** | `Agent1/Services/AI/LlmService.cs` | `OllamaThinkingHandler` | 927-962 |
| **反射注入** | `Agent1/Services/AI/LlmService.cs` | `InjectThinkingHandler()` | 798-835 |
| **熔断器** | `Agent1/Services/AI/LlmService.cs` | `CheckCircuitBreaker()` | 501-521 |
| **批量嵌入** | `Agent1/Services/AI/LlmService.cs` | `GetEmbeddingsBatchAsync()` | 705-783 |
| **7个工具** | `Agent1/Services/Compliance/ChemicalComplianceTools.cs` | `CheckHazardCategory()` 等 | 172-596 |
| **工具缓存** | `Agent1/Services/Compliance/ChemicalComplianceTools.cs` | `GetCachedOrRetrieveAsync()` | 52-80 |
| **BM25引擎** | `Agent1/Services/Knowledge/KnowledgeBaseService.cs` | `RetrieveAsync()`, `Tokenize()` | 164-295 |
| **混合检索** | `Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs` | `HybridRetrieveAsync()` | 497-563 |
| **向量检索** | `Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs` | `GpuVectorSearchAsync()` | 439-477 |
| **RRF融合** | `Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs` | RRF k=60 | 518-544 |
| **GPU索引** | `Agent1/Services/Knowledge/GpuVectorIndexService.cs` | `InitializeAsync()`, `Search()` | 50-150 |
| **查询缓存** | `Agent1/Services/Knowledge/QueryCacheService.cs` | `TryGet()`, `Set()` | 58-121 |
| **Reranker** | `Agent1/Services/Knowledge/RerankerService.cs` | `RerankAsync()` | 54-84 |
| **化学品DB** | `Agent1/Services/Knowledge/ChemicalSubstanceDatabase.cs` | `Lookup()`, `CheckCompatibility()` | 45-100 |
| **评测引擎** | `Agent1/Services/Eval/EvalEngine.cs` | `RunComplianceEvalAsync()` | 32-417 |
| **检索评估** | `Agent1/Services/Eval/EvalEngine.cs` | `EvaluateRetrievalQualityAsync()` | 506-590 |
| **结论判断** | `Agent1/Services/Eval/EvalEngine.cs` | `CheckConclusion()` | 846-918 |
| **忠实度** | `Agent1/Services/AI/ReflectionVerifier.cs` | `VerifyBusinessFactsAsync()` | 118-183 |
| **幻觉检测** | `Agent1/Services/Compliance/ConclusionVerifier.cs` | `VerifyAsync()` | 31-116 |
| **6步流水线** | `Agent1/Services/Dialog/AgentDialog.cs` | `ExecuteAsync()` | 54-79 |
| **模块调度** | `Agent1/Services/Infrastructure/ModuleDispatcher.cs` | `ExecuteModuleAsync()` | 20-41 |
| **API入口** | `Agent1.Api/Program.cs` | DI + Middleware | 1-438 |
| **API控制器** | `Agent1.Api/Controllers/ComplianceController.cs` | `Check()` | - |
| **指标收集** | `Agent1/Services/Infrastructure/MetricsCollector.cs` | `RecordLlmCall()` 等 | 33-53 |

---

## 七、前端表现层与后端架构对齐

### 7.1 三通道入口 → 统一调度

```
┌─────────────────────────────────────────────────────────────────────┐
│                     前端 agent1-web (Vue 3 + Vite 5)                 │
├─────────────────────────────────────────────────────────────────────┤
│  [路由守卫]  →  router.beforeEach 检查 authStore.hasPermission()    │
│  [状态管理]  →  8 个 Pinia Store 对应 12 个 ModuleType 的业务状态   │
│  [数据获取]  →  @tanstack/vue-query useQuery/useMutation            │
│  [HTTP层]    →  lib/axios.ts JWT拦截器 → Agent1.Api:5000            │
├─────────────────────────────────────────────────────────────────────┤
│  ↓ Vite Proxy / SSH Tunnel ↓                                        │
├─────────────────────────────────────────────────────────────────────┤
│                    Agent1.Api (ASP.NET Core)                          │
│  Middleware → Controller → [第2层 ModuleDispatcher] → ...            │
└─────────────────────────────────────────────────────────────────────┘
```

### 7.2 前端路由 → ModuleType 映射表（架构收敛关键）

| 前端路由 | 权限 | 后端 ModuleType | Controller | HTTP 动词 |
|----------|------|:--------------:|------------|-----------|
| `/login` | Public | — | AuthController | POST |
| `/dashboard` | admin,auditor | — | ComplianceController | GET summary |
| `/compliance` | admin,auditor | **ComplianceCheck (8)** | ComplianceController | POST check |
| `/inspection` | admin,auditor | **ComplianceCheck (8)** | InspectionController | GET plans |
| `/inspection/:id/execute` | admin,auditor | **ComplianceCheck (8)** | InspectionController | POST execute |
| `/tickets` | admin,auditor | **TicketFollowup (9)** | TicketsController | GET list |
| `/tickets/:id` | admin,auditor | **TicketFollowup (9)** | TicketsController | PUT status |
| `/assets` | admin,auditor | **ComplianceCheck (8)** | InspectionController | GET assets |
| `/audit` | admin | — | AuditService | — |
| `/system` | admin,auditor | — | HealthController | GET |

### 7.3 前端测试金字塔对齐后端分层

```
         ╱  Playwright E2E  ╲       ← L3: 对应后端 第1→6层全链路
        ╱  (5条关键路径)     ╲
       ╱  组件测试 (Vitest)   ╲     ← L2: 对应后端 第1层 Controller
      ╱   (6个核心组件)        ╲
     ╱   单元测试 (Vitest)      ╲   ← L1: 对应后端 第2→4层逻辑
    ╱    (8个Store + Utils)     ╲
```

---

## 八、CI/CD 持续集成层

### 8.1 GitHub Actions 七阶段质量流水线

```
git push (main/develop/feature)  →  .github/workflows/ci.yml
│
├── [Job1] build-and-test         ← dotnet build + test (排除Integration)
│     ├─ coverlet 覆盖率收集 (cobertura格式)
│     ├─ 60% 门禁阻断 (<60% → build fail)
│     ├─ 覆盖率回归检测 (下降>2% → build fail)
│     ├─ 架构收敛测试 dotnet run --project ArchitectureTest
│     └─ 上传覆盖率 artifact (retention: 30天)
│
├── [Job1b] frontend-test         ← npx vitest run (Vitest + jsdom)
│
├── [Job2] integration-test       ← PostgreSQL service容器 (pgvector/pg16)
│     ├─ psql init_database.sql
│     └─ dotnet test --filter "Category=Integration"
│
├── [Job3] docker (main only)     ← docker/build-push-action → GHCR
│
├── [Job4] benchmark              ← dotnet run Benchmark (P95基线回归)
│     ├─ 心跳轮询: curl -sf /health/live (30×2s)
│     ├─ P95退步>50% → 阻断
│     └─ 错误率>5% → 阻断
│
├── [Job5] staging-deploy         ← SSH → docker-compose pull + up
│     └─ 冒烟测试: curl /health/live (12×5s)
│
└── [Job7] notify                 ← QQ邮箱双通道通知
      ├─ ✅ 成功摘要: 覆盖率 + 测试数 + 性能
      └─ 🚨 失败告警: 失败Job详情 + 工作流链接
```

### 8.2 架构收敛测试在CI中的定位

- **位置**: [Job1] build-and-test → Architecture Convergence Test
- **当前状态**: `continue-on-error: true` (不阻断构建) → 🔴 待改进
- **目标状态**: `continue-on-error: false` → 架构退化即阻断
- **覆盖**: 7个验证点 × ArchitectureTest/Program.cs
