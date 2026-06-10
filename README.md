# AgentSystem - 化工园区危化品合规审查 AI Agent

基于 .NET 8 + Semantic Kernel + Ollama 构建的企业级化工园区危化品合规审查 AI Agent。已完成生产级容器化部署，支持 PostgreSQL+pgvector 混合检索、JWT 认证、速率限制、OpenTelemetry 可观测性。

## 🏗️ 项目架构

```
├── Agent1/                       # 核心类库
│   ├── Models/                   # 数据模型
│   │   ├── ChemicalSubstanceModels.cs  # 化学品属性/法规版本/安全距离
│   │   ├── EvalModels.cs         # 评测数据模型
│   │   ├── DialogTypes.cs        # 对话类型
│   │   ├── LongTermMemoryModels.cs     # 长期记忆模型
│   │   └── ModuleType.cs         # 模块枚举
│   ├── Modules/                  # 推理模块
│   │   ├── RAGModule.cs          # RAG 检索增强生成
│   │   ├── CoTSolidModule.cs / CoTStreamModule.cs  # CoT 思维链
│   │   ├── ReActSolidModule.cs / ReActStreamModule.cs  # ReAct 推理
│   │   ├── ReflectionModule.cs   # 自我反思纠错
│   │   └── ComplianceCheckModule.cs / UnifiedDialogModule.cs
│   ├── Services/
│   │   ├── AI/                   # LLM 服务
│   │   │   └── LlmService.cs     # Ollama 集成/Thinking控制/熔断器/重试
│   │   ├── Compliance/           # 化工合规
│   │   │   ├── ChemicalComplianceTools.cs  # 7 个 SK Plugin 工具
│   │   │   ├── ChemicalSubstanceDatabase.cs # 30+ 危化品结构化数据库
│   │   │   └── ChemicalRAG.cs    # 化工 RAG 管道
│   │   ├── Knowledge/            # 知识库
│   │   │   ├── KnowledgeBaseService.cs          # BM25 检索
│   │   │   ├── HybridKnowledgeBaseService.cs    # BM25+向量混合检索
│   │   │   ├── PdfExtractor.cs / DocExtractor.cs # 文档解析
│   │   │   ├── TextCleaner.cs / SemanticChunker.cs  # 清洗分块
│   │   │   └── RetrievedChunk.cs # 检索结果模型
│   │   ├── Dialog/               # 对话管理
│   │   │   ├── AgentDialog.cs    # Agent 对话编排
│   │   │   ├── SessionManager.cs / SessionService.cs
│   │   │   └── IntentRouter.cs   # 意图路由
│   │   ├── Memory/               # 记忆系统
│   │   │   ├── MemoryService.cs  # 短期记忆
│   │   │   ├── MemoryCoordinator.cs  # 记忆协调器
│   │   │   └── ResponseCacheService.cs # 响应缓存(SHA256 键/TTL)
│   │   ├── Infrastructure/       # 基础设施
│   │   │   ├── DatabaseService.cs # PostgreSQL + pgvector
│   │   │   ├── AuditService.cs    # 等保三级审计
│   │   │   ├── MetricsCollector.cs # Prometheus 指标
│   │   │   └── SensitiveDataMasker.cs # 数据脱敏
│   │   └── Eval/                 # 评测引擎
│   │       ├── EvalEngine.cs
│   │       ├── ConclusionVerifier.cs  # Post-hoc 结论验证
│   │       └── ReflectionVerifier.cs  # 代码级反思验证
│   ├── Config/
│   │   ├── AppConfig.cs          # 配置中心（外部化）
│   │   └── ModelConfig.cs        # 模型配置入口
│   └── Program.cs                # 控制台入口
├── Agent1.Api/                   # Web API 层
│   ├── Controllers/
│   │   ├── AuthController.cs     # JWT 登录/刷新（BCrypt + RefreshToken 轮转）
│   │   └── ComplianceController.cs # 合规检查/Hazard查询/储存兼容性
│   ├── Middleware/
│   │   ├── GlobalExceptionMiddleware.cs    # 全局异常处理
│   │   ├── RateLimitingMiddleware.cs       # 速率限制(100次/分钟/IP)
│   │   ├── RequestIdMiddleware.cs          # 请求ID透传
│   │   └── RequestMetricsMiddleware.cs     # 请求指标
│   └── Program.cs                # API 启动（DI/Serilog/JWT/OTel/健康检查）
├── Agent1.Tests/                 # xUnit 测试（133 tests）
├── Benchmark/                    # C# HTTP 压测工具
├── prometheus/                   # Prometheus 录制规则
├── grafana/                      # Grafana 仪表盘 JSON
├── .github/workflows/            # CI/CD（构建→测试→Docker→GHCR）
├── Data/ComplianceEvalSet.json   # 64 条化工合规评测集
├── knowledgebase/                # 化工合规知识库
│   ├── 国标/ 园区规则/ 历史案例/ 化工专业条例/
│   └── H166-危险化学品化工企业安全生产三级标准化/
├── docker-compose.yml            # 一键部署（PostgreSQL + API）
├── Dockerfile                    # 多阶段构建（非 root 运行）
└── Agent1.sln                    # 解决方案文件
```

## 🛠️ 技术栈

| 层级 | 技术 | 版本 | 状态 |
|------|------|------|------|
| 语言 | C# | 12.0 | ✅ |
| 框架 | .NET | 8.0 | ✅ |
| AI 框架 | Semantic Kernel | 1.74.0 | ✅ |
| 本地 LLM | Ollama (qwen3:8b) | latest | ✅ |
| 推理模型 | Qwen3-8B (Q4_K_M) | 8B | ✅ |
| 嵌入模型 | nomic-embed-text | latest | ✅ |
| 数据库 | PostgreSQL + pgvector | 16.x | ✅ |
| PDF 解析 | PdfPig | 0.1.9 | ✅ |
| DOCX 解析 | DocumentFormat.OpenXml | 3.2.0 | ✅ |
| 认证 | JWT Bearer + BCrypt | 8.0+ | ✅ |
| 速率限制 | 自定义 Middleware | - | ✅ |
| 结构化日志 | Serilog + Seq | 4.0+ | ✅ |
| 指标监控 | Prometheus Text Format | - | ✅ |
| 可视化 | Grafana 仪表盘 | latest | ✅ |
| 分布式追踪 | OpenTelemetry | 1.9+ | ✅ |
| CI/CD | GitHub Actions → GHCR | - | ✅ |
| 容器化 | Docker multi-stage | 26.x | ✅ |
| 负载测试 | Benchmark 模块 | 内置 | ✅ |

## ✨ 核心功能

### 1. 推理引擎模块
- **RAG检索增强生成**：基于 BM25 算法实现文档检索
- **CoT思维链推理**：支持同步/流式输出
- **ReAct交互式推理**：支持工具调用与反馈循环
- **合规规则验证**：集成化工行业合规知识库

### 2. 会话管理
- 基于内存的对话历史管理
- 支持多轮对话上下文保持

### 3. 知识库管理
- 支持国标、园区规则、历史案例三级知识体系
- BM25 + 向量混合检索策略
- 业务优先级重排序（国标>园区规则>历史案例）

### 4. 合规审查能力
- 危化品存储合规检查
- 动火作业许可预审
- 安全距离合规验证
- 历史案例相似匹配

### 5. 结构化化学品数据库（★ Task 10 新增）
- 30+ 常见工业危化品结构化属性（CAS号/UN编号/分子式/闪点/沸点/爆炸极限）
- 危险类别与 GB 30000 标准号精确映射
- 20+ 精确化学品储存禁忌配对规则 + 类别级自动推断
- 20 对安全距离规则（GB 50160 / GB 50016）
- GB 18218 重大危险源临界量集成
- 8 项关键法规标准版本追踪（GB 15603/18218/30871 等）
- 40+ 化学品别名自动归一化

### 6. AI 工具集（7 个 KernelFunction）
- `CheckHazardCategory` — 危险类别查询
- `CheckStorageCompatibility` — 储存兼容性检查
- `GetSafetyDistance` — 安全距离查询
- `LookupChemicalProperties` — 化学品全属性查询（★新增）
- `GetMajorHazardThreshold` — GB 18218 重大危险源临界量（★新增）
- `CheckRegulationVersion` — 法规版本状态查询（★新增）
- `GetCurrentTime` / `Calculate` — 通用工具

## 📁 文档结构

```
docs/
├── architecture/              # 架构设计文档 (13个文件)
│   ├── 架构设计文档.md
│   ├── 化工园区危化品合规审核AI Agent架构整改方案.md
│   ├── 化工园区危化品合规审核AI Agent架构适配方案.md
│   ├── ModelScope模型选型决策框架.md
│   └── ...
├── articles/                  # 技术文章与参数注入方案 (4个文件)
│   ├── Semantic_Kernel_Ollama_enable_thinking_参数注入方案探讨.md
│   └── ollama-api-chat-think-*.png
├── technical-principles/      # 技术原理文档 (9个文件)
│   ├── BM25 参数：权重平衡的关键探索.md
│   ├── C# 内存模型、LINQ、BM25 和 NGram 详解.md
│   ├── 化工园区危化品合规审核RAG系统技术原理深度解析.md
│   └── ...
├── testing/                   # 测试文档 (4个文件)
├── troubleshooting/           # 故障排查文档 (4个文件)
├── learning-notes/            # 学习笔记 (4个文件)
├── project/                   # 项目文档 (4个文件)
├── Agent1 十项核心技术决策深度拆解.md
├── FunctionCalling模型评测BUG记录.md
├── 别小看这两个for循环！中文RAG检索的底层核心解法.md
├── 🔴 断点地图：RAG 全链路深度理解.md
└── README.md                  # 文档库索引
```

## 🚀 快速开始

### 前置条件
- Docker Desktop（⭐ 推荐，数据库免安装）
- Ollama（本地 LLM 推理，需拉取 `qwen3:8b` 和 `nomic-embed-text:latest`）

### Docker 一键部署（推荐）

```bash
# 1. 克隆项目
git clone https://gitee.com/liuchao_yue/agent-system.git && cd agent-system

# 2. 配置密钥（生产环境必做，开发可用默认值）
cp .env.example .env
# 编辑 .env 填入你的密码和 JWT 密钥

# 3. 启动 Ollama（如未运行）
ollama serve

# 4. 一键启动（PostgreSQL + API 容器）
docker-compose up -d --build

# 5. 验证部署
curl http://localhost:8080/health/live
# → "Healthy"
```

首次启动后，知识库会自动加载嵌入向量（~4528 条文档，约 5-10 分钟）。
通过 `docker logs -f chemical_agent_api` 可查看进度。

### API 端点

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| `POST` | `/api/auth/login` | 登录获取 Token | 否 |
| `POST` | `/api/auth/refresh` | 刷新 Token | Bearer |
| `POST` | `/api/compliance/hazard/query` | 危化品危险类别查询 | Bearer |
| `POST` | `/api/compliance/storage/check` | 储存兼容性检查 | Bearer |
| `POST` | `/api/compliance/check` | 合规综合检查 | Bearer |
| `GET` | `/health` | 全量健康检查 | 否 |
| `GET` | `/health/live` | 存活检查 | 否 |
| `GET` | `/health/ready` | 就绪检查 | 否 |
| `GET` | `/metrics` | Prometheus 指标 | 否 |
| `GET` | `/swagger` | Swagger 文档 | 否 |

调用示例：
```bash
# 1. 登录
curl -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"your_password"}'

# 2. 查询危化品（用返回的 token）
curl -X POST http://localhost:8080/api/compliance/hazard/query \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯"}'
```

### 本地开发（不依赖 Docker）

```bash
# 1. 安装 PostgreSQL 16 + pgvector，创建数据库
# 2. 配置 .env（同上）
# 3. 启动 API
dotnet run --project Agent1.Api
```

## 🔐 生产环境安全配置

所有敏感信息通过 `.env` 环境变量注入，**绝不**硬编码或提交 Git：

| 变量 | 说明 | 生产要求 |
|------|------|----------|
| `DB_PASSWORD` | PostgreSQL 密码 | **强制设置**，否则启动失败 |
| `JWT_KEY` | JWT 签名密钥（≥32字符） | **强制设置**，否则启动失败 |
| `AUTH_ACCOUNTS_JSON` | 账号列表 JSON | **强制设置**，否则拒绝开发默认账号 |
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | 设为 `Production` |

AUTH_ACCOUNTS_JSON 格式：
```json
[{"Username":"admin","Password":"xxx","Role":"admin"},{"Username":"auditor","Password":"xxx","Role":"auditor"}]
```

> ⚠️ 生产环境启动时会检查以上三项，不满足则直接 `Exit(1)` 拒绝启动。

## 📊 可观测性

```
http://localhost:8080/metrics               # Prometheus 指标
http://localhost:9090                        # Prometheus UI（需单独启动）
http://localhost:3000                        # Grafana（需单独启动）
```

Prometheus 录制规则位于 `prometheus/` 目录，Grafana 仪表盘 JSON 位于 `grafana/` 目录。

## 🔄 CI/CD

GitHub Actions 工作流（`.github/workflows/ci.yml`）：
```
Push → Build (.NET 8) → Test (133 tests) → Docker Build → Push GHCR
```

镜像自动推送到 `ghcr.io/<org>/agent-system:latest`。

## 📚 学习路径

**初学者路径**：
1. 先看 learning-notes/ 了解学习过程
2. 再看 architecture/ 理解整体架构
3. 然后看 technical-principles/ 深入技术原理

**架构师路径**：
1. 先看 architecture/ 掌握架构设计
2. 再看 technical-principles/ 深入技术细节
3. 最后看 testing/ 和 troubleshooting/ 了解验证与改进

## 📋 软考知识点映射

本项目覆盖软考「系统架构设计师」核心考点：
- 软件架构设计（分层架构、策略模式、依赖注入）
- 信息检索系统（BM25算法、倒排索引、向量检索）
- 知识管理与知识图谱
- 系统安全与等保三级

## 📝 许可证

MIT License

---

**文档版本**：v3.0  
**最后更新**：2026年6月10日  
**状态**：生产级容器化部署完成 | JWT 认证 | 速率限制 | CI/CD | OpenTelemetry | 133 tests 全通过

## 📋 近期更新

### Task 10: 化工知识库专业覆盖增强（2026-06-06）
- **新增** `Models/ChemicalSubstanceModels.cs` — 5 个数据模型：ChemicalSubstance / HazardCategoryRef / RegulationVersion / StorageIncompatibilityRule / SafetyDistanceRule
- **新增** `Services/ChemicalSubstanceDatabase.cs` — 30+ 危化品结构化属性数据库（~1000 行）
  - 核心字段: CAS号 / UN编号 / 分子式 / 闪点 / 沸点 / 爆炸极限(LEL-UEL) / 自燃温度 / 相对密度 / 蒸气密度
  - 危险类别精确映射 GB 30000 标准号 + 子类别
  - 储存禁忌类别列表 + 精确化学品配对规则（20 条）
  - GB 18218 重大危险源临界量（苯 50t / 氯 5t / 氨 10t / 氰化钠 1t 等）
  - 40+ 别名自动归一化（双氧水→过氧化氢、液氯→氯、烧碱→氢氧化钠 等）
  - 20 对安全距离规则（甲类仓库-明火点 30m、氯气储存区-居住区 200m 等）
  - 8 项关键法规版本追踪（GB 15603: 2022版 / GB 18218: 2018版 等）
- **扩充** `ChemicalComplianceTools.cs` — 3 个新 KernelFunction 工具:
  - `LookupChemicalProperties` — 化学品全属性查询（含安全提示）
  - `GetMajorHazardThreshold` — GB 18218 重大危险源临界量查询
  - `CheckRegulationVersion` — 法规版本状态 + 全文收录情况查询
  - 降级方法升级为数据库优先：Fallback 先查结构化数据库，再降级到通用关键词匹配
- **扩充** `Data/ComplianceEvalSet.json` — 64 条评测用例（+17 条新增：属性查询 G 系列 + 重大危险源 H 系列 + 法规版本 I 系列）
- **新增** `Agent1.Tests/ChemicalSubstanceDatabaseTests.cs` — 56 个测试（查询/别名/兼容性/安全距离/法规版本/数据质量/评测集覆盖）
- **效果**: 化工知识库从 27 条硬编码 → 30+ 结构化数据库；评测集 31 种化学品 100% 覆盖；133 tests 全通过

### Phase 2a 评测体系生产级修复（2026-06-06）
- **P0-1 强制工具调用**：`LlmService.cs` 中 `FunctionChoiceBehavior.Auto()` → `Required()`，LLM 不再能跳过工具调用
- **P0-2 意图路由分离**：评测路径按 `info_query` / `compliance_judgment` 分流，信息查询用例使用 `EvalFastQueryPrompt`（禁止合规判断），合规判断用例使用 `EvalFastPrompt`
- **P0-3 评测器结构化对比**：`CheckConclusion` 重写为 intent 驱动三层对比 — 安全距离数值容差比对（±5% 或 ±1m）、法规编号精确匹配、结构化标签 `[判定:is_compliant=...]` 解析
- **P1-1 Citation 结构化输出**：`ChemicalComplianceTools.FormatRagResult` 自动从 chunk 元数据和内容中提取 `[REGULATIONS: ...]` 标签；降级方法同步输出结构化标签；Prompt 增加反幻觉指令（禁止编造法规编号）
- **P1-2 KB 反向验证**：`ConclusionVerifier.VerifyAsync` 新增知识库反向检索 — 对 LLM 输出的每个法规编号到 KB 验证是否存在，替代静态 `KnownGbPrefixes` 白名单；评测循环中集成实时幻觉检出
- **评测集升级**：`ComplianceEvalSet.json` v1.1 — 50 条用例增加 `intent`、`expected_regulation_number`、`expected_clause`、`expected_distance` 等结构化字段
- **影响指标**：工具触发率预期 78% → 95%+；安全距离结论准确率预期 0% → 显著提升；法规编号幻觉可实时检出

### Phase 2a 评测增强：结构化判定标签 + Ollama Thinking 控制（2026-06-04）
- **新增** `LlmService.OllamaThinkingHandler` — DelegatingHandler 拦截器，自动向 Ollama `/api/chat` 请求注入 `think` 参数
  - Qwen3 默认启用思考模式（生成大量 `<think>` token 导致超时），Function Calling/评测场景需关闭，ReAct/Reflection 场景需开启
  - `EnableThinking` 属性按场景动态控制（ReAct/Reflection=true，RAG/评测=false）
- **新增** `LlmService.InjectOllamaThinkingHandler` + `FindHttpClientField` — 通过反射递归搜索 SK 内部 HttpClient 并注入 handler（兼容 SK 1.74.0-alpha 装饰器模式）
- **修改** `ChemicalComplianceTools.cs` — 所有 Fallback 工具输出追加 `[判定:is_compliant=...]` 结构化标签（true/false/unknown/待核实/依据原文）
- **重构** `Program.CheckConclusion` — 优先解析 `[判定:is_compliant=...]` 标签进行结论匹配，非二元标签（unknown/待核实）不参与强制匹配，无标签时回退改良版关键词匹配
- **修改** `appsettings.json` — `EvalFastPrompt` 末尾追加判定标签输出要求
- **效果**：评测结论匹配从纯关键词猜测升级为结构化标签解析，消除工具输出中的误报/漏报；Qwen3 思考模式可按需开关，避免 Function Calling 超时

### Phase 2a 模型评测：50条业务基准量化（2026-06-03）
- **模型切换**：`deepseek-r1:local7b`(不支持Function Calling) → `qwen3:8b-eval`(Q4_K_M量化, n_ctx=8192, n_batch=1024)
- **新增** `Data/ComplianceEvalSet.json` — 50条化工合规评测集（危险类别查询15条 + 储存兼容性20条 + 安全距离8条 + 综合审核7条）
- **新增** `Modelfile.qwen3-eval` — Ollama CPU推理加速配置（num_thread=12, n_ctx=8192, n_batch=1024）
- **新增** `Config/AppConfig.EvaluationConfig` — 评测配置（数据集路径、用例间隔、报告输出）
- **新增** 评测引擎（`Program.cs.RunComplianceEval`）— 工具触发率 + 参数准确率 + 结论准确率 三维度量化
- **新增** `LlmService.InvokeNonStreamingWithRetryAsync` — 非流式调用，评测场景节省约40%时间
- **新增** `AgentDialog.ExecuteEvalFastAsync` — 评测快速通道，跳过流水线/会话/记忆
- **修复** `IntentRouter` — 扩充合规关键词(危险/特性/GHS/分类)，剔除SimpleChat通用词(什么/为什么/怎么)
- **修复** RAG知识库延迟注入 — `LlmService.SetKnowledgeBaseService` 在DI解析后连接知识库
- **待完成**：50条评测全量通过，跑通工具触发率/参数准确率/结论准确率三维业务基准

### Phase 2d：Reflection 反思层代码级验证（2026-06-01）
- **新增** `Services/ReflectionVerifier.cs` — 代码级事实核查引擎（非 LLM，基于正则+知识库反向检索）
  - `VerifyBusinessFactsAsync` — 提取结论中的 GB 编号/国务院令号，反向检索知识库验证真伪
  - `VerifySystemHealth` — 检查工具链完整性、流式取消次数、空结果告警
  - `BuildCorrectedPrompt` — 将核查报告注入 LLM 修正 Prompt
- **数据模型**：`BusinessVerificationReport`（事实精度/HallucinatedClaims）+ `SystemHealthReport`（工具健康度）
- **修改** `AgentDialog.cs` — 暴露 `LastToolResults` / `LastToolPlan` 供验证层使用
- **重构** `RunReflectionStreamTools.cs` — Reflection 流程改为：代码核查 → 核查报告 → LLM 基于报告修正（旧 LLM 自我反思降级保留）
- **注入链**：`ReflectionModule` / `ModuleFactory` 传递 `IKnowledgeBaseService`
- **效果**：LLM 引用的法规编号可被代码验证真伪，终止「LLM 批改 LLM 自己作业」的幻觉循环

### Phase 2a 前期基础：工具调度统一（2026-05-30）
- **双模工具链**：`ChemicalComplianceTools` 支持 RAG 检索（主路径）+ 硬编码字典（降级兜底）
- **LLM 语义工具选择**：`ToolService.AnalyzeAndPlanToolsAsync` 改为 LLM 驱动，关键词匹配保留为兜底
- **统一调用入口**：消除 `AgentDialog` 与 `ToolService` 中重复的 `CallTool` 逻辑
- **待完成**：移除 27 条硬编码字典，启用 Semantic Kernel 原生 Function Calling，工具数据全量来自 RAG 知识库

### P4：多格式知识库管道（2026-05-23）
- **新增** `Services/PdfExtractor.cs` — 基于 PdfPig 的 PDF 文本提取
- **新增** `Services/DocExtractor.cs` — 基于 OpenXml 的 DOCX 全文提取
- **新增** `Services/TextCleaner.cs` — 国标 PDF 封面噪声过滤、目录删除
- **新增** `Services/SemanticChunker.cs` — 按条款/条目自适应语义分块
- **改造** `ChemicalRAG.cs` — 5 个新方法串联完整管道
- **扩展** `DatabaseService.cs` — 6 个元数据列
- **效果**：知识库覆盖从 7 个 TXT → 41 个 PDF + 1097 个 DOC

### P3：领域语义重命名（2026-05-22）
- 全量替换 Industrial→ChemicalCompliance，6 个文件修改

### P0：工业工具 → 化工合规工具替换（2026-05-19）
- **新增** `Agent1/ChemicalComplianceTools.cs` — 化工合规工具集，含 5 个工具：
  - `CheckHazardCategory` — 查询危化品危险类别及适用国标（GB 30000 系列）
  - `CheckStorageCompatibility` — 检查两种危化品是否可同库储存（GB15603）
  - `GetSafetyDistance` — 查询设施间安全间距（GB50160/GB50016）
  - `GetCurrentTime` / `Calculate` — 通用工具
- **修改** `AppConfig.cs` — 新增 `ChemicalToolConfig` + `ToolDefinition` 配置驱动工具调度
- **重写** `ToolService.cs` — 关键字触发改为配置驱动，移除硬编码工业工具
- **修改** `AgentDialog.cs` / `RunReflectionStreamTools.cs` / `Program.cs` — Prompt 和工具调用全量切换

### P1：RAG.cs & LlmService.cs 工业工具残留清理（2026-05-20）
- **修改** `LlmService.cs` — SK 插件注册从 `IndustrialTools` 切换为 `ChemicalComplianceTools`
- **重构** `RAG.cs` — 三阶段清理：
  - 删除废弃的 `LoadIndustrialKnowledgeBase()` 和 `RetrieveRelevantKnowledge()`（~100 行）
  - 工具引用 6 处全量替换 + 4 个 Prompt 切换为化工合规领域
  - 新增 `ParseToolCalls` 标记行限制匹配 + 智能参数提取（`ExtractSubstance`/`ExtractFacilityType` 等）
  - 强化 Step5 校验 Prompt 格式约束，防止 LLM 自我复制

### 编译状态
✅ `dotnet test`：133 通过，0 失败
✅ `dotnet build`：0 错误，19 警告（全部为既有的 nullable 引用类型警告）

