# chemical_documents 三层精读报告

> **日期**：2026-08-12
> **方法**：数据库 L0 实测 + 代码溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：化工文档向量表（旧版），RAG 检索的数据底座

---

## 第 0 步：先立 B

每次用户问化工合规问题（"苯怎么存？"），系统从这张表里按**语义相似度**找到最相关的法规条款。

- **类比**：法规条文碎片盒——把 PDF 切成小块，每块附上语义向量，AI 用"找相似"的方式精准抽取
- **现状**：被双层表架构（knowledge_documents + knowledge_chunks）取代，当前作为**兜底表**存在

---

## 第 1 步：结构（字段骨架）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_documents'
ORDER BY ordinal_position;
```

**14 字段 → 5 组**：

| 序号 | 字段 | 类型 | 可空 | 分组 | 回答的问题 |
|------|------|------|------|------|-----------|
| 1 | id | integer | **NO** | 身份组 | 第几条记录 |
| 2 | content | text | **NO** | 内容组 | 法规条文内容 |
| 3 | embedding | vector(768) | YES | 检索组 | 语义向量（HNSW 索引） |
| 4 | regulation_type | varchar(50) | **NO** | 分类组 | 国标/条例/规则 |
| 5 | priority | varchar(20) | **NO** | 分类组 | 高/中/低 |
| 6 | source_file | varchar(200) | YES | 溯源组 | 来自哪个文件 |
| 7 | chemical_type | varchar(100) | YES | 分类组 | 危化品类型 |
| 8 | created_at | timestamptz | YES | 状态组 | 入库时间 |
| 9 | regulation_number | varchar(100) | YES | 溯源组 | 法规号（如 GB 15603-2022） |
| 10 | chapter_title | varchar(200) | YES | 溯源组 | 章节标题 |
| 11 | clause_number | varchar(50) | YES | 溯源组 | 条款号 |
| 12 | page_number | integer | YES | 溯源组 | 页码 |
| 13 | chunk_index | integer | YES | 溯源组 | 分块序号 |
| 14 | extraction_quality | varchar(20) | YES | 质量组 | OCR 质量等级 |

**骨架故事**：
- 核心三件套：content（文本）+ embedding（向量）+ regulation_type（分类）→ 支撑 RAG 检索
- 溯源五件套：source_file + regulation_number + chapter_title + clause_number + page_number → 合规回答必须"有出处"
- 质量组 extraction_quality：脏数据熔断（OCR 质量太差不入库）

**约束 + 索引**（设计决策证据）：
- 约束：只有主键（无外键、无唯一约束）
- 索引 6 个：
  - **idx_chemical_documents_embedding_hnsw**（HNSW 向量索引，m=16, ef_construction=200）→ 语义检索核心
  - **idx_chemical_documents_content_gin**（GIN 全文索引，to_tsvector('simple')）→ BM25 关键词检索
  - idx_chemical_documents_regulation_type / chemical_type / created_at → 分类过滤
- 序列：chemical_documents_id_seq last_value = **1007**

---

## 第 2 步：数据（真实行对照）

### 2.1 全量数据

```sql
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM chemical_documents;
```

**结果**：**0 行**。

### 2.2 全量分组统计

```sql
-- 按 regulation_type / priority / chemical_type 分组
SELECT regulation_type, count(*) AS 行数 FROM chemical_documents GROUP BY regulation_type;
SELECT priority, count(*) AS 行数 FROM chemical_documents GROUP BY priority;
SELECT chemical_type, count(*) AS 行数 FROM chemical_documents GROUP BY chemical_type;
```

**结果**：全部分组 0 行。

### 2.3 空值全量排查

```sql
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE content IS NULL) AS content空,
       count(*) FILTER (WHERE embedding IS NULL) AS embedding空,
       count(*) FILTER (WHERE source_file IS NULL) AS source_file空,
       count(*) FILTER (WHERE chemical_type IS NULL) AS chemical_type空
FROM chemical_documents;
```

**结果**：全 0（表空，无空值可统计）。

### 2.4 序列异常信号

序列 last_value = **1007**，但实际行数 = **0**。

**解读**：历史上曾写入约 1007 条（或测试灌入），后来被全部 DELETE/CLEAR，但序列不会回退 → 序列是"写入次数"的化石，不是"当前行数"。

---

## 第 3 步：三层考古（决策 → 现象 → 数据源）

### 3.1 逐字段三层对照

| 字段 | 决策设计（为什么） | 数据现象（L0 实测） | 数据源脉络（代码证据） |
|------|------------------|-------------------|----------------------|
| `id` | SERIAL 自增 | 序列 1007，行数 0 | DatabaseService.cs#L177 建表 |
| `content` | text 必填 = 条文内容是核心 | 0 行 | AddChemicalDocumentAsync 写入 |
| `embedding` | vector(768) 可空 | 0 行 | HybridKnowledgeBaseService 批量嵌入后写入 |
| `regulation_type` | varchar(50) 必填 = 分类检索 | 0 行 | 默认"通用" |
| `priority` | varchar(20) 必填 = 优先级排序 | 0 行 | 默认"中" |
| `source_file` | varchar(200) 可空 = 出处溯源 | 0 行 | |
| `chemical_type` | varchar(100) 可空 = 物质分类 | 0 行 | |
| `regulation_number` | varchar(100) 可空 = 法规号 | 0 行 | DatabaseService.cs#L186 扩展字段 ALTER TABLE |
| `extraction_quality` | varchar(20) 可空 = 质量标签 | 0 行 | DatabaseService.cs#L189 扩展字段 |
| 6 个索引 | HNSW + GIN + 3 个业务索引 | 全在 | init_database.sql#L82-94 |

### 3.2 数据源写入路径全景图

```
路径 A（正常·历史）：HybridKnowledgeBaseService → AddChemicalDocumentsBatchAsync → chemical_documents
   └─ LLM 批量嵌入 → 向量入库 → 6 索引加速
   └─ 调用点：知识库灌入（knowledgebase/ PDF → 切块 → 嵌入 → 写入）

路径 B（测试）：DatabaseIntegrationTests.cs → AddChemicalDocumentAsync → chemical_documents
   └─ 并发测试：10 个并发写入

路径 C（已废弃）：化学文档被双层表（knowledge_documents + knowledge_chunks）取代
   └─ GetAllChemicalDocumentTextsAsync 优先查双层表，chemical_documents 仅作兜底
```

### 3.3 落差分析

#### 落差 1：表空但序列到 1007（历史写入化石）

**现象**：0 行，序列 1007。**源头**：历史上曾灌入约 1000 条数据（测试或真实灌入），后来被 `ClearChemicalDocumentsAsync()` 清空，但 SERIAL 序列不回退。**这不是 bug，是 SERIAL 的正常行为**。

#### 落差 2：0 行但有 6 个索引（资源浪费？）

**现象**：表空但维护着 6 个索引（含 HNSW、GIN 重型索引）。**源头**：建表时预设了完整索引，但当前数据已迁移到双层表。索引占用空间但无数据可检索 → **潜在资源浪费**，但因为是空表，实际开销极小。

#### 落差 3：作为兜底表但可能永远不会被用到

**现象**：GetAllChemicalDocumentTextsAsync 的兜底逻辑（[DatabaseService.cs#L1042-1059](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/DatabaseService.cs#L1042)）只有在双层表 0 行时才触发，但双层表已有 34 文档/55 分块 → **兜底路径几乎不会被执行**。

---

## 第 4 步：一句话总结

> **chemical_documents 是"旧版 RAG 文档表"：14 字段支撑 content + embedding + 溯源五件套，6 索引（HNSW+GIN）为语义检索而设——但当前 0 行（被双层表取代），序列 1007 是历史写入化石，兜底路径几乎不会被执行。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 元数据：字段结构（14 字段骨架）──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_documents'
ORDER BY ordinal_position;

-- ── [2] 全量数据：行数 + 时间范围 ──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM chemical_documents;

-- ── [3] 全量分组统计 ──
SELECT regulation_type, count(*) AS 行数 FROM chemical_documents GROUP BY regulation_type;
SELECT priority, count(*) AS 行数 FROM chemical_documents GROUP BY priority;
SELECT chemical_type, count(*) AS 行数 FROM chemical_documents GROUP BY chemical_type;

-- ── [4] 空值全量排查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE content IS NULL OR content = '') AS content空,
       count(*) FILTER (WHERE embedding IS NULL) AS embedding空,
       count(*) FILTER (WHERE source_file IS NULL OR source_file = '') AS source_file空,
       count(*) FILTER (WHERE chemical_type IS NULL OR chemical_type = '') AS chemical_type空
FROM chemical_documents;

-- ── [5] 真实数据样例 3 条 ──
SELECT id, left(content,80) AS content,
       regulation_type, priority, source_file, chemical_type,
       left(embedding::text,50) AS embedding, created_at::text
FROM chemical_documents ORDER BY id LIMIT 3;

-- ── [6] 约束 + 索引 + 序列 ──
SELECT conname, contype, pg_get_constraintdef(oid) AS 约束定义
FROM pg_constraint
WHERE connamespace = 'public'::regnamespace AND conrelid = 'chemical_documents'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'chemical_documents' ORDER BY indexname;
SELECT last_value FROM pg_sequences
WHERE schemaname = 'public' AND sequencename = 'chemical_documents_id_seq';
```
