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
│   │   ├── KnowledgeBaseService.cs # 知识库服务
│   │   └── AuditService.cs   # 审计日志服务
│   ├── Config/               # 配置文件
│   └── Program.cs            # 应用入口
├── ArchitectureTest/         # 架构测试模块
└── Agent1.sln                # 解决方案文件
```

## 🛠️ 技术栈

| 层级 | 技术 | 版本 |
|------|------|------|
| 语言 | C# | 12.0 |
| 框架 | .NET | 8.0 |
| AI框架 | Semantic Kernel | 1.x |
| 向量数据库 | Milvus | 2.x |
| 关系数据库 | PostgreSQL | 16 |
| 数据库驱动 | Npgsql | 8.x |

## ✨ 核心功能

### 1. 推理引擎模块
- **RAG检索增强生成**：支持 BM25 + 向量混合检索
- **CoT思维链推理**：支持同步/流式输出
- **ReAct交互式推理**：支持工具调用与反馈循环
- **合规规则验证**：集成化工行业合规知识库

### 2. 会话管理
- 基于内存的对话历史管理
- 支持多轮对话上下文保持
- 会话生命周期管理

### 3. 知识库服务
- 支持文档解析与向量化
- 支持 Milvus 向量检索
- 支持 PostgreSQL 全文检索

### 4. 审计日志
- 完整的操作日志记录
- 符合等保三级要求

## 🚀 快速开始

### 环境要求
- .NET 8 SDK
- PostgreSQL 16+
- Milvus 2.x

### 安装步骤

1. **克隆项目**
```bash
git clone https://gitee.com/liuchao_yu/agent-system.git
cd agent-system
```

2. **配置数据库连接**

编辑 `Agent1/Config/AppConfig.cs`，配置 PostgreSQL 和 Milvus 连接信息。

3. **构建项目**
```bash
dotnet build Agent1.sln
```

4. **运行项目**
```bash
cd Agent1
dotnet run
```

## 📖 使用说明

### 模块调用示例

```csharp
// 创建模块工厂
var moduleFactory = serviceProvider.GetRequiredService<IModuleFactory>();

// 获取 RAG 模块
var ragModule = moduleFactory.CreateModule<IRAGModule>();

// 执行检索增强生成
var result = await ragModule.RetrieveAndGenerateAsync(userQuery);
```

### 支持的推理模式

| 模式 | 说明 | 使用场景 |
|------|------|----------|
| RAG | 检索增强生成 | 需要外部知识库支撑的问答 |
| CoT | 思维链推理 | 需要复杂逻辑推理的问题 |
| ReAct | 交互式推理 | 需要调用工具的场景 |
| UnifiedDialog | 智能路由 | 自动选择最优推理策略 |

## 🔧 配置说明

### 数据库配置
```json
{
  "PostgreSql": {
    "ConnectionString": "Host=localhost;Port=5432;Database=agentdb;Username=postgres;Password=password"
  },
  "Milvus": {
    "Endpoint": "localhost:19530",
    "CollectionName": "compliance_knowledge"
  }
}
```

## 📊 项目状态

- **当前阶段**: P0/P1 功能开发
- **状态**: 开发中
- **目标**: 等保三级合规要求

## 📝 贡献指南

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

---

**化工园区危化品合规审查 AI Agent** - 助力化工企业实现智能化合规管理
