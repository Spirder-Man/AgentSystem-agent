
-- ============================================================================
-- 纯 SQL 验证向量数据库（无需 psql 命令）
-- ============================================================================

-- 1. 检查 pgvector 扩展
SELECT '检查 pgvector 扩展' AS section;
SELECT 
    extname AS extension_name,
    extversion AS extension_version
FROM pg_extension
WHERE extname = 'vector';

-- 2. 检查 chemical_documents 表是否存在
SELECT '检查 chemical_documents 表' AS section;
SELECT 
    tablename AS table_name,
    tableowner AS table_owner
FROM pg_tables
WHERE schemaname = 'public' AND tablename = 'chemical_documents';

-- 3. 查看表结构（通过 information_schema）
SELECT '查看 chemical_documents 表结构' AS section;
SELECT 
    ordinal_position AS column_order,
    column_name,
    data_type,
    character_maximum_length,
    is_nullable,
    column_default
FROM information_schema.columns
WHERE table_name = 'chemical_documents'
ORDER BY ordinal_position;

-- 4. 检查所有索引
SELECT '检查 chemical_documents 的索引' AS section;
SELECT 
    indexname AS index_name,
    indexdef AS index_definition
FROM pg_indexes
WHERE tablename = 'chemical_documents'
ORDER BY indexname;

-- 5. 检查全文搜索配置
SELECT '检查全文搜索配置' AS section;
SELECT 
    cfgname AS config_name
FROM pg_ts_config
WHERE cfgname IN ('simple', 'chinese');

-- 6. 测试向量类型
SELECT '测试向量功能' AS section;
SELECT 
    '[0.1, 0.2, 0.3]'::vector AS test_vector,
    vector_dims('[0.1, 0.2, 0.3]'::vector) AS dimension;

-- 7. 检查当前数据库中的所有表
SELECT '数据库所有表' AS section;
SELECT 
    tablename AS table_name,
    tableowner
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY tablename;

-- ============================================================================
-- 验证完成
-- ============================================================================
SELECT '✅ 向量数据库验证完成，请检查上面的结果！' AS final_result;
