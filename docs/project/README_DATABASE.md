
# 化工园区危化品合规审核AI Agent - 数据库配置指南

## 📋 概述

本项目使用 **PostgreSQL 16 + pgvector** 作为向量数据库，支持：
- 传统关系型数据存储（会话、审计日志等）
- 高维向量存储与检索（化工文档向量）
- BM25关键词检索 + 向量语义检索的混合检索

---

## 🗄️ 数据库表结构

### 1. sessions（会话表）
存储用户会话信息，支持会话管理和恢复。

| 字段 | 类型 | 说明 |
|------|------|------|
| id | UUID | 会话ID，主键 |
| user_id | VARCHAR(100) | 用户ID |
| user_name | VARCHAR(200) | 用户名称 |
| session_data | TEXT | 会话数据（JSON格式） |
| created_at | TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | 更新时间 |
| expires_at | TIMESTAMP | 过期时间 |

### 2. audit_logs（审计日志表）
记录用户操作审计日志，满足等保三级合规要求。

| 字段 | 类型 | 说明 |
|------|------|------|
| id | SERIAL | 日志ID，主键 |
| session_id | UUID | 会话ID，外键 |
| user_id | VARCHAR(100) | 用户ID |
| action_type | VARCHAR(50) | 操作类型 |
| action_details | TEXT | 操作详情 |
| created_at | TIMESTAMP | 创建时间 |

### 3. search_logs（搜索日志表）
记录检索请求日志，用于性能监控和优化。

| 字段 | 类型 | 说明 |
|------|------|------|
| id | SERIAL | 日志ID，主键 |
| session_id | UUID | 会话ID，外键 |
| query_text | TEXT | 查询文本 |
| search_mode | VARCHAR(20) | 检索模式（bm25/vector/hybrid） |
| num_results | INT | 返回结果数 |
| response_time_ms | INT | 响应时间（毫秒） |
| created_at | TIMESTAMP | 创建时间 |

### 4. chemical_documents（化工文档向量表）
存储化工合规文档及其向量嵌入，是向量检索的核心表。

| 字段 | 类型 | 说明 |
|------|------|------|
| id | SERIAL | 文档ID，主键 |
| content | TEXT | 文档内容 |
| embedding | vector(768) | 文档向量嵌入 |
| regulation_type | VARCHAR(50) | 法规类型 |
| priority | VARCHAR(20) | 优先级 |
| source_file | VARCHAR(200) | 来源文件 |
| chemical_type | VARCHAR(100) | 危化品类型 |
| created_at | TIMESTAMP | 创建时间 |

---

## 📊 索引设计

### 全文搜索索引（BM25）
```sql
CREATE INDEX idx_chemical_documents_content_gin 
ON chemical_documents USING gin (to_tsvector('chinese', content));
```
- 使用PostgreSQL的GIN索引
- 支持中文全文搜索
- 用于BM25关键词检索

### 向量索引（HNSW）
```sql
CREATE INDEX idx_chemical_documents_embedding_hnsw 
ON chemical_documents USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, efconstruction = 200);
```
- 使用HNSW（Hierarchical Navigable Small World）索引算法
- 使用余弦相似度（vector_cosine_ops）
- 用于快速向量相似性搜索

**HNSW参数说明：**
- `m`: 每层最大连接数（默认16），值越大索引越精确但构建越慢
- `efconstruction`: 索引构建阶段的候选点数量（默认200），值越大索引越精确但构建越慢
- `efsearch`: 搜索阶段的候选点数量（可在查询时设置，默认64）

### 业务字段索引
```sql
CREATE INDEX idx_chemical_documents_regulation_type 
ON chemical_documents (regulation_type);

CREATE INDEX idx_chemical_documents_chemical_type 
ON chemical_documents (chemical_type);
```
用于按法规类型或危化品类型过滤结果。

---

## 🚀 使用方法

### 方法1：通过代码自动创建（推荐）

运行程序时，`DatabaseService.InitializeDatabaseAsync()` 会自动执行以下操作：
1. 检查并创建vector扩展
2. 创建所有表
3. 创建所有索引

### 方法2：手动执行SQL脚本

```bash
# 连接到PostgreSQL
psql -h localhost -p 5432 -U postgres -d chemical_park_ai_agent

# 执行初始化脚本
\i d:\桌面\agent\项目\Agent1\init_database.sql
```

或者：

```bash
# 直接从命令行执行
psql -h localhost -p 5432 -U postgres -d chemical_park_ai_agent -f d:\桌面\agent\项目\Agent1\init_database.sql
```

---

## 🔧 配置说明

### AppConfig.cs 向量配置

```csharp
public class VectorSearchConfig
{
    public bool EnableVectorSearch { get; set; } = true;           // 是否启用向量检索
    public string EmbeddingModelId { get; set; } = "nomic-embed-text:latest";  // 嵌入模型
    public int EmbeddingDimension { get; set; } = 768;              // 向量维度
    public double Bm25Weight { get; set; } = 0.4;                   // BM25权重
    public double VectorWeight { get; set; } = 0.6;                 // 向量权重
    public string IndexType { get; set; } = "hnsw";                 // 索引类型
    public int HnswM { get; set; } = 16;                            // HNSW M参数
    public int HnswEfConstruction { get; set; } = 200;              // HNSW efConstruction参数
    public int HnswEfSearch { get; set; } = 64;                     // HNSW efSearch参数
}
```

### 向量维度说明

| 模型 | 维度 |
|------|------|
| nomic-embed-text | 768 |
| all-minilm | 384 |
| mxbai-embed-large | 1024 |

如果更换嵌入模型，需要：
1. 更新 `EmbeddingDimension` 配置
2. 删除并重建 `chemical_documents` 表
3. 重新向量化所有文档

---

## 📈 性能优化建议

### 1. 向量索引调优

| 场景 | m | efconstruction | efsearch |
|------|---|----------------|----------|
| 低内存，快速查询 | 8 | 100 | 32 |
| 平衡（推荐） | 16 | 200 | 64 |
| 高精度 | 32 | 400 | 128 |

### 2. 混合检索权重

| 场景 | BM25权重 | 向量权重 |
|------|----------|----------|
| 精确查询（如查特定条款） | 0.6-0.8 | 0.2-0.4 |
| 语义查询（如查"安全规定"） | 0.2-0.4 | 0.6-0.8 |
| 平衡（推荐） | 0.4 | 0.6 |

### 3. 数据库参数调优

在 `postgresql.conf` 中调整：

```ini
# 内存设置（根据实际内存调整）
shared_buffers = 4GB
effective_cache_size = 12GB
maintenance_work_mem = 1GB

# HNSW索引相关
work_mem = 64MB
```

---

## 🔍 常用查询示例

### 1. 向量相似性搜索
```sql
SELECT 
    id,
    content,
    regulation_type,
    priority,
    chemical_type,
    1 - (embedding &lt;=&gt; '[向量数据]'::vector) AS similarity
FROM chemical_documents
ORDER BY embedding &lt;=&gt; '[向量数据]'::vector
LIMIT 10;
```

### 2. BM25全文搜索
```sql
SELECT 
    id,
    content,
    regulation_type,
    priority,
    chemical_type,
    ts_rank(to_tsvector('chinese', content), plainto_tsquery('chinese', '危化品 安全')) AS rank
FROM chemical_documents
WHERE to_tsvector('chinese', content) @@ plainto_tsquery('chinese', '危化品 安全')
ORDER BY rank DESC
LIMIT 10;
```

### 3. 带业务过滤的搜索
```sql
SELECT 
    id,
    content,
    regulation_type,
    priority,
    chemical_type,
    1 - (embedding &lt;=&gt; '[向量数据]'::vector) AS similarity
FROM chemical_documents
WHERE regulation_type = 'safety'
  AND priority = 'high'
ORDER BY embedding &lt;=&gt; '[向量数据]'::vector
LIMIT 10;
```

---

## 🛡️ 安全合规说明

### 等保三级要求
1. **审计日志**：所有用户操作都记录在 `audit_logs` 表
2. **会话管理**：支持会话过期和自动清理
3. **访问控制**：通过应用层实现，数据库层只存储数据

### 数据备份
建议定期备份数据库，特别是 `chemical_documents` 表，避免重新向量化文档。

---

## 📝 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0 | 2026-05-16 | 初始版本，添加向量表和混合检索 |

