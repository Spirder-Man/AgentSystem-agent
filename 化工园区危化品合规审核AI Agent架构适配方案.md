
# 化工园区危化品企业安全合规审核AI Agent架构适配方案

## 一、适配分析结论

**核心结论：不需要重构现有架构，仅需增量扩展！**

当前项目架构（接口+DI+模块工厂+服务层）完全符合化工园区场景的企业级要求，只需在现有基础上做增量扩展即可。

---

## 二、当前架构与化工场景的适配性分析

| 当前架构组件 | 化工场景需求 | 适配方式 |
|-------------|-------------|---------|
| **接口层** | 标准化模块接口 | ✅ 无需修改，直接复用 `IInferenceModule` |
| **依赖注入** | 服务解耦、可测试 | ✅ 无需修改，继续使用DI |
| **模块工厂** | 灵活切换推理范式 | ✅ 无需修改，扩展新模块类型 |
| **LlmService** | 私有化本地模型 | ✅ 无需修改，配置多模态模型（如Qwen-VL） |
| **SessionService** | 会话记忆、操作留痕 | ✅ 扩展审计日志功能 |
| **KnowledgeBaseService** | 多模态检索 | 🔧 扩展支持图片/结构化数据 |
| **MemoryService** | 历史审核记录 | ✅ 无需修改 |

---

## 三、扩展架构设计（增量式）

### 3.1 整体架构图

```
┌───────────────────────────────────────────────────────────────────────┐
│                        用户层（工业内网）                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐               │
│  │ EHS专员PC   │  │ 安全管理部   │  │ 工业平板     │               │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘               │
└─────────┼──────────────────┼──────────────────┼──────────────────────┘
          │                  │                  │
┌─────────▼──────────────────▼──────────────────▼──────────────────────┐
│                        应用层（Agent）                                │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  合规审核模块（新增）                                         │  │
│  │  - 日常合规自查模块                                          │  │
│  │  - 整改工单跟进模块                                          │  │
│  │  - 监管核查辅助模块                                          │  │
│  └───────────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  现有推理模块（保留）                                         │  │
│  │  - RAGModule / CoT / ReAct                                    │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────┬────────────────────────────────────────────────────────────┘
          │
┌─────────▼────────────────────────────────────────────────────────────┐
│                        服务层（扩展）                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐               │
│  │ LlmService  │  │SessionService│  │MemoryService │               │
│  │ (多模态配置) │  │ (扩展审计)   │  │ (审核历史)   │               │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘               │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐               │
│  │ KnowledgeBase│  │IntegrationSvc│  │ AuditService │               │
│  │ (多模态检索) │  │ (ERP/WMS/EHS)│  │ (操作审计)   │               │
│  └──────────────┘  └──────────────┘  └──────────────┘               │
└─────────┬────────────────────────────────────────────────────────────┘
          │
┌─────────▼────────────────────────────────────────────────────────────┐
│                        数据层（新增）                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐               │
│  │ 向量库      │  │ 审计日志库   │  │ 业务数据库   │               │
│  │ (Milvus)    │  │ (SQLite/PG)  │  │ (ERP/WMS)    │               │
│  └──────────────┘  └──────────────┘  └──────────────┘               │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 三、详细扩展方案

### 3.1 新增/扩展服务

#### 3.1.1 扩展 KnowledgeBaseService（多模态支持）

**新增接口：**
```csharp
// IKnowledgeBaseService 扩展
public interface IKnowledgeBaseService
{
    // 现有方法（保留）
    Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null);
    Task<List&lt;RetrievedChunk&gt;&gt; RetrieveAsync(string query, int topK = 5);
    
    // 新增方法
    Task AddImageAsync(string imagePath, string? description = null, Dictionary&lt;string, object&gt;? metadata = null);
    Task AddStructuredDataAsync&lt;T&gt;(T data, Dictionary&lt;string, object&gt;? metadata = null);
    Task&lt;List&lt;RetrievedChunk&gt;&gt; MultimodalRetrieveAsync(string query, string? imagePath = null, int topK = 5);
}
```

**实现思路：**
- 图片：提取视觉特征（使用Qwen-VL等多模态模型）+ 文本描述
- 结构化数据：序列化为JSON文本 + 元数据标签
- 分库检索：文本库、图片库、工单库分别检索后融合

---

#### 3.1.2 新增 IntegrationService（工业系统集成）

**接口设计：**
```csharp
public interface IIntegrationService
{
    // ERP/WMS对接
    Task&lt;List&lt;WarehouseRecord&gt;&gt; GetWarehouseRecordsAsync(string chemicalName = null);
    Task&lt;List&lt;EHSTicket&gt;&gt; GetEHSTicketsAsync(bool? isCompleted = null);
    
    // 数据同步
    Task SyncERPDataAsync();
    Task SyncWMSDataAsync();
}
```

**实现要点：**
- 支持工业内网协议（HTTP/OPC UA）
- 本地缓存机制（网络断开时降级）
- 数据脱敏（危化品位置等敏感信息）

---

#### 3.1.3 新增 AuditService（等保三级审计）

**接口设计：**
```csharp
public interface IAuditService
{
    Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false);
    Task&lt;List&lt;AuditLog&gt;&gt; GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null);
    Task&lt;string&gt; ExportAuditReportAsync(DateTime startTime, DateTime endTime);
}
```

**审计内容：**
- 用户登录/退出
- 合规审核操作
- 知识库访问
- 数据导出
- 配置修改

---

#### 3.1.4 扩展 SessionService（操作留痕）

```csharp
// 现有 SessionService 扩展
public class SessionService : ISessionService
{
    // 现有方法
    public Task&lt;string&gt; CreateSessionAsync(SessionType type);
    public Task AddDialogTurnAsync(string sessionId, string role, string content);
    
    // 新增审计关联
    public Task AddDialogTurnWithAuditAsync(string sessionId, string role, string content, string userId);
}
```

---

### 3.2 新增推理模块

#### 3.2.1 ComplianceCheckModule（日常合规自查）

```csharp
public class ComplianceCheckModule : IInferenceModule
{
    public string Name =&gt; "日常合规自查";
    public string Description =&gt; "上传巡检图片，自动审核合规性";
    
    private readonly IKnowledgeBaseService _kbService;
    private readonly ILlmService _llmService;
    private readonly IIntegrationService _integrationService;
    private readonly IAuditService _auditService;
    
    public async Task RunAsync()
    {
        // 1. 用户输入：图片 + 核查维度
        // 2. 多模态RAG检索国标+历史案例
        // 3. 调用IntegrationService验证仓储数据
        // 4. LLM生成合规判定报告
        // 5. AuditService记录操作
    }
}
```

---

#### 3.2.2 TicketFollowupModule（整改工单跟进）

```csharp
public class TicketFollowupModule : IInferenceModule
{
    public string Name =&gt; "整改工单跟进";
    public string Description =&gt; "对接EHS工单，自动跟进整改进度";
    
    // 核心流程：
    // 1. 获取未整改工单
    // 2. 匹配对应合规要求
    // 3. 生成整改提醒
    // 4. 生成整改标准（含国标依据）
}
```

---

#### 3.2.3 RegulatoryAuditModule（监管核查辅助）

```csharp
public class RegulatoryAuditModule : IInferenceModule
{
    public string Name =&gt; "监管核查辅助";
    public string Description =&gt; "自动生成合规核查报告，含证据链";
    
    // 核心流程：
    // 1. 获取核查清单
    // 2. 检索相关文档/图片/工单
    // 3. 生成核查报告
    // 4. 本地存储 + 操作审计
}
```

---

### 3.3 配置适配

#### 3.3.1 ModelConfig.cs 扩展

```csharp
public static class ModelConfig
{
    // 现有配置
    public const string ModelId = "deepseek-r1:local7b";
    public static readonly Uri Endpoint = new Uri("http://localhost:11434");
    
    // 新增多模态模型配置
    public const string MultimodalModelId = "qwen-vl:latest";
    
    // 化工园区特定配置
    public static class ChemicalParkConfig
    {
        public const string KnowledgeBasePath = "./data/chemical-knowledge";
        public const string AuditLogRetentionDays = "180"; // 等保三级要求6个月
        public const bool EnableDataEncryption = true;
    }
}
```

---

#### 3.3.2 ModuleType 扩展

```csharp
public enum ModuleType
{
    // 现有类型（保留）
    CoTSolid = 1,
    CoTStream = 2,
    ReActSolid = 3,
    ReActStream = 4,
    Reflection = 5,
    RAG = 6,
    UnifiedDialog = 7,
    
    // 新增化工园区专用类型
    ComplianceCheck = 8,      // 日常合规自查
    TicketFollowup = 9,       // 整改工单跟进
    RegulatoryAudit = 10      // 监管核查辅助
}
```

---

## 四、等保三级安全措施

### 4.1 数据安全

| 措施 | 实现方式 |
|-----|---------|
| 数据传输加密 | 内网HTTPS |
| 数据存储加密 | 敏感字段AES加密 |
| 数据脱敏 | 危化品存储位置、人员信息脱敏 |

### 4.2 访问控制

| 措施 | 实现方式 |
|-----|---------|
| 身份认证 | 用户名密码+Token |
| 权限隔离 | EHS专员/安全管理部/管理员三级权限 |
| 操作审计 | 所有操作记录审计日志，留存6个月 |

### 4.3 可用性

| 措施 | 实现方式 |
|-----|---------|
| 服务高可用 | RAG检索服务多实例部署 |
| 降级策略 | 网络断开时使用本地缓存 |
| 数据备份 | 知识库定期本地备份 |

---

## 五、实施步骤（建议）

### 阶段1：基础扩展（1-2周）
1. 扩展 KnowledgeBaseService 支持多模态
2. 新增 AuditService
3. 配置多模态模型

### 阶段2：核心功能（2-3周）
1. 实现 ComplianceCheckModule
2. 实现 IntegrationService（基础版）
3. 扩展 SessionService 审计功能

### 阶段3：完整功能（2-3周）
1. 实现 TicketFollowupModule
2. 实现 RegulatoryAuditModule
3. 完善 IntegrationService（ERP/WMS全对接）

### 阶段4：安全加固（1周）
1. 等保三级安全措施
2. 数据加密
3. 权限控制

---

## 六、文件变更清单（仅新增/修改，不删除）

### 新增文件
```
Agent1/
├── Services/
│   ├── IIntegrationService.cs      (新增)
│   ├── IntegrationService.cs       (新增)
│   ├── IAuditService.cs            (新增)
│   ├── AuditService.cs             (新增)
│   └── Models/                     (新增目录)
│       ├── WarehouseRecord.cs
│       ├── EHSTicket.cs
│       └── AuditLog.cs
├── Modules/
│   ├── ComplianceCheckModule.cs    (新增)
│   ├── TicketFollowupModule.cs     (新增)
│   └── RegulatoryAuditModule.cs    (新增)
└── ChemicalParkConfig.cs           (新增)
```

### 修改文件（兼容式修改）
```
Agent1/
├── Services/
│   ├── IKnowledgeBaseService.cs    (扩展接口)
│   ├── KnowledgeBaseService.cs     (扩展实现)
│   ├── ISessionService.cs          (扩展接口)
│   └── SessionService.cs           (扩展实现)
├── ModelConfig.cs                  (扩展配置)
├── ModuleType.cs                   (扩展枚举)
├── ModuleFactory.cs                (扩展模块创建)
└── Program.cs                      (扩展菜单选项)
```

---

## 七、总结

✅ **不需要重构**，现有架构完全符合要求  
✅ **仅需增量扩展**，保留所有现有功能  
✅ **向后兼容**，原有推理模块继续使用  
✅ **等保三级**，通过扩展审计/权限/加密实现  
✅ **私有化部署**，所有数据/模型100%本地

---

## 八、下一步行动

请您确认：
1. 此适配方案是否符合预期？
2. 是否需要调整某些部分？
3. 确认后，我将按阶段开始实施

