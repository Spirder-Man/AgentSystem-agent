# Agent1 — 化工园区危化品合规审查 AI Agent

基于 .NET 8 + Semantic Kernel + **llama.cpp 原生编译**构建的企业级化工园区危化品合规审查 AI Agent。支持 PostgreSQL+pgvector 混合检索、JWT 认证、速率限制、OpenTelemetry 可观测性，**针对 RTX 3090 24GB Linux 环境实现 RAG 全链路 GPU 加速**。

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
│   │   │   └── LlmService.cs     # llama.cpp 集成/Thinking控制/熔断器/重试
│   │   ├── Compliance/           # 化工合规
│   │   │   ├── ChemicalComplianceTools.cs  # 7 个 SK Plugin 工具
│   │   │   ├── ChemicalSubstanceDatabase.cs # 30+ 危化品结构化数据库
│   │   │   └── ChemicalRAG.cs    # 化工 RAG 管道
│   │   ├── Knowledge/            # 知识库
│   │   │   ├── KnowledgeBaseService.cs          # BM25 检索(SplitTextIntoChunks智能分块)
│   │   │   ├── HybridKnowledgeBaseService.cs    # BM25+向量混合检索(RRF融合)
│   │   │   ├── GpuVectorIndexService.cs         # GPU向量索引管理器(Sprint2)
│   │   │   ├── RerankerService.cs               # Cross-Encoder Reranker(Sprint3)
│   │   │   ├── QueryCacheService.cs             # LRU查询缓存(Sprint5)
│   │   │   ├── PdfExtractor.cs / DocExtractor.cs # 文档解析
│   │   │   ├── TextCleaner.cs / SemanticChunker.cs  # 清洗分块
│   │   │   └── RetrievedChunk.cs / ChemicalDocumentRecord.cs
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
├── .github/workflows/            # CI/CD（构建→测试）
├── Data/ComplianceEvalSet.json   # 64 条化工合规评测集
├── knowledgebase/                # 化工合规知识库
│   ├── 国标/ 园区规则/ 历史案例/ 化工专业条例/
│   └── H166-危险化学品化工企业安全生产三级标准化/
├── docs/                         # 项目文档
│   ├── architecture/             # 架构设计文档 (13个文件)
│   ├── deploy/                   # 部署运维文档
│   ├── technical-principles/     # 技术原理文档 (9个文件)
│   ├── testing/                  # 测试文档
│   ├── troubleshooting/          # 故障排查文档
│   └── project/                  # 项目文档
├── scripts/                      # Python 测试脚本
├── docker-compose.yml            # Docker 部署（Windows 开发环境）
├── Dockerfile                    # 多阶段构建
└── Agent1.sln                    # 解决方案文件
```

## 🛠️ 技术栈

| 层级 | 技术 | 版本 | 状态 |
|------|------|------|------|
| 语言 | C# | 12.0 | ✅ |
| 框架 | .NET | 8.0 | ✅ |
| AI 框架 | Semantic Kernel | 1.74.0 | ✅ |
| **推理引擎** | **llama.cpp (llama-server)** | **b4857** | ✅ |
| **推理模型** | **Qwen3-8B (Q4_K_M GGUF)** | **8B** | ✅ |
| **嵌入模型** | **nomic-embed-text-v1.5 (F16 GGUF)** | **latest** | ✅ |
| **精排模型** | **bge-reranker-v2-m3** | **Python sidecar** | ✅ |
| 数据库 | PostgreSQL + pgvector | 16.x | ✅ |
| PDF 解析 | PdfPig | 0.1.9 | ✅ |
| DOCX 解析 | DocumentFormat.OpenXml | 3.2.0 | ✅ |
| 认证 | JWT Bearer + BCrypt | 8.0+ | ✅ |
| 速率限制 | 自定义 Middleware | - | ✅ |
| 结构化日志 | Serilog + Seq | 4.0+ | ✅ |
| 指标监控 | Prometheus Text Format | - | ✅ |
| 可视化 | Grafana 仪表盘 | latest | ✅ |
| 分布式追踪 | OpenTelemetry | 1.9+ | ✅ |
| CI/CD | GitHub Actions | - | ✅ |
| 负载测试 | Benchmark 模块 | 内置 | ✅ |
| **GPU** | **RTX 3090 24GB (sm_86)** | — | ✅ |

### 🖥️ Linux 生产环境服务架构

```
┌─────────────────────────────────────────────────────────────┐
│                    RTX 3090 24GB Linux                       │
├───────────────┬───────────────┬───────────────┬─────────────┤
│ llama-server  │ llama-server  │ bge-reranker  │ PostgreSQL  │
│ :8080         │ :8081         │ :8082         │ :5432       │
│               │               │               │ + pgvector  │
│ Qwen3-8B      │ nomic-embed   │ Python        │             │
│ Q4_K_M        │ F16 GGUF      │ sidecar       │             │
│ -ngl 99       │ --embeddings  │ (可选)        │             │
│ -c 8192       │ --batch 512   │               │             │
│ ~5.0 GB VRAM  │ ~1.0 GB VRAM  │ ~1.5 GB VRAM  │             │
├───────────────┴───────────────┴───────────────┴─────────────┤
│                   Agent1.Api :5000                           │
│              .NET 8 + Semantic Kernel + JWT                  │
└─────────────────────────────────────────────────────────────┘
```

## ✨ 核心功能

### 1. 推理引擎模块
- **RAG检索增强生成**：BM25 + 向量混合检索 (RRF 融合)，支持查询缓存
- **CoT思维链推理**：支持同步/流式输出
- **ReAct交互式推理**：支持工具调用与反馈循环
- **Reflection反思纠错**：代码级事实核查（非 LLM 自我评价）
- **合规规则验证**：集成化工行业合规知识库

### 2. 会话管理
- 基于内存的对话历史管理
- 支持多轮对话上下文保持

### 3. 知识库管理
- 支持国标、园区规则、历史案例、化工专业条例四级知识体系
- BM25 + 向量混合检索策略 (RRF 融合)
- GPU 向量索引（内存余弦相似度搜索）
- Cross-Encoder Reranker 精排（远程 Python sidecar + 本地启发式降级）
- QueryCache 查询缓存（LRU + TTL）
- 业务优先级重排序

### 4. 合规审查能力
- 危化品存储合规检查
- 安全距离合规验证
- 危险类别精准匹配
- 化学品属性结构化查询
- 法规版本状态追踪
- 重大危险源临界量查询

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

### 7. RAG GPU 全链路加速（★ Sprint 1-5）
- **Sprint 1**: 批量 GPU 嵌入 (`GetEmbeddingsBatchAsync`)
- **Sprint 2**: GPU 向量检索 (`GpuVectorIndexService` 内存索引)
- **Sprint 3**: Cross-Encoder Reranker (`RerankerService`)
- **Sprint 4**: 智能分块 + 查询扩展 + RRF 融合
- **Sprint 5**: LRU 查询缓存 + GPU 监控 (nvidia-smi VRAM 采集)
- **性能预期**：嵌入延迟 -85% | 检索延迟 -84% | 总延迟 -75%

## 🚀 快速开始

### Linux 生产环境（RTX 3090 + llama.cpp 原生编译）⭐ 推荐

> 📖 完整部署指南：[docs/deploy/Linux服务器一键启动与测试命令.md](docs/deploy/Linux服务器一键启动与测试命令.md)

#### 步骤 1：启动 PostgreSQL

```bash
service postgresql start
pg_isready  # 验证
```

#### 步骤 2：启动 AI 推理服务（llama.cpp 原生）

```bash
mkdir -p /root/autodl-tmp/logs

# LLM 推理服务（端口 8080）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /root/autodl-tmp/logs/llama-server.log 2>&1 &

# Embedding 嵌入服务（端口 8081，GPU 加速）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings \
  -ngl 99 -c 2048 --batch-size 512 \
  > /root/autodl-tmp/logs/llama-embed.log 2>&1 &

sleep 5

# 健康检查
curl -s http://localhost:8080/health  # LLM → "ok"
curl -s http://localhost:8081/health  # Embedding → "ok"
```

#### 步骤 3：拉取代码并编译

```bash
cd /root/autodl-tmp/agent-system
git checkout linux原生编译模型llama.cpp && git pull origin linux原生编译模型llama.cpp
dotnet build Agent1/Agent1.csproj -c Release
```

#### 步骤 4：启动控制台程序

```bash
DOTNET_ENVIRONMENT=Production \
JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx \
DB_PASSWORD=7758521 \
dotnet run --project Agent1
```

启动后选择菜单项：
- **10** — 数据库连接验证
- **12** — 工具调用诊断验证
- **8** — 化工合规自查（核心功能）
- **13** — 合规评测集（GPU 加速核心验证）⭐

#### 步骤 5（可选）：启动 API 服务

```bash
nohup dotnet run --project Agent1.Api \
  --environment Production \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &

curl http://localhost:5000/health/live
```

### Windows 开发环境

```bash
# 1. 安装 PostgreSQL 16 + pgvector，创建数据库
# 2. 安装 Ollama 并拉取模型
ollama pull qwen3:8b
ollama pull nomic-embed-text

# 3. 配置 .env
cp .env.example .env
# 编辑 .env 填入数据库密码和 JWT 密钥

# 4. 启动 API
dotnet run --project Agent1.Api
```

### API 端点

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| `POST` | `/api/auth/login` | 登录获取 Token | 否 |
| `POST` | `/api/auth/refresh` | 刷新 Token | Bearer |
| `POST` | `/api/compliance/hazard/query` | 危化品危险类别查询 | Bearer |
| `POST` | `/api/compliance/storage/check` | 储存兼容性检查 | Bearer |
| `POST` | `/api/compliance/check` | 合规综合检查 | Bearer |
| `GET` | `/health` | 全量健康检查（DB + Ollama + KB） | 否 |
| `GET` | `/health/live` | 存活检查 | 否 |
| `GET` | `/health/ready` | 就绪检查 | 否 |
| `GET` | `/metrics` | Prometheus 指标 | 否 |
| `GET` | `/swagger` | Swagger 文档 | 否 |

调用示例：
```bash
# 1. 登录
curl -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"your_password"}'

# 2. 查询危化品（用返回的 token）
curl -X POST http://localhost:5000/api/compliance/hazard/query \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯"}'
```

## 🔐 生产环境安全配置

所有敏感信息通过环境变量注入，**绝不**硬编码或提交 Git：

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
http://localhost:5000/metrics                # Prometheus 指标
http://localhost:5000/health                 # 全量健康检查（DB + LLM + KB）
http://localhost:9090                        # Prometheus UI（需单独启动）
http://localhost:3000                        # Grafana（需单独启动）
```

Prometheus 录制规则位于 `prometheus/` 目录，Grafana 仪表盘 JSON 位于 `grafana/` 目录。

## 🔄 CI/CD

GitHub Actions 工作流（`.github/workflows/ci.yml`）：
```
Push → Build (.NET 8) → Test (133 tests)
```

## 📁 文档结构

```
docs/
├── architecture/              # 架构设计文档 (13个文件)
│   ├── Agent1宏观架构导向图.md
│   ├── 架构设计文档.md
│   ├── ModelScope模型选型决策框架.md
│   └── ...
├── deploy/                    # 部署运维文档
│   └── Linux服务器一键启动与测试命令.md
├── articles/                  # 技术文章与参数注入方案 (4个文件)
├── technical-principles/      # 技术原理文档 (9个文件)
├── testing/                   # 测试文档 (4个文件)
├── troubleshooting/           # 故障排查文档 (5个文件)
├── learning-notes/            # 学习笔记 (4个文件)
├── project/                   # 项目文档 (5个文件)
└── README.md                  # 文档库索引
```

## 📚 学习路径

**初学者路径**：
1. 先看 learning-notes/ 了解学习过程
2. 再看 architecture/ 理解整体架构
3. 然后看 technical-principles/ 深入技术原理

**架构师路径**：
1. 先看 architecture/ 掌握架构设计
2. 再看 technical-principles/ 深入技术细节
3. 最后看 testing/ 和 troubleshooting/ 了解验证与改进

**运维部署路径**：
1. 先看 deploy/ 了解标准部署流程
2. 再看 troubleshooting/ 掌握常见故障处理
3. 结合 prometheus/ + grafana/ 搭建监控体系

## 📋 软考知识点映射

本项目覆盖软考「系统架构设计师」核心考点：
- 软件架构设计（分层架构、策略模式、依赖注入）
- 信息检索系统（BM25 算法、倒排索引、向量检索、RRF 融合）
- 知识管理与知识图谱
- 系统安全与等保三级

## 📝 许可证

MIT License

---

**文档版本**：v3.5  
**最后更新**：2026年6月16日  
**分支**：`linux原生编译模型llama.cpp`  
**状态**：RAG GPU 全链路加速 | P0 级 Bug 修复 | 133 tests 全通过

## 📋 近期更新

### P0 级 Bug 系统性修复（2026-06-16）
针对全项目代码扫描发现的 16 个 Bug 进行系统性修复（10 项已完成）：
- **P0-1**: `ChemicalComplianceTools.cs` L334 — 正则表达式 `>=s*` → `>=\s*`，修复安全距离提取失效
- **P0-2**: `ChemicalComplianceTools.cs` L49 — `RagCache` 从 `Dictionary` 升级为 `ConcurrentDictionary` + TTL + LRU 淘汰
- **P0-3**: `GpuVectorIndexService.cs` L242 — `Timer(async void)` 替换为 `PeriodicTimer` + `Task.Run`，避免并发同步冲突
- **P0-4**: `HybridKnowledgeBaseService.cs` L527 — RRF 去重键从 `Guid.NewGuid()` 改为确定性 `GetDedupKey()`
- **P1-5**: `KnowledgeBaseService.cs` L190 — BM25 `_avgDocLength` 除零保护 (`Math.Max(1.0, ...)`)
- **P1-9**: `ReflectionVerifier.cs` L165 — 检索异常时标记 `[KB检索异常]` 而非错误标记 `FoundInSource=true`
- **P1-10**: `KnowledgeBaseService.cs` L154 — `AddDocumentsAsync` 从 `.Wait()` 改为 `async/await`
- **P2-12**: `ConclusionVerifier.cs` L79 — 同时匹配 `【合规判断】` 和 `[判定:is_compliant=...]` 两种输出格式
- **P2-14**: `MetricsCollector.cs` L70 — 快照从多次 `Interlocked.Read` 改为单次原子读取

### Sprint 1-5: RAG GPU 全链路加速优化（2026-06-12）
针对 RTX 3090 24GB Linux 环境实施 RAG 检索全链路 GPU 加速，五个 Sprint 全部完成：
- **Sprint 1**: 嵌入 GPU 加速 + 批量处理 — `GetEmbeddingsBatchAsync()` 单次 API 调用处理多条文本
- **Sprint 2**: GPU 向量检索 + FAISS 内存索引 — `GpuVectorIndexService` 全量加载 + 余弦相似度搜索
- **Sprint 3**: Cross-Encoder Reranker — Python sidecar 远程调用 + 本地启发式降级
- **Sprint 4**: 智能分块 + 查询扩展 + RRF 融合 — 语义边界识别 + 同义词扩展 + k=60 融合
- **Sprint 5**: LRU 查询缓存 + GPU 监控 — TTL 5min + nvidia-smi VRAM 采集

### RAG 评估体系重设计 + 紧急修复（2026-06-11）
- **fix1b**: Prompt 格式约束 — 禁止输出法规全文
- **fix1c**: Faithfulness 评估改进 — 过滤 RAG 原文引用块
- **fix2**: CheckStorageCompatibility 描述强化
- **fix3**: 危险类别空数据检测
- **eval1-3**: Answer Relevance + Citation Accuracy + 工程指标

### Task 10: 化工知识库专业覆盖增强（2026-06-06）
30+ 危化品结构化属性数据库、40+ 别名归一化、3 个新 KernelFunction 工具、56 个单元测试

### Phase 2a: 评测体系生产级修复（2026-06-06 → 2026-06-04 → 2026-06-03）
- FC 就绪性检查 + 意图路由分离 + 结构化判定标签
- Qwen3 Thinking 控制（OllamaThinkingHandler 反射注入）
- 50 条业务评测 + 三维度量化

### 编译状态
✅ `dotnet build`：0 错误，19 警告（全部为既有的 nullable 引用类型警告）
✅ `dotnet test`：133 通过，0 失败
