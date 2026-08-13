# knowledge_documents 三层精读报告

> **日期**：2026-08-12
> **方法**：数据库 L0 实测 + 代码溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：双层表架构的"文档层"——知识库馆藏目录，chunks 精读的配套（故事线 A 第 2 站）

---

## 第 0 步：先立 B

知识库的"馆藏目录"——每个 PDF/DOCX 文件一行：法规编号、标题、优先级、提取质量、页数、哈希。回答"**库里有什么**"；chunks 回答"内容是什么"。

- **类比**：图书馆馆藏卡片（书名/作者/编号/状态）vs 摘抄卡（chunks）
- **与 knowledge_chunks 的关系**：1 ──── N（document_id 外键，CASCADE 删除）；检索时 JOIN 两表返回 13 字段完整元数据

---

## 第 1 步：结构（字段骨架，17 字段）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'knowledge_documents'
ORDER BY ordinal_position;
```

**实测**（2026-08-12 L0）：

| 序号 | 字段 | 类型 | 可空 | 备注 |
|------|------|------|------|------|
| 1 | id | integer | NO | SERIAL 自增 |
| 2 | source_path | varchar(500) | NO | **UNIQUE**，相对路径（删除键=插入键） |
| 3 | file_name | varchar(300) | NO | 展示名 |
| 4 | file_format | varchar(10) | NO | pdf/doc/docx/txt |
| 5 | file_size_bytes | bigint | YES | 文件大小 |
| 6 | regulation_type | varchar(50) | NO | 国标/园区规则/历史案例/企业制度/化工专业条例 |
| 7 | regulation_number | varchar(100) | YES | 如 "GB 30000.7-2013" |
| 8 | regulation_title | varchar(500) | YES | ⚠️ 实测 100% 空 |
| 9 | priority | varchar(10) | NO | 高/中/低，默认中 |
| 10 | parent_category | varchar(200) | YES | H166 层级路径，⚠️ 实测 100% 空 |
| 11 | extraction_quality | varchar(20) | YES | good/partial/failed，默认 good |
| 12 | page_count | integer | YES | 页数 |
| 13 | is_full_text | boolean | YES | false=仅摘要入库 |
| 14 | total_chunks | integer | YES | 块数，⚠️ 语义漂移（切块数≠入库数） |
| 15 | content_hash | varchar(64) | YES | SHA-256 增量更新检测，⚠️ 实测 100% 空 |
| 16 | last_modified | timestamptz | YES | 文件修改时间（已写入 ✅） |
| 17 | created_at | timestamptz | YES | 默认 NOW() |

**设计要点**：source_path UNIQUE = 文档级幂等（蓝图承诺的 chunk 级 UNIQUE(file_hash, chunk_index) 未实现，以此替代）；content_hash 是增量更新机制的核心依赖。

---

## 第 2 步：数据（全量分组统计）

**查询 SQL**：
```sql
-- 行数 + 时间范围
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM knowledge_documents;
-- 四维全量分组
SELECT regulation_type, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT priority, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT extraction_quality, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT file_format, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
-- total_chunks 一致性验证
SELECT kd.id, kd.file_name, kd.total_chunks AS 字段值, count(kc.id) AS 实际块数,
       kd.total_chunks - count(kc.id) AS 差值
FROM knowledge_documents kd LEFT JOIN knowledge_chunks kc ON kc.document_id = kd.id
GROUP BY kd.id, kd.file_name, kd.total_chunks
ORDER BY abs(kd.total_chunks - count(kc.id)) DESC;
-- 目录 x 类型 交叉（分类源头）
SELECT split_part(source_path, '/', 1) AS 一级目录, regulation_type, count(*) AS 文档数
FROM knowledge_documents GROUP BY 1, 2 ORDER BY 1;
```

**实测**：

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 34 | — |
| 时间范围 | 2026-07-30 11:17:26 ~ 11:17:33（7 秒） | 与 chunks 同批清空重灌 |
| id 范围 | 1292 ~ 1325 连续 | 此前 1291 条被 DELETE（7-30 重灌铁证） |
| regulation_type | 化工专业条例 30 / 国标 2 / 园区规则 1 / 历史案例 1 | 分类物理摆放问题 |
| priority | 高 32 / 中 1 / 低 1 | 94% 标"高"，区分度低 |
| extraction_quality | **partial 29 / good 5** | 28 个水印文档全部 partial |
| file_format | pdf 30 / txt 4 | — |
| total_chunks 一致性 | **1/34 不一致：30871-2022 字段 22 vs 实际 1** | 21 块被乱码闸门拒收，计数含被拒块 |
| content_hash | **34/34 空** | 增量更新检测失效 |
| regulation_title / parent_category | **34/34 空** | 幽灵字段 |
| last_modified / file_size | 0 空 | ✅ 正常写入 |

---

## 第 3 步：三层考古（写入路径 + 落差分析）

### 3.1 数据源写入路径全景图

```
路径 A（正常·7-30 重灌）：LoadKnowledgeBaseAsync → 目录枚举（EnumerateSupportedFiles）
   └─ 硬编码目录→类型映射（ChemicalRAG.cs L280-283）：
      国标/ → 国标 · 化工专业条例/化工专业条例/ → 化工专业条例
      园区规则/ → 园区规则 · 历史案例/ → 历史案例
   └─ 每文件：提取 → TextCleaner → SemanticChunker → InsertDocumentForFileAsync（先插 documents）
      → 逐块过 RejectGarbledChunk 闸门 → AddDocumentAsync（写 chunks）
      → UpdateDocumentChunkCountAsync(documentId, chunks.Count)  ← total_chunks 写入点
```

### 3.2 落差分析

#### 落差 1：#10 根因链补上最后一环——"知道失败，仍入库"（P1 关联）

**现象**：28 个水印文档的 extraction_quality **全部 = partial**，page_count 平均 19.3 页（28 文档 ≈ 540 页法规内容），但入库的只有 99 字符水印。

**真相**：提取器**知道**提取不完整（标记 partial），也知道页数（19 页），但 partial 标记**没有触发任何拒收/OCR 重灌动作**——水印照样入库。**约 540 页 GB 30000 法规内容实际丢失**，只留下 28 条水印作为"存在过的证据"。

**修复方向**：extraction_quality = partial 且文本极短（<200 字符）时拒收 + 标记"待 OCR"；或入库前对 partial 文档做 OCR 增强。

#### 落差 2：content_hash 100% 空——增量更新检测从出生起失效（P2）

**现象**：34/34 空。**源头**：[InsertDocumentForFileAsync L415-430](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Dialog/ChemicalRAG.cs#L415) 构造 KnowledgeDocumentRecord 时**未给 ContentHash 赋值**——last_modified、file_size 都写了，唯独哈希没算。

**影响**：init_database.sql L119 注释明说"content_hash 用于增量更新检测"，机制从未生效：文件内容变化无法被哈希检测，增量更新只能依赖 last_modified（可能漏判内容改动）。

#### 落差 3：#13 分类不一致根因钉死——物理摆放，不是代码 bug（P2 关联）

**现象**：目录×类型交叉 **1:1 完全一致**（国标目录→国标 2 个、化工专业条例目录→化工专业条例 30 个）。

**真相**：代码映射（L176-178、L280-283）忠实执行。**问题在知识库文件物理摆放**：28 个 GB 30000 分册（国家标准）被放进"化工专业条例"目录，而 GB15603 + GB30000 总纲在"国标"目录——**同一标准家族被物理拆到两个目录**。

**修复方向**：调整知识库物理目录（28 个 GB 30000 分册移入"国标/"）后重灌；或改映射逻辑。

#### 落差 4：total_chunks 语义漂移（P3）

**现象**：30871-2022 字段值 22 vs 实际 1 块。**源头**：`UpdateDocumentChunkCountAsync(documentId, chunks.Count)` 写入的是**切块数**（22），而 RejectGarbledChunk 拒收了 21 块乱码（拒收是正确行为！），实际入库 1 块。

**影响**：total_chunks 失去"该文档可检索块数"语义；前端展示、统计失真。（注：30871 的 21 块被拒收说明闸门在乱码场景**有效**——与 #10 水印放行形成对照：闸门拦"乱码"，不拦"有意义的水印"。）

#### 落差 5：regulation_title / parent_category 幽灵字段（P3）

**现象**：34/34 空。**源头**：调用点（L715-717 等）从未传 regulationTitle / parentCategory 参数。

**影响**：法规标题需从 file_name 推断（"GB 30000.7-2013 化学品分类和标签规范 第7部分：易燃液体"这种长名靠文件名）；H166 层级路径（parent_category）无法追溯文档归属类别。

---

## 第 4 步：一句话总结

> **knowledge_documents 是双层表的"馆藏目录"：17 字段、source_path 幂等、34 个文档 7-30 重灌——但 content_hash 从未写入（增量更新失效）、regulation_title/parent_category 全空（幽灵字段）、total_chunks 统计的是切块数非入库数，而 #10 水印问题的最后一环在此钉死：28 个文档被标记 partial 却照样入库，约 540 页法规内容实际丢失——目录层记录了"存在"，却没记录"可用"。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'knowledge_documents'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围 ──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM knowledge_documents;

-- ── [3] 全量分组统计（类型/优先级/质量/格式）──
SELECT regulation_type, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT priority, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT extraction_quality, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;
SELECT file_format, count(*) FROM knowledge_documents GROUP BY 1 ORDER BY 2 DESC;

-- ── [4] total_chunks 一致性验证 ──
SELECT kd.id, kd.file_name, kd.total_chunks AS 字段值, count(kc.id) AS 实际块数,
       kd.total_chunks - count(kc.id) AS 差值
FROM knowledge_documents kd LEFT JOIN knowledge_chunks kc ON kc.document_id = kd.id
GROUP BY kd.id, kd.file_name, kd.total_chunks
ORDER BY abs(kd.total_chunks - count(kc.id)) DESC;

-- ── [5] 目录 x 类型 交叉（#13 源头）──
SELECT split_part(source_path, '/', 1) AS 一级目录, regulation_type, count(*) AS 文档数
FROM knowledge_documents GROUP BY 1, 2 ORDER BY 1;

-- ── [6] 幽灵字段全量排查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE content_hash IS NULL OR content_hash='') AS content_hash空,
       count(*) FILTER (WHERE regulation_title IS NULL OR regulation_title='') AS 标题空,
       count(*) FILTER (WHERE parent_category IS NULL OR parent_category='') AS 父目录空,
       count(*) FILTER (WHERE regulation_number IS NULL OR regulation_number='') AS 编号空
FROM knowledge_documents;

-- ── [7] 约束 + 索引 + 序列 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='knowledge_documents'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='knowledge_documents' ORDER BY indexname;
SELECT last_value FROM pg_sequences
WHERE schemaname='public' AND sequencename='knowledge_documents_id_seq';

-- ── [8] 水印文档质量标记（#10 证据）──
SELECT kd.extraction_quality, count(*) AS 文档数
FROM knowledge_documents kd
JOIN knowledge_chunks kc ON kc.document_id = kd.id
WHERE kc.content LIKE '%标准分享网%'
GROUP BY kd.extraction_quality;

-- ── [9] 水印 vs 正常文档对比（提取失败痕迹）──
SELECT (CASE WHEN EXISTS (SELECT 1 FROM knowledge_chunks kc WHERE kc.document_id = kd.id
                          AND kc.content LIKE '%标准分享网%') THEN '水印文档' ELSE '正常文档' END) AS 类别,
       count(*) AS 文档数, count(*) FILTER (WHERE page_count IS NULL) AS 页数空,
       round(avg(COALESCE(page_count, 0)), 1) AS 平均页数
FROM knowledge_documents kd GROUP BY 1;
```
