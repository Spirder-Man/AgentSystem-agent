-- ============================================================================
-- 003_cleanup_stale_chemical_aliases.sql
-- 2026-08-11 本地库迁移修复（双人审批：已备份 local_chemical_park_ai_agent_before_cleanup_20260811.dump）
-- 问题：本地库残留旧版空壳表 chemical_aliases（0 行 / 无约束 / 无索引 / 无依赖），
--       导致 002_chemical_knowledge_graph.sql 执行时 CREATE TABLE IF NOT EXISTS 跳过建表，
--       INSERT 种子数据失败（id 列无 SERIAL 默认值）。
-- 处置：删除残留空壳表，让 002 迁移可完整重放（002 会重建标准结构并灌入种子数据）。
-- 安全依据：表 0 行数据、无任何约束/索引/依赖对象，删除不损失任何数据。
-- ============================================================================
BEGIN;
DROP TABLE IF EXISTS public.chemical_aliases;
COMMIT;
