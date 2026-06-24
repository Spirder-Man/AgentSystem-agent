# Agent1 — 化工园区危化品合规审查 AI Agent

> **项目版本**：v3.3（文档深度扩展 — 2026-06-24）
> **核心修复文档**：[P0-P1修复详细技术文档](docs/troubleshooting/P0-P1修复详细技术文档.md) | [RAG工程Bug修复笔记](docs/troubleshooting/RAG工程Bug修复笔记_2026-05-26.md) | [故障排查文档](docs/troubleshooting/故障排查文档.md) | [代码自检清单](docs/工程skill/代码自检清单%20Skill.md)

基于 .NET 8 + Semantic Kernel + **llama.cpp 原生编译**构建的企业级化工园区危化品合规审查 AI Agent。

支持 20 个控制台菜单、12 个 ModuleType、REST API、JWT 认证、PostgreSQL+pgvector 混合检索、
OpenTelemetry 可观测性、等保三级审计（SHA256 哈希链），**针对 NVIDIA GPU (RTX 3090/3080 Ti) Linux 环境实现 RAG 全链路 GPU 加速**。

> 整体完成度：~95% | 编译：0 错误 | C# 文件：~105 个 / ~15,500 行 | 测试：148 通过 | 自动化测试：25 条 CLI 全功能

## 功能全景

```
┌─────────────────────────────────────────────────────────┐
│  AI 推理引擎        │  SK Auto FC + 断路器 + GPU嵌入    │
│  化工合规工具        │  8 个 [KernelFunction] + 三层降级  │
│  知识库              │  BM25+Vector+RRF+增量更新        │
│  化工业务模块        │  合规自查/工单/监管/应急/图谱     │
│  基础设施            │  Sha256审计链/安全双防线/健康检查  │
│  可观测性            │  PipelineMetrics/TraceId/事件溯源 │
│  API 服务            │  JWT认证/限流/OTel/优雅关闭       │
│  19 菜单 + 12 ModuleType — 全部实现 ✅                    │
└─────────────────────────────────────────────────────────┘
```

## 🏗️ 项目架构

```
├── Agent1/                       # 核心类库
│   ├── Models/                   # 数据模型
│   │   ├── CliExecutionResult.cs      # 统一输出契约 (Success/Warnings/ToolCalls/Events)
│   │   ├── PipelineMetrics.cs         # 6步流水线性能指标 (7步耗时+TraceId+业务指标)
│   │   ├── PipelineEvent.cs           # 事件溯源单元 (EventId/TraceId/EventType)
│   │   ├── ChemicalSubstanceModels.cs  # 化学品属性/法规版本/安全距离
│   │   ├── EvalModels.cs         # 评测数据模型
│   │   ├── DialogTypes.cs        # 对话类型
│   │   ├── LongTermMemoryModels.cs     # 长期记忆模型
│   │   └── ModuleType.cs         # 模块枚举 (12 个, 全部实现)
│   ├── Commands/                 # [P2] 命令模式
│   │   └── MenuCommands.cs       # IMenuCommand + 14 个命令类
│   ├── Modules/                  # 推理模块 (12 个, 全部继承 PipelineModuleBase)
│   │   ├── CoTSolidModule.cs / CoTStreamModule.cs  # CoT 思维链
│   │   ├── ReActSolidModule.cs / ReActStreamModule.cs  # ReAct 推理
│   │   ├── ReflectionModule.cs   # 自我反思纠错
│   │   ├── RAGModule.cs          # RAG 检索增强生成
│   │   ├── UnifiedDialogModule.cs # 智能对话系统
│   │   ├── ComplianceCheckModule.cs # 化工合规自查 ★核心
│   │   ├── TicketFollowupModule.cs  # [P1] 整改工单跟进
│   │   ├── RegulatoryAuditModule.cs # [P3] 监管核查辅助
│   │   ├── EmergencyResponseModule.cs # [P3] 应急响应方案
│   │   └── KnowledgeGraphModule.cs   # [P3] 知识图谱查询
│   ├── Services/
│   │   ├── AI/                   # LLM 服务
│   │   │   └── LlmService.cs     # llama.cpp 集成/Thinking控制/熔断器/重试
│   │   ├── Compliance/           # 化工合规
│   │   │   ├── ChemicalComplianceTools.cs  # 8 个 SK Plugin 工具
│   │   │   ├── EmergencyResponseService.cs # [P3] 应急响应引擎
│   │   │   ├── KnowledgeGraphService.cs    # [P3] 知识图谱 (BFS遍历)
│   │   │   ├── SafetyGuardService.cs       # [P3] Prompt注入+输出检测
│   │   │   ├── ChemicalSubstanceDatabase.cs # 58 种危化品结构化数据
│   │   │   └── ChemicalRAG.cs    # 化工 RAG 管道 (含增量更新)
│   │   ├── Knowledge/            # 知识库
│   │   │   ├── KnowledgeBaseService.cs          # BM25 检索
│   │   │   ├── HybridKnowledgeBaseService.cs    # BM25+向量混合检索(RRF融合)
│   │   │   ├── RerankerService.cs               # Cross-Encoder Reranker
│   │   │   ├── QueryCacheService.cs             # LRU查询缓存
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
│   │   │   ├── PipelineModuleBase.cs    # 模块抽象基类 (6步流水线)
│   │   │   ├── ModuleDispatcher.cs / ModuleFactory.cs
│   │   │   ├── IInferenceModule.cs      # 推理模块接口
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
├── Agent1.Tests/                 # xUnit 测试（148 tests, 含架构收敛+熔断器验证）
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
├── scripts/                      # 测试脚本
│   ├── auto_test.sh              # CLI 全功能自动化测试 (25条, bash)
│   └── *.py                      # Python 测试脚本 (历史)
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
- [P3] 监管核查辅助（逐条比对法规）
- [P3] 应急响应方案（ERG疏散/PPE/灭火/急救）
- [P3] 知识图谱（化学品-法规-事故关联网）
- [P2] 风险评估 3×3 矩阵
- [P2] 多模态 GHS 标签识别

### 5. 结构化化学品数据库（58 种危化品）
- 30+ 常见工业危化品结构化属性（CAS号/UN编号/分子式/闪点/沸点/爆炸极限）
- 危险类别与 GB 30000 标准号精确映射
- 20+ 精确化学品储存禁忌配对规则 + 类别级自动推断
- 20 对安全距离规则（GB 50160 / GB 50016）
- GB 18218 重大危险源临界量集成
- 8 项关键法规标准版本追踪（GB 15603/18218/30871 等）
- 40+ 化学品别名自动归一化

### 6. AI 工具集（8 个 KernelFunction）
- `CheckHazardCategory` — 危险类别查询
- `CheckStorageCompatibility` — 储存兼容性检查
- `GetSafetyDistance` — 安全距离查询
- `LookupChemicalProperties` — 化学品全属性查询
- `LookupRegulationReferences` — 法规引用查询
- `LookupHazardLabel` — [P2] GHS 标签识别 (多模态)
- `GetCurrentTime` / `Calculate` — 通用工具

### 7. RAG GPU 全链路加速（★ Sprint 1-5）
- **Sprint 1**: 批量 GPU 嵌入 (`GetEmbeddingsBatchAsync`)
- **Sprint 2**: GPU 向量检索 (`GpuVectorIndexService` 内存索引)
- **Sprint 3**: Cross-Encoder Reranker (`RerankerService`)
- **Sprint 4**: 智能分块 + 查询扩展 + RRF 融合
- **Sprint 5**: LRU 查询缓存 + GPU 监控 (nvidia-smi VRAM 采集)
- **性能预期**：嵌入延迟 -85% | 检索延迟 -84% | 总延迟 -75%

## 🚀 快速开始

### Linux 生产环境（RTX 3080 Ti / 3090 + llama.cpp 原生编译）⭐ 推荐

> 以下流程在 RTX 3080 Ti + CUDA 12.4 + Ubuntu 22.04 上亲测通过，0 残留问题。

#### 第 1 步：安装 .NET 8 SDK

```bash
# APT 安装（不要用 dotnet-install.sh，国内太慢）
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0
dotnet --version   # 验证: 应为 8.0.xxx
```

#### 第 2 步：安装 PostgreSQL 16 + pgvector

```bash
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /usr/share/keyrings/postgresql.gpg --yes
echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt jammy-pgdg main" | tee /etc/apt/sources.list.d/pgdg.list
apt update
apt install -y postgresql-16 postgresql-client-16 postgresql-16-pgvector

# 手动启动（容器环境禁止自动启动）
pg_ctlcluster 16 main start

# 设置密码 + 建库
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD '7758521';\""
su - postgres -c "psql -c \"CREATE DATABASE chemical_park_ai_agent;\""
su - postgres -c "psql -d chemical_park_ai_agent -c \"CREATE EXTENSION IF NOT EXISTS vector;\""
```

#### 第 3 步：编译 llama.cpp（CUDA GPU 版）

```bash
cd /root/autodl-tmp
rm -rf llama.cpp
git clone https://gitclone.com/github.com/ggerganov/llama.cpp.git
cd llama.cpp

# CUDA 编译 — 必须显式指定 nvcc 路径
cmake -B build \
  -DGGML_CUDA=ON \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="86"

cmake --build build --config Release -j$(nproc)

# 验证
ls -lh build/bin/llama-server
```

#### 第 4 步：下载 GGUF 模型文件

```bash
mkdir -p /root/autodl-tmp/models

# LLM 模型（~4.7GB）
wget -O /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf

# 嵌入模型（~274MB）
wget -O /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf
```

> 如容器网络不通，走 JupyterLab 上传 → `find / -name "*.gguf"` 定位 → `mv` 到 models/。

#### 第 5 步：克隆代码 + 初始化数据库

```bash
cd /root/autodl-tmp
git clone https://gitee.com/liuchao_yue/agent-system.git
cd agent-system
git checkout linux原生编译模型llama.cpp && git pull origin linux原生编译模型llama.cpp

# 初始化数据库
cp init_database.sql /tmp/
PGPASSWORD=7758521 psql -h localhost -U postgres -f /tmp/init_database.sql
```

#### 第 6 步：验证配置（默认已正确）

`Agent1/appsettings.json` 中 LLM 端点已默认为 llama-server（v4.3 已修复），无需手动改：

```
"Llm": {
    "Endpoint": "http://localhost:8080/v1"     ← 已默认正确
},
"VectorSearch": {
    "EmbeddingEndpoint": "http://localhost:8081/v1"  ← 已默认正确
}
```

> 环境变量 `LLM_ENDPOINT` 和 `KNOWLEDGE_BASE_PATH` 也可覆盖默认值。

#### 第 7 步：启动 AI 推理服务

```bash
mkdir -p /root/autodl-tmp/logs

# LLM 推理服务（端口 8080）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /root/autodl-tmp/logs/llama-server.log 2>&1 &

# Embedding 嵌入服务（端口 8081）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings \
  -ngl 99 -c 2048 --batch-size 512 \
  > /root/autodl-tmp/logs/llama-embed.log 2>&1 &

sleep 5

# 健康检查
curl -s http://localhost:8080/health  # → {"status":"ok"}
curl -s http://localhost:8081/health  # → {"status":"ok"}
```

#### 第 8 步：编译并启动

```bash
cd /root/autodl-tmp/agent-system
dotnet build Agent1/Agent1.csproj -c Release

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

#### 可选：启动 API 服务

```bash
nohup dotnet run --project Agent1.Api \
  --environment Production \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &

curl http://localhost:5000/health/live
```

### 🐳 Docker 容器化部署（Windows 亲测通过）

#### 准备工作

```powershell
# ① 进入项目目录
cd d:\桌面\agent\项目\Agent1

# ② 确认 Docker 在运行（如果没反应，手动打开 Docker Desktop 再执行）
docker version

# ③ 确认 .env 存在
ls .env

# ④ 如果不存在，创建一个
copy .env.example .env
```

#### 第一步：拉取基础镜像（不需要 GPU 编译的）

```powershell
docker compose pull postgres
docker compose pull prometheus
docker compose pull grafana
```

#### 第二步：构建 llama.cpp CUDA 编译镜像（首次约 10~30 分钟）

```powershell
docker compose build llama-server llama-embed
```

> 这一步会克隆 llama.cpp 源码 + CUDA 编译，完整日志可见。Windows 上 GPU 不可用但镜像兼容 Linux。

#### 第三步：构建 API 镜像

```powershell
docker compose build api
```

> 这一步会 `dotnet restore` → `dotnet publish`，约 2~3 分钟。

#### 第四步：启动全部服务

```powershell
docker compose up -d
```

#### 第五步：实时看日志

```powershell
docker compose logs -f
```

`Ctrl+C` 退出日志。

#### 第六步：验证

```powershell
# 服务状态
docker compose ps

# API 健康检查
curl http://localhost:8080/health

# Swagger 文档
start http://localhost:8080/swagger

# Prometheus
start http://localhost:9090

# Grafana（admin / agent1-admin）
start http://localhost:3000
```

#### 常用后续命令

```powershell
docker compose logs -f api            # 只看 API 日志
docker compose restart api            # 重启 API
docker compose down                   # 停止全部（保留数据卷）
docker compose down -v                # 停止 + 清除所有数据
```

#### Windows 注意事项

| 问题                | 说明                                                         |
| ------------------- | ------------------------------------------------------------ |
| llama-server 启动慢 | 首次需要编译 CUDA 镜像，CPU 推理模式（Windows 无 GPU 直通），启动后日志会显示 `llama_model_load: loaded meta data` |
| 模型需要手动放      | `models/` 目录下放 `qwen3-8b-q4_k_m.gguf` 和 `nomic-embed-text-v1.5.Q8_0.gguf`，否则 llama-server 启动失败 |
| 没有模型文件        | 可以先跳过 llama-server/llama-embed，只启动 `docker compose up -d postgres api prometheus grafana`，API Mock 测试仍可用 |

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

## 🔐 安全配置指南

所有敏感信息通过外部配置注入，**绝不**硬编码到源码或提交 Git。系统提供三层优先级机制，
确保从开发到生产的每个阶段都有合适的配置方式。

### 账号密码配置

系统按以下优先级（从高到低）加载账号：

```
环境变量 AUTH_ACCOUNTS_JSON  >  appsettings.json Auth.Accounts  >  开发随机密码（仅非生产环境）
```

#### 方式一：环境变量（Docker / 生产推荐）

```bash
# Linux / macOS
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"MySecureP@ss","Role":"admin"},{"Username":"auditor","Password":"AuditP@ss","Role":"auditor"}]'

# Windows PowerShell
$env:AUTH_ACCOUNTS_JSON = '[{"Username":"admin","Password":"MySecureP@ss","Role":"admin"}]'
```

**Docker Compose** 中注入（`docker-compose.yml`）：

```yaml
api:
  environment:
    - AUTH_ACCOUNTS_JSON=[{"Username":"admin","Password":"MySecureP@ss","Role":"admin"}]
```

#### 方式二：appsettings.json（本地开发推荐）

在 `Agent1/appsettings.json` 中添加：

```json
{
  "Auth": {
    "Accounts": [
      { "Username": "admin",   "Password": "MyPassword123",   "Role": "admin" },
      { "Username": "auditor", "Password": "AuditorPwd123",   "Role": "auditor" },
      { "Username": "viewer",  "Password": "ViewerPwd123",    "Role": "viewer" }
    ]
  }
}
```

> 💡 明文密码在首次加载时会被**自动升级为 BCrypt 哈希**（`workFactor=12`），
> 控制台会打印升级后的哈希值。你可以用哈希值替换明文，此后只做哈希比对，更加安全。

#### 方式三：开发随机密码（零配置启动）

如果未配置任何账号且非 `Production` 环境，系统会**自动生成密码学强度的随机密码**
并打印在控制台（带醒目分隔框）：

```
═══════════════════════════════════════════════════════════
  开发环境默认账号（随机生成，仅本次启动有效）：
  admin   → aB3xK9mW7pQ2
  auditor → dR8nL4vF6hJ1
  viewer  → tY5cG2sE8wM0
  请通过 AUTH_ACCOUNTS_JSON 环境变量或 appsettings.json 配置固定密码
═══════════════════════════════════════════════════════════
```

> ⚠️ 每次重启密码都会变。如需固定密码，请使用方式一或方式二。

#### 生产环境保护

若 `ASPNETCORE_ENVIRONMENT=Production` 且未配置任何账号，系统会**拒绝启动**并抛出异常：

```
System.InvalidOperationException: 生产环境必须通过 AUTH_ACCOUNTS_JSON 环境变量配置账号
```

### JWT 签名密钥

```bash
# .env 文件或环境变量（推荐）
JWT_KEY=your-key-at-least-32-characters-long-please-change-me
```

或在 `appsettings.json` 中：

```json
{
  "Jwt": {
    "Key": "your-key-at-least-32-characters-long-please-change-me",
    "Issuer": "Agent1",
    "Audience": "Agent1.Api",
    "AccessTokenExpireMinutes": "60",
    "RefreshTokenExpireDays": "7"
  }
}
```

| 变量 | 说明 | 生产要求 |
|------|------|----------|
| `JWT_KEY` | JWT 签名密钥（≥32 字符） | **强制设置** |
| `DB_PASSWORD` | PostgreSQL 密码 | **强制设置** |
| `AUTH_ACCOUNTS_JSON` | 账号列表 JSON | **强制设置**（至少 admin 角色） |
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | 设为 `Production` |

### 预置角色与权限

| 角色 | 权限 |
|------|------|
| `admin` | 全部功能：仪表盘 + 合规审核 + 巡检 + 工单 + 系统管理 + GPU 监控 |
| `auditor` | 核心业务：仪表盘 + 合规审核 + 巡检 + 工单 |
| `viewer` | 只读：仪表盘 + 查看巡检报告 |

## 🖥️ 前端项目 (React SPA)

前端基于 React 18 + TypeScript + Ant Design 5 + Vite 5，详见 [前端架构设计方案](docs/architecture/Agent1前端架构设计方案.md)。

### ⚡ 前后端并行开发（MSW Mock）

前端通过 **MSW (Mock Service Worker)** 在浏览器端模拟后端 API，实现前后端完全解耦并行开发：

```bash
cd agent1-web
npm install

# Mock 模式（后端不需要启动，纯前端开发）
npm run dev:mock

# 真实 API 模式（连接后端 Agent1.Api:5000）
npm run dev
```

**原理**：MSW 在浏览器的 Service Worker 线程中拦截 HTTP 请求，返回与后端 API 格式一致的假数据。React 组件完全不感知 Mock 层——关掉开关即切换真实 API，**零代码改动**。

```
Mock 模式:  React → Axios → MSW (浏览器拦截) → 假数据返回
真实模式:  React → Axios → 网线 → Agent1.Api:5000 → PostgreSQL
```

Mock 数据包含：
- **合规审核模拟** — 关键词匹配返回不同合规结论，2-5 秒模拟 LLM 推理延迟
- **工单状态流转引擎** — 完整实现 New→Confirmed→InProgress→Remediated→Verified 状态机
- **巡检/资产/报告假数据** — 覆盖全部 25 个 API 端点

> 详见 `agent1-web/src/mocks/README.md`

### 前端技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 框架 | React 18 + TypeScript 5 | 与后端 C# 强类型体系对齐 |
| 构建 | Vite 5 | 秒级 HMR |
| UI | Ant Design 5 + Tailwind CSS 3 | 企业级组件 + 原子化 CSS |
| 路由 | React Router v6 | 嵌套路由 + 懒加载 + 角色守卫 |
| 服务端状态 | TanStack Query 5 | LLM 长耗时请求的缓存/重试 |
| 客户端状态 | Zustand 4 | 轻量（<1KB），存 Auth/全局配置 |
| HTTP | Axios | JWT 拦截器 + Token 自动刷新 |
| Mock | MSW 2 | Service Worker 拦截，前后端并行 |
| 图表 | ECharts 5 | 合规态势仪表盘/风险分布 |
| 测试 | Vitest + Playwright | 单元 + E2E + 视觉回归 |

### 页面地图

```
/login                          # 登录页
/dashboard                      # 合规态势总览仪表盘
/compliance/check               # 合规审核（核心）
/compliance/hazard              # 危化品类别查询
/compliance/storage             # 储存兼容性检查
/inspection/plans               # 巡检计划管理
/inspection/assets              # 资产台账
/knowledge-graph                # 知识图谱大屏
/emergency                      # 应急响应
/gpu-monitor                    # GPU 推理监控（admin）
/tickets                        # 整改工单
/admin                          # 系统管理（admin）
```

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
Push → Restore → Build (.NET 8) → Test (148 tests) → Docker (仅main分支)
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
├── testing/                  # 测试文档 (7个文件)
│   ├── Agent1-Linux完整测试方案.html    # 变量级测试方案 (120+ 条)
│   ├── Agent1-手动测试执行手册.html     # 逐菜单操作指南
│   └── Agent1无GPU全链路测试方案.html   # 无GPU 测试方案
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

**文档版本**：v4.5  
**最后更新**：2026年6月24日  
**分支**：`linux原生编译模型llama.cpp`  
**状态**：P0-P2 清完 | P1 死代码清理完成 | 148测试全通过 | 双仓库同步 | 容器化 (llama.cpp)

## 📋 近期更新

### Linux 3080 Ti 全功能自动化测试通过（2026-06-24）
- **自动化测试脚本**: `scripts/auto_test.sh` — 25 条 CLI 全功能一键测试, 独立日志 + 汇总报告
- **测试结果**: 22/25 Pass (88%), 0 Fail, 3 Timeout — RTX 3080 Ti 12GB
- **LLM 端点修复**: appsettings.json Endpoint 从 Ollama 11434 改为 llama-server 8080/v1
- **新增测试文档**: 3 份 HTML 测试方案 (变量级方案 + 执行手册 + 无GPU方案)
- **双仓库同步**: Gitee + GitHub 全部 4 分支实时同步

### CLI 安全管道统一整改 + 结构化可观测性升级（2026-06-23）
- **P0 安全加固**: AgentDialog 合规路径注入 SafetyGuardService 输入/输出双防线
- **P0 审计追踪**: IntentRouter 添加 LastMatchedKeyword + Serilog 审计日志
- **P1 架构收敛**: CliExecutionResult 统一输出契约 + PipelineModuleBase 抽象基类
- **P1 枚举补全**: ModuleType 新增 EmergencyResponse(11) + KnowledgeGraph(12)
- **P2 可观测性**: PipelineMetrics (7步耗时+TraceId+业务指标) + Serilog 结构化日志
- **P2 事件溯源**: PipelineEvent record + IEventStore + ExecuteAsync 中 9 类事件记录
- **测试**: 架构收敛测试(8项) + 熔断器验证测试(6项) — 新增 15 个测试
- **改动**: 27 files, +1079/-152 lines | 编译 0 errors | 测试 148/148 通过

### P3 大工程 + 架构审查 + 生产加固（2026-06-16）
- **应急响应模块**: `EmergencyResponseService` (302行) + `EmergencyResponseModule` (134行) — 对标 ERG 指南
- **知识图谱模块**: `KnowledgeGraphService` (370行) + `KnowledgeGraphModule` (53行) — BFS 多跳遍历
- **监管核查模块**: `RegulatoryAuditModule` (187行) — 逐条比对法规
- **多模态识别**: `MultimodalService` (139行) — HttpClient 直调 Ollama /api/chat
- **安全加固**: `SafetyGuardService` (150行) — Prompt 注入+输出高危断言双防线
- **风险评估**: `RiskAssessmentService` (159行) — 3×3 矩阵 MVP
- **整改工单**: `TicketFollowupModule` (173行) — LLM 提取整改项
- **架构审查**: 硬编码治理 (12 个常量→配置)、空 catch 修复 (4 处)、优雅关闭 (已存在)
- **配置外部化**: MemoryConfig + SafetyConfig + CircuitBreakerThreshold
- **架构优化**: Lazy\<T\> 替代 null! 循环依赖、命令模式重构 Program.cs、SearchModeType enum
- **审核完整性**: SHA256 哈希链 DB 持久化 + VerifyIntegrityAsync()
- **增量更新**: 知识库文件追踪 + 新增/修改/删除全周期清理

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
✅ `dotnet build`：0 错误，30 警告（全部为既有的 nullable 引用类型警告）
✅ `dotnet test`：148 通过，0 失败
✅ `dotnet run`：本地启动正常，安全拦截生效，PipelineMetrics 完整采集
