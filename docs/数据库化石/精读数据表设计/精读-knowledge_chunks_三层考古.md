# knowledge_chunks 三层精读报告

> **日期**：2026-08-12
> **方法**：数据库 L0 实测 + 代码溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：双层表架构的"分块层"——RAG 检索的内容仓库，全库数据质量最关键的 55 行

---

## 第 0 步：先立 B

知识库的"内容仓库"——用户问"苯怎么存？"，系统在这里按向量相似度找到法规原文片段，再通过 document_id 回查是哪份文件。**知识库的好坏 = 这张表的内容质量**。

- **类比**：图书馆的摘抄卡——卡片内容好，检索才有意义；卡片是水印，检索全是废纸
- **与 chemical_documents 的关系**：它是双层表的新分块层（旧扁平表的继承者），同样有 HNSW 向量索引

---

## 第 1 步：结构（字段骨架，14 字段）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'knowledge_chunks'
ORDER BY ordinal_position;
```

**实测**（2026-08-12 L0）：

| 序号 | 字段 | 类型 | 可空 | 备注 |
|------|------|------|------|------|
| 1 | id | integer | NO | SERIAL 自增 |
| 2 | document_id | integer | NO | FK → knowledge_documents，CASCADE 删除 |
| 3 | content | text | NO | 分块内容 |
| 4 | embedding | vector | YES | 768 维语义向量 |
| 5 | chunk_index | integer | NO | 同文档内块序号，默认 0 |
| 6 | chapter_number | varchar(50) | YES | "3" 或 "第3章" |
| 7 | chapter_title | varchar(500) | YES | "术语和定义" |
| 8 | clause_number | varchar(50) | YES | "3.1", "3.2.1" |
| 9 | article_number | varchar(50) | YES | "第五条"（园区规则） |
| 10 | page_number | integer | YES | 页码 |
| 11 | sub_chunk_index | integer | YES | 章节内再分块子序号 |
| 12 | regulation_type | varchar(50) | NO | 冗余字段，加速过滤 |
| 13 | priority | varchar(10) | NO | 冗余字段，加速过滤 |
| 14 | created_at | timestamptz | YES | 默认 NOW() |

**设计要点**：regulation_type / priority 是**冗余字段**（documents 层也有）——用存储换检索过滤速度；溯源五件套（chapter_number/title/clause/article/page）是双层表改造的核心价值。

---

## 第 2 步：数据（全量分组统计）

**查询 SQL**：
```sql
-- 行数 + 时间范围
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM knowledge_chunks;
-- regulation_type / priority 全量分组
SELECT regulation_type, count(*) FROM knowledge_chunks GROUP BY 1 ORDER BY 2 DESC;
SELECT priority, count(*) FROM knowledge_chunks GROUP BY 1 ORDER BY 2 DESC;
-- 每文档块数（JOIN 文档层）
SELECT kd.file_name, count(kc.id) AS 块数
FROM knowledge_documents kd LEFT JOIN knowledge_chunks kc ON kc.document_id = kd.id
GROUP BY kd.id, kd.file_name ORDER BY 块数 DESC;
```

**实测**：

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 55 | — |
| 时间范围 | 2026-07-30 11:17:27 ~ 11:17:33（**6 秒**） | 一次性批量重灌 |
| id 范围 | 1245 ~ 1299，**连续 55 个** | 7-30 清空重灌铁证（此前 1244 条被 DELETE） |
| regulation_type | 化工专业条例 38 / 国标 11 / 园区规则 5 / 历史案例 1 | 分类不一致（见落差 4） |
| priority | 高 49 / 中 5 / 低 1 | 偏科：89% 标"高" |
| 每文档块数 | 34 文档：6 个有多块，**28 个仅 1 块** | 提取异常（见落差 1） |
| 溯源完整率 | **0 / 55 = 0%** | 核心目标未达成（见落差 2） |
| embedding 空 | 0/55 | ✅ 向量全部生成 |

---

## 第 3 步：三层考古（写入路径 + 落差分析）

### 3.1 数据源写入路径全景图

```
路径 A（正常·7-30 重灌）：knowledgebase/ PDF → 提取器 → TextCleaner → SemanticChunker
   └─ InsertDocumentForFileAsync（先插 documents 拿 id）→ 逐块过 RejectGarbledChunk 闸门
      → HybridKnowledgeBaseService.AddDocumentAsync（写 content + embedding）→ knowledge_chunks
      └─ 闸门逻辑：GarbledTextDetector 三条规则（重复≥4 / 异常字符>40% / 中文<20%）
```

### 3.2 落差分析

#### 落差 1：50.9% 块是"标准分享网"水印垃圾（P1，全库最严重数据质量）

**现象**：28/55 块（28 个 GB30000.2~.29 文档的唯一一块）内容 = `标准分享网 www.bzfxw.com 免费下载+`（99 字符）。

**根因链（四环全通）**：
```
① PDF 是扫描版 → 提取器只拿到水印页（99 字符）
② SemanticChunker 对 99 字符只切 1 块（chunk_index=0）
③ GarbledTextDetector 规则盲区：中文占比 37.5% > 20% 阈值 → 规则③放行
   （"标准分享网"+"免费下载"9 个汉字 / 24 总字符；异常字符仅 4% → 规则②放行；无连续重复 → 规则①放行）
④ RejectGarbledChunk 闸门（ChemicalRAG.cs L822）未拦截 → 入库
```

**影响**：检索"苯怎么存"可能命中"标准分享网免费下载"；合规回答引用的是水印文本。**RAG 数据底座一半是垃圾**。

**修复方向**（待讨论）：水印文本黑名单 / GarbledTextDetector 增加"有效内容长度"规则（<200 字符且无标点结构拒收）/ 28 个扫描版 PDF 走 OCR 重灌。

#### 落差 2：溯源完整率 0%——双层表改造的核心目标未落地（P2）

**现象**：55 块中无一块同时具备 chapter_title + clause_number + page_number；有效 27 块中也仅 11 有章节、16 有条款、10 有页码。

**源头**：SemanticChunker 的章节/条款解析依赖文本结构（"第X条"、"X.X" 模式），大部分提取文本无清晰结构 → 字段留空；写入时 `c.ChapterTitle ?? ""`（ChemicalRAG.cs L728 等）。

**影响**：蓝图 V 清单第 3 条"BM25 metadata 含 RegulationNumber"只在 documents 层达成，chunks 层溯源能力空转——**双层表 7 个核心价值中"溯源"一环名存实亡**。

#### 落差 3：蓝图承诺的 UNIQUE(file_hash, chunk_index) 未实现（P3）

**现象**：commit 72271fae 声称"联合唯一约束 UNIQUE(file_hash, chunk_index) 防止重复写入"，实测约束仅 `knowledge_chunks_pkey` + `document_id_fkey`（init_database.sql L126-140 无 file_hash 列）。

**分析**：file_hash 属于 documents 层（content_hash 列），跨表无法建唯一约束——实现改为 `source_path UNIQUE`（文档级幂等），**分块级幂等缺失**：同文件重灌会在 chunks 产生重复行（无冲突处理）。

#### 落差 4：分类修正只做了一半（P2）

**现象**：GB 30000 系列 28 个分册 = "化工专业条例"，但 GB30000-2013 总纲 + GB15603-2022 = "国标"。

**源头**：蓝图 P5 的"国标→化工专业条例"只应用于 `spec/` 目录文件；分类是"目录→类型"静态映射，不是内容识别。

**影响**：检索过滤 `WHERE regulation_type = '化工专业条例'` 会漏掉总纲和 GB15603——同标准家族两套标签。

#### 落差 5：id 1245-1299 连续 = 7-30 清空重灌地层证据（非问题，历史事实）

55 个 id 连续分配，说明 7-30 11:17 前 chunks 表被整体 DELETE（1244 条）后重新灌入。与 documents 表（id 1292-1325，34 条）同步。**知识库经历过 7-17 迁移 → 7-30 重灌 → 8-11 整库替换 三代地层**。

---

## 第 4 步：一句话总结

> **knowledge_chunks 是双层表的"内容仓库"：14 字段、HNSW 向量检索、55 块全量 7-30 重灌——但 50.9% 块是 PDF 提取失败漏网的水印垃圾，溯源完整率 0%，防重复约束未实现，同标准家族分类不一致——知识库的地基有半数是沙子，这是全库精读至今最严重的数据质量问题。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 元数据：字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'knowledge_chunks'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围 ──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM knowledge_chunks;

-- ── [3] 全量分组统计 ──
SELECT regulation_type, count(*) AS 行数 FROM knowledge_chunks GROUP BY 1 ORDER BY 2 DESC;
SELECT priority, count(*) AS 行数 FROM knowledge_chunks GROUP BY 1 ORDER BY 2 DESC;

-- ── [4] 每文档块数分布（找"1 块文档"= 提取异常信号）──
SELECT kd.id AS doc_id, kd.file_name, kd.regulation_type, count(kc.id) AS 块数
FROM knowledge_documents kd
LEFT JOIN knowledge_chunks kc ON kc.document_id = kd.id
GROUP BY kd.id, kd.file_name, kd.regulation_type
ORDER BY 块数 DESC;

-- ── [5] 水印垃圾规模（真相 1 验证）──
SELECT count(*) FILTER (WHERE content LIKE '%标准分享网%') AS 水印块,
       count(*) AS 总行数,
       round(100.0 * count(*) FILTER (WHERE content LIKE '%标准分享网%') / count(*), 1) AS 水印占比pct
FROM knowledge_chunks;

-- ── [6] 溯源完整率（真相 2 验证）──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE chapter_title IS NOT NULL AND chapter_title<>'') AS 章节非空,
       count(*) FILTER (WHERE clause_number IS NOT NULL AND clause_number<>'') AS 条款非空,
       count(*) FILTER (WHERE page_number IS NOT NULL) AS 页码非空
FROM knowledge_chunks;

-- ── [7] 约束 + 索引 + 序列 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='knowledge_chunks'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='knowledge_chunks' ORDER BY indexname;
SELECT last_value FROM pg_sequences
WHERE schemaname='public' AND sequencename='knowledge_chunks_id_seq';

-- ── [8] 待核销：水印文档的 extraction_quality 标记是否失真 ──
SELECT kd.extraction_quality, count(*) AS 文档数
FROM knowledge_documents kd
JOIN knowledge_chunks kc ON kc.document_id = kd.id
WHERE kc.content LIKE '%标准分享网%'
GROUP BY kd.extraction_quality;
```
