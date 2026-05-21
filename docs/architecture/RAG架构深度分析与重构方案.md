
# RAG架构深度分析与重构方案

## 版本信息
- **文档版本**: v1.0
- **创建日期**: 2026-05-18
- **适用项目**: 化工园区危化品合规审核AI Agent
- **技术栈**: .NET 8 + PostgreSQL 16 + pgvector

---

## 目录
1. [现有架构概览](#1-现有架构概览)
2. [核心问题诊断](#2-核心问题诊断)
3. [现有代码深度分析](#3-现有代码深度分析)
4. [重构方案设计](#4-重构方案设计)
5. [实施路线图](#5-实施路线图)

---

## 1. 现有架构概览

### 1.1 架构分层结构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        当前架构分层                                  │
├─────────────────────────────────────────────────────────────────────┤
│  Presentation Layer                                                 │
│    └── Program.cs (控制台入口)                                        │
├─────────────────────────────────────────────────────────────────────┤
│  Module Layer (推理模块)                                             │
│    ├── RAGModule.cs         # RAG推理模块                            │
│    └── ComplianceCheckModule.cs  # 合规检查模块                       │
├─────────────────────────────────────────────────────────────────────┤
│  Service Layer (核心服务)                                            │
│    ├── IKnowledgeBaseService.cs   # 知识库接口                       │
│    ├── KnowledgeBaseService.cs    # BM25检索实现                     │
│    ├── HybridKnowledgeBaseService.cs # 混合检索实现                  │
│    └── DatabaseService.cs         # 数据库服务(含向量检索)            │
├─────────────────────────────────────────────────────────────────────┤
│  Business Layer (业务逻辑)                                            │
│    ├── ChemicalRAG.cs       # 化工专用RAG逻辑                        │
│    └── RAG.cs               # 通用RAG逻辑                            │
├─────────────────────────────────────────────────────────────────────┤
│  Data Layer (数据层)                                                 │
│    ├── PostgreSQL + pgvector  # 向量存储                             │
│    └── knowledgebase/         # 本地文件知识库                        │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 核心组件关系图

```
Program.cs
    │
    ├──► ModuleFactory ──► RAGModule ──► RAG.cs
    │                           │
    │                           ├──► ILlmService (LLM服务)
    │                           ├──► ISessionService (会话服务)
    │                           └──► IKnowledgeBaseService (知识库服务)
    │
    ├──► ChemicalRAG ────────────────────► IKnowledgeBaseService
    │           │
    │           └──► HybridKnowledgeBaseService
    │                       │
    │                       ├──► KnowledgeBaseService (BM25)
    │                       └──► DatabaseService (向量检索)
    │                                   │
    │                                   └──► PostgreSQL + pgvector
```

---

## 2. 核心问题诊断

### 2.1 六大核心问题一览表

| 问题编号 | 问题类别 | 具体问题 | 影响 | 关联文件 |
|---------|---------|---------|------|---------|
| **Q1** | 文档格式支持 | 仅支持txt文件 | 无法处理企业级真实知识库（doc、docx、xls、xlsx、pdf） | `ChemicalRAG.cs`, `KnowledgeBaseService.cs` |
| **Q2** | 文件加载逻辑 | 分散在多个类中 | 代码冗余，难以维护 | `ChemicalRAG.cs`, `KnowledgeBaseService.cs` |
| **Q3** | 预处理管道 | 缺少统一的文档预处理流程 | 文档质量参差不齐，影响检索效果 | `KnowledgeBaseService.cs` |
| **Q4** | 索引管理 | 内存索引+数据库索引分离 | 数据一致性难以保证 | `KnowledgeBaseService.cs`, `HybridKnowledgeBaseService.cs` |
| **Q5** | 增量更新 | 无增量更新机制 | 知识库更新需要全量重建 | `IKnowledgeBaseService.cs` |
| **Q6** | 错误处理 | 缺乏统一的异常处理和日志 | 问题定位困难 | 全局 |

### 2.2 问题详细分析

#### Q1: 文档格式支持不足

**现状分析**：
- 当前仅支持 `.txt` 文件格式
- `ChemicalRAG.LoadKnowledgeBaseAsync()` 中硬编码了 `*.txt` 模式
- 企业级知识库包含多种格式：`.doc`, `.docx`, `.xls`, `.xlsx`, `.pdf`

**现有代码位置** (`ChemicalRAG.cs:55-86`)：
```csharp
// 当前仅支持txt文件
var files = Directory.GetFiles(gbDir, "*.txt");
totalFiles += files.Length;
foreach (var file in files)
{
    var chunks = await LoadAndSplitFile(file, "国标", "高");
    totalChunks += chunks.Count;
}
```

**影响**：无法直接使用企业级真实知识库，需要人工转换格式。

---

#### Q2: 文件加载逻辑分散

**现状分析**：
- `ChemicalRAG.LoadKnowledgeBaseAsync()` 负责遍历目录和加载
- `KnowledgeBaseService.LoadChemicalKnowledgeBaseAsync()` 也有类似逻辑
- 两者功能重叠，代码冗余

**现有代码对比**：

| 文件 | 方法 | 职责 |
|-----|------|------|
| `ChemicalRAG.cs` | `LoadKnowledgeBaseAsync()` | 加载国标/园区规则/历史案例目录 |
| `KnowledgeBaseService.cs` | `LoadChemicalKnowledgeBaseAsync()` | 同样加载国标/园区规则/历史案例目录 |

**影响**：修改知识库加载逻辑需要修改多个文件，维护成本高。

---

#### Q3: 缺少统一的文档预处理管道

**现状分析**：
- 当前仅在 `KnowledgeBaseService.Tokenize()` 中有简单的分词处理
- 缺少文档清洗、格式标准化、噪声过滤等环节
- 直接将原始文件内容分块索引，影响检索精度

**现有代码位置** (`KnowledgeBaseService.cs:199-238`)：
```csharp
private List<string> Tokenize(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return new List<string>();

    var cleanedText = text.Replace("\n", " ").Replace("\r", " ");
    // ... 分词逻辑
}
```

**问题**：预处理能力不足，无法处理：
- 文档中的页眉页脚
- 表格数据
- 特殊字符和格式噪声
- 编码问题

---

#### Q4: 索引管理不一致

**现状分析**：
- `KnowledgeBaseService` 维护内存中的倒排索引 (`_termDocFreq`)
- `DatabaseService` 维护 PostgreSQL 中的向量索引
- `HybridKnowledgeBaseService` 同时操作两者
- 没有统一的索引同步机制

**现有代码位置** (`HybridKnowledgeBaseService.cs:27-57`)：
```csharp
public async Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null)
{
    await _bm25Service.AddDocumentAsync(content, metadata);  // 内存索引
    
    try
    {
        // ... 向量索引
        var embedding = await _llmService.GetEmbeddingAsync(content);
        await _databaseService.AddChemicalDocumentAsync(content, ..., embedding);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ⚠️ 向量化添加失败: {ex.Message}");
        // 内存索引已更新，但向量索引失败，数据不一致！
    }
}
```

**问题**：异常情况下内存索引和数据库索引不同步，导致检索结果不一致。

---

#### Q5: 无增量更新机制

**现状分析**：
- 当前只有 `AddDocumentAsync()` 和 `Clear()` 方法
- 缺少 `UpdateDocumentAsync()` 和 `DeleteDocumentAsync()`
- 更新知识库需要先 `Clear()` 再全量加载

**现有接口** (`IKnowledgeBaseService.cs`)：
```csharp
public interface IKnowledgeBaseService
{
    Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null);
    Task AddDocumentsAsync(IEnumerable<string> contents);
    Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5);
    // ... 缺少 Update/Delete 方法
}
```

**影响**：知识库更新效率低下，无法支持实时更新场景。

---

#### Q6: 缺乏统一的异常处理和日志

**现状分析**：
- 异常处理零散，使用 `Console.WriteLine` 简单输出
- 没有统一的日志框架
- 缺少错误追踪和告警机制

**现有代码示例** (`HybridKnowledgeBaseService.cs:53-56`)：
```csharp
catch (Exception ex)
{
    Console.WriteLine($"   ⚠️ 向量化添加失败: {ex.Message}");
}
```

**影响**：
- 生产环境难以定位问题
- 无法满足等保三级的审计要求
- 缺少监控告警能力

---

## 3. 现有代码深度分析

### 3.1 核心服务类分析

#### 3.1.1 KnowledgeBaseService（BM25检索服务）

**定位**：基于BM25算法的内存检索服务

**核心数据结构**：
```csharp
private readonly List<Document> _documents = new List<Document>();  // 文档列表
private readonly Dictionary<string, Dictionary<int, int>> _termDocFreq = new Dictionary<string, Dictionary<int, int>>();  // 倒排索引
private double _avgDocLength = 0;  // 平均文档长度
```

**核心算法**（BM25评分）：
```csharp
// KnowledgeBaseService.cs:146-148
double idf = Math.Log((_documents.Count - df + 0.5) / (df + 0.5) + 1);
double tfComp = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * (doc.Length / _avgDocLength)));
score += idf * tfComp;
```

**分词策略**：
- 基础分词：按分隔符切分
- N-gram分词：生成2-gram和单字分词
- 支持中文处理

---

#### 3.1.2 HybridKnowledgeBaseService（混合检索服务）

**定位**：整合BM25和向量检索的混合服务

**检索模式**：
```csharp
// HybridKnowledgeBaseService.cs:64-80
public async Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
{
    var mode = _kbConfig.SearchMode?.ToLowerInvariant() ?? "hybrid";
    
    switch (mode)
    {
        case "bm25":
            return await Bm25RetrieveAsync(query, topK);
        case "vector":
            return await VectorRetrieveAsync(query, topK);
        case "hybrid":
        default:
            return await HybridRetrieveAsync(query, topK);
    }
}
```

**混合检索算法**：
```csharp
// HybridKnowledgeBaseService.cs:221-223
var hybridScore = x.bm25Score * _vectorConfig.Bm25Weight + x.vectorScore * _vectorConfig.VectorWeight;
// 默认权重：BM25=0.4，Vector=0.6
```

---

#### 3.1.3 ChemicalRAG（化工专用RAG）

**定位**：针对化工合规场景的RAG封装

**核心流程**：
1. `LoadKnowledgeBaseAsync()` - 加载知识库
2. `LoadAndSplitFile()` - 加载并分块文件
3. `SplitTextIntoChunks()` - 文本分块（500字符/块）
4. `SearchAsync()` - 执行检索并重排序

**分块策略**：
```csharp
// ChemicalRAG.cs:138-171
private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
{
    // 按段落分割
    var paragraphs = text.Split(new char[] { '\r', '\n' }, ...)
        .Select(p => p.Trim())
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .ToList();
    
    // 合并段落直到达到最大chunk大小
    // ...
}
```

**重排序逻辑**：
```csharp
// ChemicalRAG.cs:194-219
var priorityLevels = new Dictionary<string, int>
{
    { "国标", 3000 },
    { "园区规则", 2000 },
    { "历史案例", 1000 }
};

var rerankedResults = bm25Results
    .Select(r => new { 
        Result = r, 
        AdjustedScore = r.Score + priority 
    })
    .OrderByDescending(x => x.AdjustedScore)
    .Take(topK)
    .Select(x => x.Result)
    .ToList();
```

---

#### 3.1.4 DatabaseService（数据库服务）

**定位**：PostgreSQL数据库操作封装，支持向量存储和检索

**表设计**：
```sql
-- 从 DatabaseService.cs:129-139 提取
CREATE TABLE IF NOT EXISTS chemical_documents (
    id SERIAL PRIMARY KEY,
    content TEXT NOT NULL,
    embedding vector(768),
    regulation_type VARCHAR(50) NOT NULL,
    priority VARCHAR(20) NOT NULL,
    source_file VARCHAR(200),
    chemical_type VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
```

**向量索引**（HNSW）：
```sql
CREATE INDEX IF NOT EXISTS idx_chemical_documents_embedding_hnsw 
ON chemical_documents USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 200);
```

---

### 3.2 模块层分析

#### RAGModule

**定位**：RAG推理模块，实现 `IInferenceModule` 接口

**核心实现**：
```csharp
// RAGModule.cs
public class RAGModule : IInferenceModule
{
    private readonly RAG _rag;
    
    public RAGModule(ILlmService llmService, ISessionService sessionService)
    {
        _rag = new RAG(llmService, sessionService);
    }
    
    public async Task RunAsync()
    {
        await _rag.RunRAGReflectionStreamTools();
    }
}
```

---

### 3.3 配置层分析

#### AppConfig 配置结构

```csharp
// AppConfig.cs
public class AppConfig
{
    public ChemicalLlmConfig Llm { get; set; }           // LLM配置
    public ChemicalKnowledgeBaseConfig KnowledgeBase { get; set; } // 知识库配置
    public VectorSearchConfig VectorSearch { get; set; }   // 向量检索配置
    public DatabaseConfig Database { get; set; }           // 数据库配置
    public IntegrationConfig Integration { get; set; }     // 集成配置
    public AuditConfig Audit { get; set; }                 // 审计配置
}
```

**关键配置项**：

| 配置类别 | 关键项 | 当前值 | 说明 |
|---------|-------|--------|------|
| 知识库 | `BasePath` | `d:\桌面\agent\项目\Agent1\knowledgebase` | 知识库根目录 |
| 知识库 | `ChunkSize` | 500 | 分块大小（字符） |
| 知识库 | `SearchMode` | Hybrid | 检索模式 |
| 向量检索 | `Bm25Weight` | 0.4 | BM25权重 |
| 向量检索 | `VectorWeight` | 0.6 | 向量权重 |
| 向量检索 | `EmbeddingDimension` | 768 | 向量维度 |
| 数据库 | `Provider` | PostgreSQL | 数据库类型 |

---

## 4. 重构方案设计

### 4.1 目标架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        重构后架构分层                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  Presentation Layer                                                    │
│    └── Program.cs / API Controllers                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  Module Layer                                                          │
│    ├── RAGModule / ComplianceCheckModule / UnifiedDialogModule         │
├─────────────────────────────────────────────────────────────────────────┤
│  Service Layer                                                         │
│    ├── IKnowledgeManager      # 统一知识库管理器                       │
│    ├── IEmbeddingService      # 嵌入服务                               │
│    └── (原有服务保持不变)                                              │
├─────────────────────────────────────────────────────────────────────────┤
│  Pipeline Layer (新增)                                                 │
│    ├── IDocumentParser        # 文档解析器（支持多格式）                │
│    ├── IDocumentCleaner       # 文档清洗器                            │
│    ├── IDocumentSplitter      # 文档分块器                            │
│    ├── IIndexManager          # 索引管理器                             │
│    └── IDocumentPipeline      # 文档处理管道                          │
├─────────────────────────────────────────────────────────────────────────┤
│  Storage Layer                                                         │
│    └── PostgreSQL + pgvector                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 4.2 新增核心接口设计

#### 4.2.1 IDocumentParser（文档解析器）

```csharp
public interface IDocumentParser
{
    /// <summary>
    /// 支持的文件扩展名
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }
    
    /// <summary>
    /// 解析单个文件
    /// </summary>
    Task<DocumentParseResult> ParseFileAsync(string filePath);
    
    /// <summary>
    /// 批量解析
    /// </summary>
    Task<IEnumerable<DocumentParseResult>> ParseFilesAsync(IEnumerable<string> filePaths);
}

public class DocumentParseResult
{
    public string Content { get; set; }          // 提取的文本内容
    public string FilePath { get; set; }         // 原始文件路径
    public string FileType { get; set; }         // 文件类型
    public Dictionary<string, object> Metadata { get; set; } // 元数据
    public ParseStatus Status { get; set; }      // 解析状态
    public string? ErrorMessage { get; set; }    // 错误信息
}

public enum ParseStatus
{
    Success,
    PartialSuccess,
    Failed
}
```

**解析器实现规划**：

| 实现类 | 支持格式 | 依赖库 |
|-------|---------|--------|
| `TxtDocumentParser` | .txt | 内置 |
| `WordDocumentParser` | .doc, .docx | DocX/Documents.OpenXml |
| `ExcelDocumentParser` | .xls, .xlsx | EPPlus |
| `PdfDocumentParser` | .pdf | iTextSharp |
| `MarkdownDocumentParser` | .md | Markdig |

---

#### 4.2.2 IDocumentCleaner（文档清洗器）

```csharp
public interface IDocumentCleaner
{
    string Clean(string content, DocumentCleanOptions options = null);
}

public class DocumentCleanOptions
{
    public bool RemoveExtraWhitespace { get; set; } = true;      // 移除多余空白
    public bool RemoveSpecialCharacters { get; set; } = true;    // 移除特殊字符
    public bool NormalizeEncoding { get; set; } = true;          // 标准化编码
    public bool RemoveHeadersFooters { get; set; } = true;       // 移除页眉页脚
}
```

---

#### 4.2.3 IDocumentSplitter（文档分块器）

```csharp
public interface IDocumentSplitter
{
    List<DocumentChunk> Split(string content, ChunkOptions options);
}

public class DocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // Chunk唯一标识
    public string Content { get; set; }                          // chunk内容
    public int Index { get; set; }                               // 在文档中的序号
    public int StartPosition { get; set; }                       // 在原文中的起始位置
    public int EndPosition { get; set; }                         // 在原文中的结束位置
    public string? PreviousChunkId { get; set; }                 // 前一个chunk的ID
    public string? NextChunkId { get; set; }                     // 后一个chunk的ID
    public float[]? Embedding { get; set; }                      // 向量嵌入
    public Dictionary<string, object> Metadata { get; set; }     // 元数据
}

public class ChunkOptions
{
    public int MaxChunkSize { get; set; } = 500;      // 最大chunk大小（字符）
    public int MinChunkSize { get; set; } = 100;      // 最小chunk大小
    public int OverlapSize { get; set; } = 50;        // 重叠大小
    public ChunkStrategy Strategy { get; set; } = ChunkStrategy.Sentence;
}

public enum ChunkStrategy
{
    Sentence,    // 按句子分割
    Paragraph,   // 按段落分割
    FixedSize,   // 固定大小分割
    Smart        // 智能分割
}
```

---

#### 4.2.4 IIndexManager（索引管理器）

```csharp
public interface IIndexManager
{
    /// <summary>
    /// 创建索引
    /// </summary>
    Task CreateIndexAsync(string indexName);
    
    /// <summary>
    /// 删除索引
    /// </summary>
    Task DropIndexAsync(string indexName);
    
    /// <summary>
    /// 重建索引
    /// </summary>
    Task RebuildIndexAsync(string indexName);
    
    /// <summary>
    /// 添加文档到索引
    /// </summary>
    Task AddDocumentAsync(DocumentChunk chunk);
    
    /// <summary>
    /// 更新索引中的文档
    /// </summary>
    Task UpdateDocumentAsync(string chunkId, DocumentChunk chunk);
    
    /// <summary>
    /// 删除索引中的文档
    /// </summary>
    Task DeleteDocumentAsync(string chunkId);
    
    /// <summary>
    /// 检索
    /// </summary>
    Task<List<RetrievedChunk>> SearchAsync(string query, SearchOptions options);
    
    /// <summary>
    /// 获取索引状态
    /// </summary>
    Task<IndexStatus> GetIndexStatusAsync(string indexName);
}

public class SearchOptions
{
    public int TopK { get; set; } = 5;
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;
    public double Bm25Weight { get; set; } = 0.4;
    public double VectorWeight { get; set; } = 0.6;
    public Dictionary<string, string>? Filters { get; set; } // 过滤条件
}

public enum SearchMode
{
    BM25,
    Vector,
    Hybrid
}
```

---

#### 4.2.5 IDocumentPipeline（文档处理管道）

```csharp
public interface IDocumentPipeline
{
    /// <summary>
    /// 处理单个文件
    /// </summary>
    Task<PipelineResult> ProcessFileAsync(string filePath, PipelineOptions options);
    
    /// <summary>
    /// 处理目录
    /// </summary>
    Task<PipelineResult> ProcessDirectoryAsync(string directoryPath, PipelineOptions options);
    
    /// <summary>
    /// 处理文本内容
    /// </summary>
    Task<PipelineResult> ProcessContentAsync(string content, string sourceName, PipelineOptions options);
    
    /// <summary>
    /// 批量处理
    /// </summary>
    Task<List<PipelineResult>> ProcessBatchAsync(IEnumerable<string> filePaths, PipelineOptions options);
}

public class PipelineResult
{
    public bool Success { get; set; }
    public string Source { get; set; }
    public int TotalChunks { get; set; }
    public int IndexedChunks { get; set; }
    public List<string> Errors { get; set; } = new List<string>();
    public TimeSpan ProcessingTime { get; set; }
}

public class PipelineOptions
{
    public ChunkOptions ChunkOptions { get; set; } = new ChunkOptions();
    public DocumentCleanOptions CleanOptions { get; set; } = new DocumentCleanOptions();
    public bool SkipIndexing { get; set; } = false;  // 是否跳过索引
    public bool OverwriteExisting { get; set; } = false; // 是否覆盖已有文档
}
```

---

#### 4.2.6 IKnowledgeManager（知识库管理器）

```csharp
public interface IKnowledgeManager
{
    /// <summary>
    /// 初始化知识库
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// 加载知识库目录
    /// </summary>
    Task LoadKnowledgeBaseAsync(string basePath);
    
    /// <summary>
    /// 重新加载知识库
    /// </summary>
    Task ReloadKnowledgeBaseAsync();
    
    /// <summary>
    /// 添加文档
    /// </summary>
    Task AddDocumentAsync(string filePath);
    
    /// <summary>
    /// 批量添加文档
    /// </summary>
    Task AddDocumentsAsync(IEnumerable<string> filePaths);
    
    /// <summary>
    /// 删除文档
    /// </summary>
    Task RemoveDocumentAsync(string documentId);
    
    /// <summary>
    /// 更新文档
    /// </summary>
    Task UpdateDocumentAsync(string documentId, string newContent);
    
    /// <summary>
    /// 通用检索
    /// </summary>
    Task<List<RetrievedChunk>> SearchAsync(string query, SearchOptions options);
    
    /// <summary>
    /// 化工法规专用检索
    /// </summary>
    Task<List<RetrievedChunk>> SearchChemicalRegulationAsync(string query, 
                                                            string? chemicalType = null, 
                                                            string? regulationType = null,
                                                            int topK = 5);
    
    /// <summary>
    /// 获取统计信息
    /// </summary>
    Task<KnowledgeBaseStats> GetStatsAsync();
}

public class KnowledgeBaseStats
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> DocumentsByType { get; set; } = new Dictionary<string, int>();
    public DateTime LastUpdated { get; set; }
}
```

---

### 4.3 数据库表重构设计

```sql
-- 文档表（存储原始文档信息）
CREATE TABLE IF NOT EXISTS kb_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    file_name VARCHAR(500) NOT NULL,
    file_path VARCHAR(1000) NOT NULL,
    file_type VARCHAR(50) NOT NULL,
    content TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending, processed, failed
    error_message TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Chunk表（存储分块后的内容和向量）
CREATE TABLE IF NOT EXISTS kb_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES kb_documents(id),
    content TEXT NOT NULL,
    embedding vector(768),
    chunk_index INT NOT NULL,
    start_position INT NOT NULL,
    end_position INT NOT NULL,
    previous_chunk_id UUID REFERENCES kb_chunks(id),
    next_chunk_id UUID REFERENCES kb_chunks(id),
    regulation_type VARCHAR(50),
    priority VARCHAR(20),
    chemical_type VARCHAR(100),
    source_file VARCHAR(500),
    metadata JSONB,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 全文搜索索引
CREATE INDEX IF NOT EXISTS idx_kb_chunks_content_gin 
    ON kb_chunks USING gin (to_tsvector('chinese', content));

-- 向量索引（HNSW）
CREATE INDEX IF NOT EXISTS idx_kb_chunks_embedding_hnsw 
    ON kb_chunks USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 200);

-- 业务索引
CREATE INDEX IF NOT EXISTS idx_kb_chunks_document_id ON kb_chunks(document_id);
CREATE INDEX IF NOT EXISTS idx_kb_chunks_regulation_type ON kb_chunks(regulation_type);
CREATE INDEX IF NOT EXISTS idx_kb_chunks_chemical_type ON kb_chunks(chemical_type);
CREATE INDEX IF NOT EXISTS idx_kb_chunks_priority ON kb_chunks(priority);

-- 检索日志表
CREATE TABLE IF NOT EXISTS kb_search_logs (
    id SERIAL PRIMARY KEY,
    query TEXT NOT NULL,
    search_mode VARCHAR(20) NOT NULL,
    results_count INT,
    execution_time_ms INT,
    filters JSONB,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
```

---

### 4.4 依赖注入配置

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 配置
    services.AddSingleton(AppConfig.Instance);
    
    // 文档解析器
    services.AddSingleton<IDocumentParser, TxtDocumentParser>();
    services.AddSingleton<IDocumentParser, WordDocumentParser>();
    services.AddSingleton<IDocumentParser, ExcelDocumentParser>();
    services.AddSingleton<IDocumentParser, PdfDocumentParser>();
    
    // 文档处理组件
    services.AddSingleton<IDocumentCleaner, DocumentCleaner>();
    services.AddSingleton<IDocumentSplitter, DocumentSplitter>();
    services.AddSingleton<IEmbeddingService, EmbeddingService>();
    
    // 解析器工厂
    services.AddSingleton<IDocumentParserFactory, DocumentParserFactory>();
    
    // 管道和索引
    services.AddSingleton<IDocumentPipeline, DocumentPipeline>();
    services.AddSingleton<IIndexManager, IndexManager>();
    
    // 知识库服务
    services.AddSingleton<IKnowledgeManager, KnowledgeManager>();
    
    // 原有服务
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton<ILlmService, LlmService>();
    services.AddSingleton<ISessionService, SessionService>();
    services.AddSingleton<IMemoryService, MemoryService>();
    
    // 模块工厂
    services.AddSingleton<IModuleFactory, ModuleFactory>();
    services.AddSingleton<ModuleDispatcher>();
}
```

---

## 5. 实施路线图

### 5.1 阶段划分

| 阶段 | 名称 | 时间 | 目标 |
|-----|------|------|------|
| **P0** | 核心基础建设 | 2-3周 | 实现文档解析器、处理管道、统一索引管理 |
| **P1** | 功能增强 | 2周 | 增量更新、版本管理、检索优化 |
| **P2** | 企业级能力 | 3-4周 | 分布式部署、权限控制、监控告警 |

### 5.2 P0阶段详细任务

| 任务编号 | 任务描述 | 关联问题 | 状态 |
|---------|---------|---------|------|
| P0-01 | 实现文档解析器基类和Txt解析器 | Q1 | 待开发 |
| P0-02 | 实现Word文档解析器 (.doc/.docx) | Q1 | 待开发 |
| P0-03 | 实现Excel文档解析器 (.xls/.xlsx) | Q1 | 待开发 |
| P0-04 | 实现PDF文档解析器 | Q1 | 待开发 |
| P0-05 | 实现文档解析器工厂 | Q2 | 待开发 |
| P0-06 | 实现文档清洗器 | Q3 | 待开发 |
| P0-07 | 实现文档分块器 | Q3 | 待开发 |
| P0-08 | 实现统一索引管理器 | Q4 | 待开发 |
| P0-09 | 实现文档处理管道 | Q2, Q3 | 待开发 |
| P0-10 | 实现知识库管理器 | Q5 | 待开发 |
| P0-11 | 重构数据库表结构 | Q4 | 待开发 |
| P0-12 | 集成测试和验证 | 全部 | 待测试 |

### 5.3 代码迁移策略

| 原有组件 | 新组件 | 迁移策略 |
|---------|-------|---------|
| `KnowledgeBaseService` | `IndexManager` (BM25部分) | 提取BM25算法到新索引管理器 |
| `HybridKnowledgeBaseService` | `IndexManager` (混合检索) | 整合到新索引管理器 |
| `ChemicalRAG` | `KnowledgeManager` | 重构为调用新的知识库管理器 |
| `IKnowledgeBaseService` | `IKnowledgeManager` | 新接口替代旧接口 |

### 5.4 向后兼容性保障

1. **接口兼容**：保留 `IKnowledgeBaseService` 接口，实现委托到新服务
2. **数据迁移**：提供从 `chemical_documents` 到 `kb_chunks` 的数据迁移脚本
3. **配置兼容**：保持现有配置结构，逐步扩展新配置项

---

## 附录：关键设计决策总结

| 决策点 | 方案 | 理由 |
|-------|------|------|
| **文档解析** | 策略模式+工厂模式 | 易于扩展新格式，符合开闭原则 |
| **分块策略** | 按句子分割+重叠 | 保持语义完整性，提升检索精度 |
| **向量存储** | PostgreSQL + pgvector | 轻量级部署，满足当前规模需求 |
| **混合检索** | BM25(0.4) + Vector(0.6) | 关键词匹配与语义理解结合 |
| **架构风格** | 管道过滤器模式 | 各组件解耦，易于测试和替换 |
| **异常处理** | 统一日志框架 | 满足等保三级审计要求 |

---

**文档结束**

---

*本文档基于化工园区危化品合规审核AI Agent项目现状分析，旨在指导RAG架构的重构和优化工作。*
