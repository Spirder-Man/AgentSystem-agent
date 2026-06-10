
-- ============================================================================
-- 验证向量数据库配置
-- ============================================================================

-- 1. 检查 pgvector 扩展是否已安装
SELECT 
    extname AS extension_name,
    extversion AS extension_version,
    extrelocatable AS is_relocatable
FROM pg_extension
WHERE extname = 'vector';

-- 2. 查看所有表（确认 chemical_documents 存在）
SELECT 
    tablename AS table_name,
    tablespace AS tablespace_name,
    tableowner AS table_owner
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY tablename;

-- 3. 查看 chemical_documents 表结构
\d chemical_documents

-- 4. 查看所有索引（确认向量索引已创建）
SELECT 
    indexname AS index_name,
    indexdef AS index_definition
FROM pg_indexes
WHERE tablename = 'chemical_documents'
ORDER BY indexname;

-- 5. 查看全文搜索配置
SELECT 
    cfgname AS config_name,
    cfgnamespace AS namespace
FROM pg_ts_config
WHERE cfgname IN ('simple', 'chinese');

-- 6. 查看 HNSW 索引的详细信息
SELECT 
    idxrelid::regclass AS index_name,
    indrelid::regclass AS table_name,
    pg_get_indexdef(idxrelid) AS index_def
FROM pg_index
WHERE idxrelid::regclass::text LIKE '%chemical_documents%';

-- 7. 测试向量类型是否正常工作
SELECT 
    '[1, 2, 3]'::vector AS test_vector,
    vector_dims('[1, 2, 3]'::vector) AS vector_dimension,
    '[1, 2, 3]'::vector &lt;-&gt; '[4, 5, 6]'::vector AS euclidean_distance,
    '[1, 2, 3]'::vector &lt;=&gt; '[4, 5, 6]'::vector AS cosine_distance;

-- ============================================================================
-- 验证完成
-- ============================================================================
SELECT '✅ 向量数据库验证完成' AS result;
