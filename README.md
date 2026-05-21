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
| AI框架 | Semantic Kernel | 1.x | ✅ 已实现 |
| 检索算法 | BM25 | - | ✅ 已实现 |
| 数据库 | PostgreSQL | 16.x | ✅ 已实现 |
| 向量扩展 | pgvector | 0.7.x | ✅ 已实现 |
| 向量数据库 | Milvus | 2.x | ⏳ 规划中 |

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
- PostgreSQL 16 + pgvector 扩展
- Ollama（本地LLM推理）

### 运行方式

```bash
# 克隆项目
git clone <repository-url>
cd Agent1

# 安装依赖
dotnet restore

# 配置数据库连接
# 编辑 Config/AppConfig.cs

# 运行项目
dotnet run --project Agent1
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

## 📋 软考知识点映射

本项目覆盖软考「系统架构设计师」核心考点：
- 软件架构设计（分层架构、策略模式、依赖注入）
- 信息检索系统（BM25算法、倒排索引、向量检索）
- 知识管理与知识图谱
- 系统安全与等保三级

## 📝 许可证

MIT License

---

**文档版本**：v1.1  
**最后更新**：2026年5月20日  
**状态**：P1 阶段完成，P2 待处理

## 📋 近期更新 (P0 + P1)

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
✅ `dotnet build`：0 错误，21 警告（全部为既有的 nullable 引用类型警告）

