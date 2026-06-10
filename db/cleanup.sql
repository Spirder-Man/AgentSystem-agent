
-- ============================================================================
-- 清理化工向量表（仅删除本次新增的表）
-- ============================================================================

-- 删除索引
DROP INDEX IF EXISTS idx_chemical_documents_content_gin;
DROP INDEX IF EXISTS idx_chemical_documents_embedding_hnsw;
DROP INDEX IF EXISTS idx_chemical_documents_regulation_type;
DROP INDEX IF EXISTS idx_chemical_documents_chemical_type;
DROP INDEX IF EXISTS idx_chemical_documents_created_at;

-- 删除表
DROP TABLE IF EXISTS chemical_documents;

-- 保留其他表（sessions, audit_logs, search_logs 等不删除）

SELECT '✅ 化工向量表已清理完成' AS result;
