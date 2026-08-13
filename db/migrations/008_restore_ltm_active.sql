-- ============================================================================
-- 008_restore_ltm_active.sql
-- #18 历史记忆恢复：旧版冲突解决按 memory_type 整组停用，导致 1157→43 记忆空心化。
-- 本迁移按“用户+类型+归一化内容”分组，只保留每组最新一条为活跃：
--   - 无活跃版本的历史事实 → 恢复最新版本为活跃（旧停用是 Bug 级联，不是用户删除）
--   - 已有多个活跃重复 → 折叠为最新一条
-- 执行前必须 pg_dump 备份 long_term_memories。
-- ============================================================================

BEGIN;

WITH ranked AS (
    SELECT id,
           row_number() OVER (
               PARTITION BY user_id, memory_type,
                   regexp_replace(lower(content), '[\s\u3000]+', '', 'g')
               ORDER BY created_at DESC, id DESC
           ) AS rn
    FROM long_term_memories
)
UPDATE long_term_memories m
SET is_active = (r.rn = 1),
    updated_at = CURRENT_TIMESTAMP
FROM ranked r
WHERE m.id = r.id;

COMMIT;
