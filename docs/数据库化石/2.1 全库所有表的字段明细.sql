-- 【2.1 核心表字段明细（8 张项目表，一张大表）】学习主用版
-- 2026-08-11 优化：默认过滤 agent_* 外来空壳表，聚焦 8 张核心表
-- 输出解读：
--   column_name → 字段名
--   data_type   → 类型（character varying=字符串, integer=整数,
--                 USER-DEFINED 且在 udt_name 看到 vector = 向量列）
--   max_len     → 字符串最大长度（NULL 表示不限长，如 TEXT）
--   nullable    → YES=可空, NO=必填
--   default     → 默认值（如 nextval('..._id_seq'::regclass) = 自增）
-- 学习要点：这是了解字段含义的第一入口，逐表过一遍。
--           白名单 IN(...) 不依赖命名约定，比 NOT LIKE 'agent\_%' 更稳
SELECT table_name,
       ordinal_position               AS 序号,
       column_name                    AS 字段名,
       data_type                      AS 类型,
       COALESCE(character_maximum_length::text, '') AS 最大长度,
       is_nullable                    AS 可空,
       COALESCE(column_default, '')   AS 默认值,
       COALESCE(udt_name, '')         AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name IN ('sessions', 'refresh_tokens', 'audit_logs',
                     'search_logs', 'chemical_documents',
                     'knowledge_documents', 'knowledge_chunks',
                     'long_term_memories')
ORDER BY table_name, ordinal_position;
-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------
-- ── [1] 元数据：字段结构（8 字段骨架）──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'audit_logs'
ORDER BY ordinal_position;

-- ── [2] 真实数据：最新 5 条 ──
SELECT id, user_id, action, module, left(detail,60) AS detail前60字,
       ip_address, created_at::text AS created_at, left(chain_hash,64) AS chain_hash
FROM audit_logs ORDER BY id DESC LIMIT 5;

-- ── [3] 全量分组统计 ──
SELECT min(created_at)::text AS 最早, max(created_at)::text AS 最晚 FROM audit_logs;
SELECT action, count(*) AS 行数 FROM audit_logs GROUP BY action ORDER BY 行数 DESC;
SELECT COALESCE(NULLIF(user_id,''),'(空)') AS 用户, count(*) AS 行数
FROM audit_logs GROUP BY 1 ORDER BY 行数 DESC;

-- ── [4] 真相验证：module 全空 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE module IS NULL OR module = '') AS module为空,
       count(*) FILTER (WHERE module IS NOT NULL AND module <> '') AS module有值
FROM audit_logs;

-- ── [5] 真相验证：无哈希行数 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE chain_hash IS NULL OR chain_hash = '') AS 无哈希,
       count(*) FILTER (WHERE chain_hash IS NOT NULL AND chain_hash <> '') AS 有哈希
FROM audit_logs;

-- ── [6] 哈希长度全量分组统计（SHA256=64位）──
SELECT length(chain_hash) AS 哈希长度, count(*) AS 行数
FROM audit_logs WHERE chain_hash IS NOT NULL AND chain_hash <> '' GROUP BY 1;

-- ── [7] ip_address 空值统计 ──
SELECT count(*) FILTER (WHERE ip_address IS NULL OR ip_address='') AS ip为空,
       count(*) FILTER (WHERE ip_address IS NOT NULL AND ip_address<>'') AS ip有值,
       count(*) AS 总行数 FROM audit_logs;

-- ── [8] 约束/索引/序列（设计决策证据）──
SELECT conname, contype, pg_get_constraintdef(oid) AS 约束定义
FROM pg_constraint
WHERE connamespace = 'public'::regnamespace AND conrelid = 'audit_logs'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'audit_logs' ORDER BY indexname;
SELECT last_value FROM pg_sequences
WHERE schemaname = 'public' AND sequencename = 'audit_logs_id_seq';



-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

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

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

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


-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

-- ── [1] 字段结构（含 vector 维度）──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'long_term_memories'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围 ──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM long_term_memories;

-- ── [3] 核心健康度三查（活跃率/重复率/零命中率）──
SELECT is_active, count(*) FROM long_term_memories GROUP BY 1;  -- 预期活跃率 > 50%
SELECT count(*) AS 总行数, count(DISTINCT content) AS 去重内容数,
       count(*) - count(DISTINCT content) AS 重复条数
FROM long_term_memories;  -- 预期重复 = 0
SELECT count(*) FILTER (WHERE hit_count = 0) AS 零命中,
       round(avg(hit_count), 2) AS 平均命中 FROM long_term_memories;

-- ── [4] 停用分布（批量停用 vs 逐日停用）──
SELECT updated_at::date AS 停用日, count(*) FROM long_term_memories
WHERE is_active = false GROUP BY 1 ORDER BY 2 DESC;

-- ── [5] 活跃记忆创建日分布（空心化证据）──
SELECT created_at::date AS 创建日, count(*) FROM long_term_memories
WHERE is_active = true GROUP BY 1 ORDER BY 1;

-- ── [6] 重复 TOP5 ──
SELECT user_id, memory_type, content, count(*) AS 重复数
FROM long_term_memories GROUP BY user_id, memory_type, content
HAVING count(*) > 1 ORDER BY 重复数 DESC LIMIT 5;

-- ── [7] sessions 孤儿引用验证 ──
SELECT count(DISTINCT lt.source_session_id) AS 记忆引用的会话数,
       count(DISTINCT s.id) AS sessions表实际会话数,
       count(*) FILTER (WHERE lt.source_session_id IS NOT NULL AND s.id IS NULL) AS 孤儿数
FROM long_term_memories lt LEFT JOIN sessions s ON s.id = lt.source_session_id;

-- ── [8] 约束 + 索引（HNSW 向量索引确认）──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='long_term_memories'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='long_term_memories' ORDER BY indexname;


-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------


-- ── [1] 字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_substances'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围（判断是否单事务导入）──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM chemical_substances;

-- ── [3] 物理状态分布（枚举统一性检查）──
SELECT physical_state, count(*) FROM chemical_substances GROUP BY 1 ORDER BY 2 DESC;

-- ── [4] 空值全量排查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE cas_number IS NULL OR cas_number='') AS cas空,
       count(*) FILTER (WHERE flash_point_c IS NULL) AS 闪点空,
       count(*) FILTER (WHERE boiling_point_c IS NULL) AS 沸点空
FROM chemical_substances;

-- ── [5] 约束 + 索引 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='chemical_substances'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='chemical_substances' ORDER BY indexname;

-- ── [6] 别名健康度（孤儿/歧义）──
SELECT count(*) FROM chemical_aliases a
LEFT JOIN chemical_substances s ON s.id = a.substance_id WHERE s.id IS NULL;  -- 预期 0
SELECT alias_text, count(DISTINCT substance_id) FROM chemical_aliases
GROUP BY alias_text HAVING count(DISTINCT substance_id) > 1;  -- 预期 0 行

-- ── [7] 子表孤儿验证 ──
SELECT 'hazard_categories' AS 表, count(*) AS 孤儿数
FROM chemical_hazard_categories hc LEFT JOIN chemical_substances s ON s.id = hc.substance_id
WHERE s.id IS NULL
UNION ALL
SELECT 'incompatible_categories', count(*)
FROM chemical_incompatible_categories ic LEFT JOIN chemical_substances s ON s.id = ic.substance_id
WHERE s.id IS NULL;

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

-- ── [1] 字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_aliases'
ORDER BY ordinal_position;

-- ── [2] 约束 + 索引 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='chemical_aliases'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='chemical_aliases' ORDER BY indexname;

-- ── [3] 别名覆盖（无别名物质）──
SELECT s.id, s.name FROM chemical_substances s
LEFT JOIN chemical_aliases a ON a.substance_id = s.id
WHERE a.id IS NULL;  -- 预期 0 行

-- ── [4] 自指别名检查 ──
SELECT a.alias_text FROM chemical_aliases a
JOIN chemical_substances s ON s.id = a.substance_id
WHERE a.alias_text = s.name;  -- 预期 0 行

-- ── [5] 别名质量（空格/长度）──
SELECT count(*) FILTER (WHERE alias_text <> btrim(alias_text)) AS 含空格,
       count(*) FILTER (WHERE length(alias_text) < 2) AS 超短
FROM chemical_aliases;

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

-- ── 反向重复（A->B 与 B->A 双写）──
SELECT sa.name, sb.name FROM chemical_incompatibilities i
JOIN chemical_incompatibilities r ON r.substance_a_id = i.substance_b_id AND r.substance_b_id = i.substance_a_id
JOIN chemical_substances sa ON sa.id = i.substance_a_id
JOIN chemical_substances sb ON sb.id = i.substance_b_id
WHERE i.id < r.id;

-- ── 死规则验证（incompatible_with 与 GHS 术语匹配度）──
WITH cats AS (SELECT DISTINCT category FROM chemical_hazard_categories)
SELECT t.incompatible_with, count(*) AS 使用次数
FROM (SELECT ic.incompatible_with FROM chemical_incompatible_categories ic GROUP BY ic.incompatible_with) t
WHERE NOT EXISTS (SELECT 1 FROM cats c WHERE c.category LIKE '%'||t.incompatible_with||'%' OR t.incompatible_with LIKE '%'||c.category||'%')
GROUP BY t.incompatible_with ORDER BY 使用次数 DESC;

-- ── 无 regulation_ref 的规则 ──
SELECT sa.name, sb.name FROM chemical_incompatibilities i
JOIN chemical_substances sa ON sa.id = i.substance_a_id
JOIN chemical_substances sb ON sb.id = i.substance_b_id
WHERE i.regulation_ref IS NULL OR i.regulation_ref = '';

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

-- ── 全量清单 ──
SELECT id, facility_pair, min_distance_m, regulation_ref
FROM chemical_safety_distances ORDER BY id;

-- ── 质量检查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE facility_pair IS NULL OR facility_pair='') AS pair空,
       count(*) FILTER (WHERE min_distance_m IS NULL OR min_distance_m < 0) AS 距离异常,
       count(*) FILTER (WHERE regulation_ref IS NULL OR regulation_ref='') AS 法规空
FROM chemical_safety_distances;

-- ── 泛化/特化并存检查（子串遮蔽候选）──
SELECT a.facility_pair AS 泛化, b.facility_pair AS 特化
FROM chemical_safety_distances a
JOIN chemical_safety_distances b
  ON b.facility_pair LIKE '%' || a.facility_pair || '%' AND a.facility_pair <> b.facility_pair;
-- 预期：>0 即存在遮蔽风险（泛化条目会先命中 contains）

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------


-- ── 无类别标注的物质 ──
SELECT s.name FROM chemical_substances s
LEFT JOIN chemical_hazard_categories hc ON hc.substance_id = s.id
WHERE hc.id IS NULL;

-- ── gb_standard 一致性 ──
SELECT category, count(DISTINCT gb_standard) AS 不同GB数
FROM chemical_hazard_categories
GROUP BY category HAVING count(DISTINCT gb_standard) > 1;

-- ── 字段利用（幽灵字段检查）──
SELECT count(*) FILTER (WHERE sub_category <> '') AS sub有值,
       count(*) FILTER (WHERE gb_standard <> '') AS gb有值,
       count(*) AS 总行数
FROM chemical_hazard_categories;

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------

-- ── 全量清单 ──
SELECT regulation_number, current_version, has_full_text,
       deprecated_versions, change_notes
FROM chemical_regulation_versions ORDER BY id;

-- ── has_full_text vs 知识库实际覆盖 ──
SELECT file_name FROM knowledge_documents
WHERE file_name ~ '50160|50016|18218|617|30000|15603|30871';

-- ── 编号前缀遮蔽检查（短编号是长编号的前缀）──
SELECT a.regulation_number AS 短编号, b.regulation_number AS 长编号
FROM chemical_regulation_versions a
JOIN chemical_regulation_versions b
  ON b.regulation_number LIKE a.regulation_number || '%' AND a.id <> b.id;
-- 预期：>0 即存在 contains 遮蔽风险

-------------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------------











