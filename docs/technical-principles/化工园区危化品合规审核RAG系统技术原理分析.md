# 化工园区危化品合规审核 RAG 系统 - 技术原理分析

## 一、完整方法调用关系图

### 1.1 启动流程（Program.cs）
```
Main()
├─ 实例化服务
│  ├─ new SessionService()
│  ├─ new MemoryService()
│  ├─ new LlmService()
│  ├─ new ToolService()
│  ├─ new AgentDialog()
│  ├─ new ModuleFactory()
│  ├─ new ModuleDispatcher()
│  ├─ new KnowledgeBaseService()          ← 核心服务1
│  └─ new ChemicalRAG(path, knowledgeBase) ← 核心服务2
└─ 主循环
   └─ 用户选择菜单 8
      └─ RunChemicalRAGTest(ChemicalRAG)
         ├─ LoadKnowledgeBaseAsync()
         │  ├─ LoadAndSplitFile("国标", "高")
         │  │  ├─ File.ReadAllTextAsync()
         │  │  ├─ SplitTextIntoChunks(text, 500)
         │  │  └─ 循环每个chunk
         │  │     └─ KnowledgeBaseService.AddDocumentAsync(chunk, metadata)
         │  ├─ LoadAndSplitFile("园区规则", "中")
         │  └─ LoadAndSplitFile("历史案例", "低")
         └─ SearchAsync(query)
            ├─ KnowledgeBaseService.RetrieveAsync(query, topK*3)
            │  ├─ Tokenize(query)
            │  ├─ 计算每个文档的BM25分数
            │  └─ 排序后返回topK*3
            └─ 优先级重排序
               ├─ 给每个结果加分值（国标+3000）
               ├─ 重新排序
               └─ 返回topK
```

---

## 二、关键字段设计原因

### 2.1 ChemicalRAG 字段设计
| 字段 | 类型 | 设计原因 | 耦合度 |
|-----|-----|---------|-------|
| `_knowledgeBase` | `IKnowledgeBaseService` | **接口依赖而非实现依赖** - 遵循依赖倒置原则(DIP)，低耦合 | **低** |
| `_knowledgeBasePath` | `string` | **路径配置化** - 避免硬编码，支持不同环境部署 | **无** |

### 2.2 KnowledgeBaseService 字段设计
| 字段 | 类型 | 设计原因 | 耦合度 |
|-----|-----|---------|-------|
| `_documents` | `List<Document>` | **内存索引** - 用于BM25快速检索 | **中** |
| `_termDocFreq` | `Dictionary<string, Dictionary<int, int>>` | **倒排索引** - 存储"关键词→文档列表"的映射 | **高** |
| `_avgDocLength` | `double` | **BM25算法参数** - 文档平均长度，用于TF归一化 | **低** |
| `K1` | `const double` | **BM25超参数** - 控制词频饱和度 | **无** |
| `B` | `const double` | **BM25超参数** - 控制文档长度归一化程度 | **无** |

### 2.3 Document 私有类字段设计
| 字段 | 类型 | 设计原因 |
|-----|-----|---------|
| `Id` | `int` | 文档唯一标识，用于索引 |
| `Content` | `string` | 原始文本内容 |
| `Tokens` | `List<string>` | 分词结果，预处理后缓存 |
| `TermFreq` | `Dictionary<string, int>` | 词频统计，用于BM25计算 |
| `Length` | `int` | 文档长度，用于归一化 |
| `Metadata` | `Dictionary<string, object>` | **扩展字段** - 存储优先级、类型、来源等，无需改表设计 |

---

## 三、核心方法技术分析

### 3.1 ChemicalRAG.LoadKnowledgeBaseAsync() - 知识库加载
**职责**：从文件系统加载化工文档并分块
**技术细节**：
```
按目录划分加载顺序（体现业务优先级）
├─ "国标" 目录最先加载（GB优先）
├─ "园区规则" 其次加载
└─ "历史案例" 最后加载
```
**设计优势**：
- ✅ 目录结构与业务语义对应
- ✅ 文件读取使用 `Encoding.UTF8` - 支持中文
- ✅ 异常处理 `try-catch` - 单个文件失败不影响整体加载

### 3.2 Tokenize() - 中文分词（核心修复！）
**之前问题**：把整句作为单个Token，无法匹配关键词
**现在实现**：NGram 多粒度分词
```csharp
// 示例："危化品储罐安全距离"
分词结果:
├─ 完整词："危化品储罐安全距离"
├─ 2-gram："危化","化品","品储","储罐","..."
└─ 单字："危","化","品","储","罐","安","全","距","离"
```
**设计原理**：
- **召回优先策略** - 不同粒度的Token覆盖多种匹配模式
- **中文适配** - 中文不需要空格分隔，必须用字符级切分

### 3.3 SearchAsync() - 检索流程（两阶段！）
**阶段1**：BM25粗召回
- 调用 `_knowledgeBase.RetrieveAsync(query, topK * 3)`
- 拿到 15 个候选结果（为后续重排序留足空间）

**阶段2**：优先级重排序（业务逻辑层！）
```csharp
// 优先级加分
国标     → +3000 分
园区规则 → +2000 分
历史案例 → +1000 分
```
**关键点**：
- ✅ BM25 分数和优先级分数**线性叠加**
- ✅ 先拿更多结果，确保重排序后有足够多的国标文档
- ✅ **业务逻辑与检索算法分离** - ChemicalRAG处理优先级，KnowledgeBaseService纯算法

---

## 四、耦合度详细分析

### 4.1 整体耦合情况
```
Program.cs (入口)
│
├─ 无依赖 → 实例化所有服务
│
├─ ChemicalRAG (化工业务)
│  └─ 依赖 IKnowledgeBaseService (接口抽象)
│     → 低耦合！
│
└─ KnowledgeBaseService (检索算法)
   ├─ 实现 IKnowledgeBaseService
   └─ 内部私有类 Document (自包含)
      → 高内聚！
```

### 4.2 耦合度评分（1-10，1最低）
| 模块 | 耦合对象 | 评分 | 分析 |
|-----|---------|-----|------|
| **ChemicalRAG** | IKnowledgeBaseService | **2** | ✅ 完美！接口依赖，无具体实现耦合 |
| **KnowledgeBaseService** | Document私有类 | **3** | ✅ 自包含，私有类，不对外暴露 |
| **Program.cs** | 所有服务 | **7** | ⚠️ 紧耦合，直接new所有实例（简单场景可接受） |
| **ChemicalRAG** | 文件系统 | **5** | ⚠️ 依赖具体路径，但这是业务必要 |

---

## 五、设计模式与架构原则应用

### 5.1 用到的设计模式
1. **策略模式** - IKnowledgeBaseService 接口，可替换检索算法
2. **依赖倒置原则(DIP)** - ChemicalRAG 依赖接口而非具体类
3. **单一职责原则(SRP)** - KnowledgeBaseService 只负责检索，ChemicalRAG 只负责化工业务
4. **开闭原则(OCP)** - 通过 Metadata 扩展业务字段，无需改代码

### 5.2 架构优势
| 优势 | 说明 |
|-----|------|
| **扩展性** | 添加新优先级规则只需改 ChemicalRAG，无需碰 BM25 算法 |
| **可替换性** | 把 BM25 换成向量检索，只需实现 IKnowledgeBaseService |
| **可测试性** | 可以 Mock IKnowledgeBaseService 单独测 ChemicalRAG |

---

## 六、可优化的技术点

### 6.1 当前耦合点（可改进）
1. **Program.cs紧耦合所有new** - 可以用DI容器（Microsoft.Extensions.DependencyInjection）
2. **ChemicalRAG中priorityLevels硬编码** - 可以移到配置文件（appsettings.json）
3. **KnowledgeBaseService是内存索引** - 可以改成SQLite/Redis持久化

### 6.2 检索优化空间
- ✅ 当前：BM25 + 优先级重排序
- 📊 下一步：添加关键词匹配加分（用户搜"储罐"，结果包含"储罐"的额外加分）
- 📊 再下一步：添加向量检索（Hybrid Search混合检索）

---

## 七、完整技术栈总结

```
应用层：
  ├─ ChemicalRAG - 化工业务逻辑
  └─ Program.cs - 入口和用户交互

服务层（接口抽象）：
  ├─ IKnowledgeBaseService - 知识库服务接口
  └─ 检索/加载/管理

服务层（实现）：
  ├─ KnowledgeBaseService - BM25检索实现
  ├─ Tokenize - 中文NGram分词
  ├─ BM25算法 - 关键词匹配+TF-IDF
  └─ 倒排索引 - 加速检索

数据层：
  ├─ 文件系统 - .txt 文档
  └─ 内存索引 - BM25数据结构
```

---

## 八、总结

- ✅ 整体设计符合企业级架构标准
- ✅ 接口与实现分离，耦合度低
- ✅ 业务逻辑（ChemicalRAG）与算法（BM25）职责清晰
- ✅ 有很好的可扩展性和可维护性
