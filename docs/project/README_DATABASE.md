# 数据库配置指南

## 概述

本项目使用 **PostgreSQL 16 + pgvector** 作为向量数据库，支持：
- 传统关系型数据存储（会话、审计日志等）
- 高维向量存储与检索（化工文档向量）
- BM25关键词检索 + 向量语义检索的混合检索

## 数据库表结构

### 1. sessions（会话表）

| 字段 | 类型 | 说明 |
|------|------|------|
| id | UUID | 会话ID，主键 |
| user_id | VARCHAR(100) | 用户ID |
| user_name | VARCHAR(200) | 用户名称 |
| session_data | TEXT | 会话数据（JSON格式） |
| created_at | TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | 更新时间 |

### 2. audit_logs（审计日志表）

| 字段 | 类型 | 说明 |
|------|------|------|
| id | SERIAL | 日志ID，主键 |
| session_id | UUID | 会话ID，外键 |
| user_id | VARCHAR(100) | 用户ID |
| action_type | VARCHAR(50) | 操作类型 |
| action_details | TEXT | 操作详情 |
| created_at | TIMESTAMP | 创建时间 |

### 3. chemical_documents（化工文档向量表）

| 字段 | 类型 | 说明 |
|------|------|------|
| id | SERIAL | 文档ID，主键 |
| content | TEXT | 文档内容 |
| embedding | vector(768) | 文档向量嵌入 |
| regulation_type | VARCHAR(100) | 法规类型 |
| priority | INTEGER | 优先级 |
| source_file | VARCHAR(255) | 来源文件 |
| chemical_type | VARCHAR(100) | 危化品类型 |

## 向量检索配置

### 安装pgvector扩展

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE INDEX idx_chemical_embedding ON chemical_documents
USING hnsw (embedding vector_cosine_ops);
```
