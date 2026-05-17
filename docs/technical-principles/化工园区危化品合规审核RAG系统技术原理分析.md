# 化工园区危化品合规审核RAG系统技术原理分析

## 一、启动流程

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
│  ├─ new KnowledgeBaseService()
│  └─ new ChemicalRAG(path, knowledgeBase)
└─ 主循环
   └─ RunChemicalRAGTest()
      ├─ LoadKnowledgeBaseAsync()
      └─ SearchAsync(query)
```

## 二、核心服务设计

### 2.1 ChemicalRAG

| 字段 | 类型 | 设计原因 |
|-----|-----|---------|
| `_knowledgeBase` | `IKnowledgeBaseService` | 接口依赖，遵循DIP原则 |
| `_knowledgeBasePath` | `string` | 路径配置化 |

### 2.2 KnowledgeBaseService

| 字段 | 类型 | 设计原因 |
|-----|-----|---------|
| `_documents` | `List<Document>` | 内存索引，BM25快速检索 |
| `_termDocFreq` | `Dictionary<string, Dictionary<int, int>>` | 倒排索引 |
| `_avgDocLength` | `double` | BM25算法参数 |
| `K1` | `const double` | BM25超参数 |
| `B` | `const double` | BM25超参数 |

### 2.3 Document

| 字段 | 类型 | 设计原因 |
|-----|-----|---------|
| `Id` | `int` | 文档唯一标识 |
| `Content` | `string` | 原始文本内容 |
| `Tokens` | `List<string>` | 分词结果缓存 |
| `TermFreq` | `Dictionary<string, int>` | 词频统计 |
| `Metadata` | `Dictionary<string, object>` | 扩展字段 |

## 三、检索流程

### 3.1 知识库加载

按目录划分加载顺序（体现业务优先级）：
1. "国标" 目录最先加载（GB优先）
2. "园区规则" 其次加载
3. "历史案例" 最后加载

### 3.2 两阶段检索

1. **BM25粗召回**：基于关键词相关性召回候选文档
2. **业务优先级重排序**：叠加业务优先级分数

## 四、设计原则应用

| 原则 | 应用场景 |
|------|---------|
| 依赖倒置原则(DIP) | ChemicalRAG依赖IKnowledgeBaseService接口 |
| 单一职责原则(SRP) | KnowledgeBaseService只负责检索 |
| 开闭原则(OCP) | Document的Metadata字段支持扩展 |
