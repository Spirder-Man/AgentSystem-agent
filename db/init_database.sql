
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
-- 7. 知识库文档表（文档-分块双层模型，替代旧 chemical_documents 扁平表）
-- ============================================================================

-- 7a. 文档级元数据表（一个物理文件 = 一行）
CREATE TABLE IF NOT EXISTS knowledge_documents (
    id              SERIAL PRIMARY KEY,
    source_path     VARCHAR(500) NOT NULL UNIQUE,   -- 相对路径，如 "化工专业条例/化工专业条例/GB 30000.7-2013.pdf"
    file_name       VARCHAR(300) NOT NULL,           -- 展示名
    file_format     VARCHAR(10)  NOT NULL,           -- pdf / doc / docx / txt
    file_size_bytes BIGINT,
    regulation_type VARCHAR(50)  NOT NULL,           -- 国标 / 园区规则 / 历史案例 / 企业制度 / 化工专业条例
    regulation_number VARCHAR(100),                  -- 如 "GB 30000.7-2013"
    regulation_title  VARCHAR(500),                  -- 如 "化学品分类和标签规范 第7部分：易燃液体"
    priority        VARCHAR(10)  NOT NULL DEFAULT '中',  -- 高 / 中 / 低
    parent_category VARCHAR(200),                    -- H166 层级路径，如 "1.法律法规/1.1识别和获取"
    extraction_quality VARCHAR(20) DEFAULT 'good',  -- good / partial / failed
    page_count      INT,
    is_full_text    BOOLEAN DEFAULT TRUE,            -- false=仅文件名摘要入库
    total_chunks    INT DEFAULT 0,
    content_hash    VARCHAR(64),                     -- SHA-256，用于增量更新检测
    last_modified   TIMESTAMP WITH TIME ZONE,
    created_at      TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- 7b. 分块级向量表（一个语义块 = 一行 + 向量嵌入）
CREATE TABLE IF NOT EXISTS knowledge_chunks (
    id              SERIAL PRIMARY KEY,
    document_id     INT NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
    content         TEXT NOT NULL,
    embedding       vector(768),
    chunk_index     INT NOT NULL DEFAULT 0,          -- 同文档内块序号
    chapter_number  VARCHAR(50),                     -- "3" 或 "第3章"
    chapter_title   VARCHAR(500),                    -- "术语和定义"
    clause_number   VARCHAR(50),                     -- "3.1", "3.2.1"
    article_number  VARCHAR(50),                     -- "第五条" (园区规则)
    page_number     INT,
    sub_chunk_index INT,                             -- 章节内再分块子序号
    regulation_type VARCHAR(50)  NOT NULL,           -- 冗余字段，加速过滤
    priority        VARCHAR(10)  NOT NULL,           -- 冗余字段，加速过滤
    created_at      TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- 7c. 索引
CREATE INDEX IF NOT EXISTS idx_chunks_embedding ON knowledge_chunks
    USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 200);
CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON knowledge_chunks(document_id);
CREATE INDEX IF NOT EXISTS idx_chunks_regulation_type ON knowledge_chunks(regulation_type);
CREATE INDEX IF NOT EXISTS idx_chunks_priority ON knowledge_chunks(priority);
CREATE INDEX IF NOT EXISTS idx_docs_regulation_number ON knowledge_documents(regulation_number);
CREATE INDEX IF NOT EXISTS idx_docs_type_priority ON knowledge_documents(regulation_type, priority);

-- ============================================================================
-- 8. 验证表结构
-- ============================================================================
\dt
\d+ knowledge_documents
\d+ knowledge_chunks

-- ============================================================================
-- 初始化完成！
-- ============================================================================
SELECT '✅ 数据库初始化成功！' AS result;

