# AgentSystem - 化工园区危化品合规审查 AI Agent

基于 .NET 8 + Semantic Kernel 构建的企业级化工园区危化品合规审查智能助手系统。

## 🏗️ 项目架构

```
├── Agent1/                    # 主应用模块
│   ├── Modules/              # 推理模块
│   │   ├── RAGModule.cs      # RAG检索增强生成
│   │   ├── CoTSolidModule.cs # CoT思维链推理
│   │   ├── ReActStreamModule.cs # ReAct交互式推理
│   │   └── ComplianceCheckModule.cs # 合规审查模块
│   ├── Services/             # 核心服务
│   │   ├── LlmService.cs     # LLM服务接口
│   │   ├── SessionService.cs # 会话管理
│   │   ├── KnowledgeBaseService.cs # 知识库服务（BM25检索）
│   │   ├── HybridKnowledgeBaseService.cs # 混合检索服务（BM25+向量）
│   │   ├── DatabaseService.cs # 数据库服务（PostgreSQL + pgvector）
│   │   ├── PdfExtractor.cs   # PDF 文档提取器（PdfPig）
│   │   ├── DocExtractor.cs   # DOC/DOCX 文档提取器（OpenXml）
│   │   ├── TextCleaner.cs    # 文本清洗服务
│   │   ├── SemanticChunker.cs # 语义分块服务
│   │   └── AuditService.cs   # 审计日志服务
│   ├── Config/               # 配置文件
│   └── Program.cs            # 应用入口
├── ArchitectureTest/         # 架构测试模块
├── docs/                     # 文档库（已整理）
│   ├── architecture/         # 架构设计文档
│   ├── technical-principles/ # 技术原理解析
│   ├── testing/              # 测试文档
│   ├── troubleshooting/      # 故障排查文档
│   ├── learning-notes/       # 学习笔记
│   └── project/              # 项目基本文档
├── knowledgebase/            # 化工合规知识库
│   ├── 国标/                 # 国家标准文档
│   ├── 园区规则/             # 园区规章制度
│   └── 历史案例/             # 事故案例与整改经验
└── Agent1.sln                # 解决方案文件
```

## 🛠️ 技术栈

| 层级 | 技术 | 版本 | 状态 |
|------|------|------|------|
| 语言 | C# | 12.0 | ✅ 已实现 |
| 框架 | .NET | 8.0 | ✅ 已实现 |
| AI框架 | Semantic Kernel | 1.74.0 | ✅ 已实现 |
| PDF 解析 | PdfPig | 0.1.9 | ✅ 已实现 |
| DOCX 解析 | DocumentFormat.OpenXml | 3.2.0 | ✅ 已实现 |
| 检索算法 | BM25 + pgvector 混合 | - | ✅ 已实现 |
| 数据库 | PostgreSQL | 16.x | ✅ 已实现 |
| 向量扩展 | pgvector | 0.7.x | ✅ 已实现 |
| 本地 LLM | Ollama | latest | ✅ 已实现 |
| DI 容器 | Microsoft.Extensions.DI | 8.0+ | ✅ 已实现 |
| 结构化日志 | Serilog | 4.0+ | ✅ 已实现 |

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

## 📁 文档结构

```
docs/
├── architecture/              # 架构设计文档 (12个文件)
│   ├── 架构设计文档.md
│   ├── 化工园区危化品合规审核AI Agent架构整改方案.md
│   ├── 化工园区危化品合规审核AI Agent架构适配方案.md
│   └── ...
├── technical-principles/      # 技术原理文档 (9个文件)
│   ├── BM25 参数：权重平衡的关键探索.md
│   ├── C# 内存模型、LINQ、BM25 和 NGram 详解.md
│   ├── 化工园区危化品合规审核RAG系统技术原理深度解析.md
│   └── ...
├── testing/                   # 测试文档 (4个文件)
├── troubleshooting/           # 故障排查文档 (2个文件)
├── learning-notes/            # 学习笔记 (3个文件)
├── project/                   # 项目文档 (2个文件)
└── README.md                  # 文档库索引
```

## 🚀 快速开始

### 环境要求
- .NET 8 SDK
- Docker Desktop（⭐ 推荐，数据库免安装）**或** PostgreSQL 16 + pgvector 手动安装
- Ollama（本地 LLM 推理，需拉取 `deepseek-r1:local7b` 和 `nomic-embed-text:latest`）

### 方式一：Docker 一键启动（需先安装 Docker Desktop）

如果你已安装 Docker Desktop，30 秒内完成：

```bash
# 1. 克隆项目
git clone https://gitee.com/liuchao_yue/agent-system.git
cd agent-system

# 2. 启动数据库（首次自动建库 + 执行建表脚本 + 启用 pgvector）
docker-compose up -d

# 3. 启动应用
dotnet run --project Agent1
```

配置文件 `docker-compose.yml` 已内置默认密码，开箱即用。如需自定义：
```bash
cp .env.example .env    # 编辑密码后 docker-compose 会自动读取
```

### 方式二：手动安装数据库（不需要 Docker）

如果你没有 Docker 或想手动管理 PostgreSQL：

### 1. 初始化数据库

先创建数据库并导入建表脚本：

```sql
-- PostgreSQL 中执行
CREATE DATABASE chemical_park_ai_agent;
```

```bash
psql -U postgres -d chemical_park_ai_agent -f init_database.sql
```

### 2. 配置

所有配置集中在 `Agent1/appsettings.json`，无需修改代码。

**数据库连接**（敏感信息通过环境变量注入，禁止硬编码）：

```bash
# 1. 复制环境变量模板
cp .env.example .env

# 2. 编辑 .env 填入你的数据库密码
#    Windows 记事本: notepad .env
#    VSCode: code .env
```

或直接通过 PowerShell 设置（永久生效，需重启终端）：

```powershell
[System.Environment]::SetEnvironmentVariable("DB_PASSWORD", "你的数据库密码", "User")
[System.Environment]::SetEnvironmentVariable("DB_USERNAME", "postgres", "User")
```

> ⚠️ **安全提醒**：
> - 密码通过 `DB_PASSWORD` 环境变量注入，**绝不**写入 `appsettings.json` 或提交到 Git
> - 生产环境建议创建专用数据库账号并授予最小权限，而非使用 `postgres` 超级用户
> - 本地开发使用 `postgres` + 环境变量即可，这是个人项目的合理折中

**LLM 模型**（默认值可直接使用，也可按需调整）：

```json
// appsettings.json 关键配置项
"Llm": { "ModelId": "deepseek-r1:local7b", "Endpoint": "http://localhost:11434" },
"KnowledgeBase": { "BasePath": "d:\\桌面\\agent\\项目\\Agent1\\knowledgebase" },
"VectorSearch": { "EmbeddingModelId": "nomic-embed-text:latest" },
"Database": { "Host": "localhost", "Port": 5432, "DatabaseName": "chemical_park_ai_agent" }
```

### 2. 运行

```bash
cd Agent1
dotnet restore
dotnet run --project Agent1
```

启动后按菜单选择功能（推荐首选「8. 化工合规自查」或「9. 化工合规RAG测试」验证环境）。

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

**文档版本**：v2.0  
**最后更新**：2026年5月30日  
**状态**：Phase 2a 工具调用架构重构完成，工具已对接 RAG 知识库

## 📋 近期更新 (P3 + P4)

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

### Phase 2a：工具调用架构重构 — LLM 统一调度（2026-05-30）
- **双模工具链**：`ChemicalComplianceTools` 支持 RAG 检索（主路径）+ 硬编码字典（降级兜底）
- **LLM 语义工具选择**：`ToolService.AnalyzeAndPlanToolsAsync` 改为 LLM 驱动，关键词匹配保留为兜底
- **统一调用入口**：消除 `AgentDialog` 与 `ToolService` 中重复的 `CallTool` 逻辑（删除 ~90 行硬编码代码）
- **工具真正接入 RAG**：`CheckHazardCategory` / `CheckStorageCompatibility` / `GetSafetyDistance` 改为从知识库检索 GB30000/GB15603/GB50160 全文，不再依赖硬编码字典

### 编译状态
✅ `dotnet build`：0 错误，20 警告（全部为既有的 nullable 引用类型警告）
✅ `dotnet test`：2/2 通过

