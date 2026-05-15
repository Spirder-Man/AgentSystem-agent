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
│   │   └── AuditService.cs   # 审计日志服务
│   ├── Config/               # 配置文件
│   └── Program.cs            # 应用入口
├── ArchitectureTest/         # 架构测试模块
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
| ORM框架 | Entity Framework Core | 8.0.x | ✅ 已实现 |
| 数据访问 | Npgsql + Dapper | 8.0.x | ✅ 已实现 |

## ✨ 核心功能

### 1. 推理引擎模块
- **RAG检索增强生成**：基于 BM25 算法实现文档检索
- **CoT思维链推理**：支持同步/流式输出
- **ReAct交互式推理**：支持工具调用与反馈循环
- **合规规则验证**：集成化工行业合规知识库

### 2. 会话管理
- 基于内存的对话历史管理
- 支持多轮对话上下文保持
- 会话生命周期管理

### 3. 知识库服务
- 支持文档解析与加载
- 基于 BM25 的倒排索引检索
- 化工专业术语识别与优先级排序

### 4. 审计日志
- 完整的操作日志记录
- 符合等保三级要求

### 5. 数据库服务
- PostgreSQL 数据库连接与管理
- 会话记录持久化
- 审计日志存储
- 检索日志追踪
- 数据库连接验证功能

### 6. ORM框架支持
- Entity Framework Core 8.0 完整集成
- 支持数据库迁移（Migrations）
- LINQ 查询支持
- 实体映射与关系配置

## 🚀 快速开始

### 环境要求
- .NET 8 SDK
- PostgreSQL 16+（可选，用于数据持久化）

### 安装步骤

1. **克隆项目**
```bash
git clone https://gitee.com/liuchao_yu/agent-system.git
cd agent-system
```

2. **配置知识库路径**

编辑 `Agent1/Config/AppConfig.cs`，配置知识库路径：

```csharp
public class ChemicalKnowledgeBaseConfig
{
    public string BasePath { get; set; } = @"d:\桌面\agent\化工知识库";
}
```

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

### 知识库配置
```csharp
public class ChemicalKnowledgeBaseConfig
{
    // 知识库基础路径
    public string BasePath { get; set; } = @"d:\桌面\agent\化工知识库";
    
    // 知识源配置（支持优先级设置）
    public List<KnowledgeSourceConfig> Sources { get; set; } = new()
    {
        new() { Name = "国标", Path = "国标", Priority = 100 },
        new() { Name = "园区规则", Path = "园区规则", Priority = 80 },
        new() { Name = "历史案例", Path = "历史案例", Priority = 60 }
    };
}
```

### 数据库配置
```csharp
public class DatabaseConfig
{
    public string Provider { get; set; } = "PostgreSQL";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string DatabaseName { get; set; } = "chemical_park_ai_agent";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "your_password";
}
```

### 数据库表结构
系统自动创建以下数据表：
- `sessions` - 会话记录表
- `audit_logs` - 审计日志表（等保三级要求）
- `search_logs` - 检索记录表

### EF Core 数据库迁移

```bash
# 创建迁移
dotnet ef migrations add InitialCreate --project Agent1

# 更新数据库
dotnet ef database update --project Agent1

# 查看迁移历史
dotnet ef migrations list --project Agent1
```

## 📊 项目状态

- **当前阶段**: P0/P1 功能开发
- **状态**: 开发中
- **目标**: 等保三级合规要求

## 📈 未来规划

| 阶段 | 功能 | 状态 |
|------|------|------|
| P2 | 集成 Milvus 向量数据库 | ⏳ 待开发 |
| P2 | PostgreSQL 关系数据库 | ✅ 已实现 |
| P3 | 分布式部署支持 | ⏳ 待规划 |

## 📝 贡献指南

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

---

**化工园区危化品合规审查 AI Agent** - 助力化工企业实现智能化合规管理
