-- ============================================================================
-- 007_unify_incompatible_categories.sql
-- #30 禁忌类别词表统一 + 去重 + UNIQUE 约束
--
-- 种子数据长期存在同义不同写：氨/氨水、氯/氯气、铝/铝粉、氢/氢气、碱/强碱、酸/强酸。
-- 本迁移先折叠同义词，再删除折叠后产生的重复行，最后加 UNIQUE(substance_id, incompatible_with)。
-- ============================================================================

BEGIN;

-- 1) 同义词折叠到统一词根（与 ChemicalKnowledgeGraph.NormalizeCategoryTerm 保持一致）
UPDATE chemical_incompatible_categories SET incompatible_with = '氨' WHERE incompatible_with = '氨水';
UPDATE chemical_incompatible_categories SET incompatible_with = '氯' WHERE incompatible_with = '氯气';
UPDATE chemical_incompatible_categories SET incompatible_with = '铝' WHERE incompatible_with = '铝粉';
UPDATE chemical_incompatible_categories SET incompatible_with = '氢' WHERE incompatible_with = '氢气';
UPDATE chemical_incompatible_categories SET incompatible_with = '碱' WHERE incompatible_with = '强碱';
UPDATE chemical_incompatible_categories SET incompatible_with = '酸' WHERE incompatible_with = '强酸';

-- 2) 折叠后同一物质可能产生多行相同禁忌词，保留 id 最小的一行
DELETE FROM chemical_incompatible_categories a
USING chemical_incompatible_categories b
WHERE a.substance_id = b.substance_id
  AND a.incompatible_with = b.incompatible_with
  AND a.id > b.id;

-- 3) UNIQUE 约束（先查再建，幂等）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_chemical_incompatible_categories_substance_with'
    ) THEN
        ALTER TABLE chemical_incompatible_categories
            ADD CONSTRAINT uq_chemical_incompatible_categories_substance_with
            UNIQUE (substance_id, incompatible_with);
    END IF;
END $$;

COMMIT;
