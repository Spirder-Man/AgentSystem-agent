-- ─────────────────────────────────────────────────────────────
-- 【1.1 我在哪个库？什么版本？】验证连接无误，数据库基本信息
-- 输出解读：current_database = chemical_park_ai_agent（项目库）
--          current_user = postgres（超级用户）
--          version 里能看到 "PostgreSQL 16.13" 和编译信息
-- ─────────────────────────────────────────────────────────────
SELECT current_database(), current_user, version();

-- ─────────────────────────────────────────────────────────────
-- 【1.2 全库表清单 + 估算行数】一眼看出 15 张表谁有数据谁是空壳
-- 输出解读：
--   表名          → 所有用户表
--   估算行数      → n_live_tup 是统计器估算值（不是精确 count）
--   last_vacuum   → 是否被清理过（NULL 说明表基本没写入）
-- 学习要点（2026-08-11 整库替换后）：本地 15 张 = 8 核心 + 7 chemical_*
--          与远程生产库一致，多数表已有真实数据（audit_logs 1189 等）；
--          估算值不可靠（chemical_substances 估算 0、实际 35），以 count(*) 为准
-- ─────────────────────────────────────────────────────────────
SELECT relname                                  AS 表名,
       n_live_tup                               AS 估算行数,
       last_vacuum IS NOT NULL                  AS 曾真空,
       last_autovacuum                          AS 最后自动清理
FROM pg_stat_user_tables
ORDER BY n_live_tup DESC;

-- ─────────────────────────────────────────────────────────────
-- 【1.3 已安装的扩展】确认 pgvector 在不在（向量检索的前提）
-- 输出解读：
--   plpgsql → PostgreSQL 内置存储过程语言（标配）
--   vector  → pgvector 扩展（0.8.2），embedding 列的类型就是它提供的
-- 学习要点：没有 vector 扩展，chemical_documents 等表的
--          vector(768) 列根本无法创建——这是"扩展先行"的初始化顺序原因
-- ─────────────────────────────────────────────────────────────
SELECT extname, extversion
FROM pg_extension
ORDER BY extname;