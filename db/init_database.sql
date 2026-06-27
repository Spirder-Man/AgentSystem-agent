
-- ============================================================================
-- 化工园区危化品合规审核AI Agent - 数据库初始化脚本
-- ============================================================================
-- 创建日期: 2026-05-16
-- 数据库: PostgreSQL + pgvector
-- ============================================================================

-- ============================================================================
-- 1. 启用pgvector扩展
-- ============================================================================
CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================================
-- 2. 创建会话表
-- ============================================================================
CREATE TABLE IF NOT EXISTS sessions (
    id UUID PRIMARY KEY,
    user_id VARCHAR(100) NOT NULL,
    user_name VARCHAR(200),
    session_data TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP WITH TIME ZONE
);

-- ============================================================================
-- 3. 创建审计日志表
-- ============================================================================
CREATE TABLE IF NOT EXISTS audit_logs (
    id SERIAL PRIMARY KEY,
    session_id UUID NOT NULL,
    user_id VARCHAR(100) NOT NULL,
    action_type VARCHAR(50) NOT NULL,
    action_details TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_audit_logs_session_id ON audit_logs(session_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_user_id ON audit_logs(user_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_created_at ON audit_logs(created_at);

-- ============================================================================
-- 4. 创建搜索日志表
-- ============================================================================
CREATE TABLE IF NOT EXISTS search_logs (
    id SERIAL PRIMARY KEY,
    session_id UUID NOT NULL,
    query_text TEXT NOT NULL,
    search_mode VARCHAR(20),
    num_results INT,
    response_time_ms INT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_search_logs_session_id ON search_logs(session_id);
CREATE INDEX IF NOT EXISTS idx_search_logs_created_at ON search_logs(created_at);

-- ============================================================================
-- 5. 创建化工文档表（向量表）
-- ============================================================================
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

-- ============================================================================
-- 6. 创建索引
-- ============================================================================

-- 向量索引（HNSW用于高召回率的相似性搜索）
CREATE INDEX IF NOT EXISTS idx_chemical_documents_embedding_hnsw 
ON chemical_documents USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 200);

-- 业务字段索引
CREATE INDEX IF NOT EXISTS idx_chemical_documents_regulation_type 
ON chemical_documents (regulation_type);

CREATE INDEX IF NOT EXISTS idx_chemical_documents_chemical_type 
ON chemical_documents (chemical_type);

CREATE INDEX IF NOT EXISTS idx_chemical_documents_created_at 
ON chemical_documents (created_at);

-- 添加哈希链列（等保三级防篡改）
ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS chain_hash TEXT;

-- ============================================================================
-- 7. 验证表结构
-- ============================================================================
\dt
\d+ chemical_documents

-- ============================================================================
-- 初始化完成！
-- ============================================================================
SELECT '✅ 数据库初始化成功！' AS result;

