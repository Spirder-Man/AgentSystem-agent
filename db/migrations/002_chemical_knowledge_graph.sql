-- ============================================================================
-- 002_chemical_knowledge_graph.sql
-- 化工知识图谱持久化 — 替代 ChemicalSubstanceDatabase.cs 硬编码数据
-- ============================================================================

BEGIN;

-- ── 物质节点表 ──
CREATE TABLE IF NOT EXISTS chemical_substances (
    id              SERIAL PRIMARY KEY,
    name            VARCHAR(100) NOT NULL UNIQUE,
    name_en         VARCHAR(100) NOT NULL DEFAULT '',
    cas_number      VARCHAR(30)  NOT NULL DEFAULT '',
    un_number       VARCHAR(10)  NOT NULL DEFAULT '',
    formula         VARCHAR(50)  NOT NULL DEFAULT '',
    physical_state  VARCHAR(50)  NOT NULL DEFAULT '',
    flash_point_c   DOUBLE PRECISION,
    boiling_point_c DOUBLE PRECISION,
    explosive_lower DOUBLE PRECISION,
    explosive_upper DOUBLE PRECISION,
    auto_ignition_c DOUBLE PRECISION,
    relative_density DOUBLE PRECISION,
    vapor_density   DOUBLE PRECISION,
    major_hazard_threshold_tons DOUBLE PRECISION DEFAULT 0,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ── 别名表（多对一） ──
CREATE TABLE IF NOT EXISTS chemical_aliases (
    id              SERIAL PRIMARY KEY,
    substance_id    INT NOT NULL REFERENCES chemical_substances(id) ON DELETE CASCADE,
    alias_text      VARCHAR(100) NOT NULL,
    UNIQUE(substance_id, alias_text)
);
CREATE INDEX IF NOT EXISTS idx_chemical_aliases_text ON chemical_aliases(alias_text);

-- ── 危险类别表 ──
CREATE TABLE IF NOT EXISTS chemical_hazard_categories (
    id              SERIAL PRIMARY KEY,
    substance_id    INT NOT NULL REFERENCES chemical_substances(id) ON DELETE CASCADE,
    category        VARCHAR(100) NOT NULL,
    gb_standard     VARCHAR(30)  NOT NULL DEFAULT '',
    sub_category    VARCHAR(50)  NOT NULL DEFAULT ''
);

-- ── 储存禁忌类别（物质级） ──
CREATE TABLE IF NOT EXISTS chemical_incompatible_categories (
    id              SERIAL PRIMARY KEY,
    substance_id    INT NOT NULL REFERENCES chemical_substances(id) ON DELETE CASCADE,
    incompatible_with VARCHAR(100) NOT NULL
);

-- ── 精确禁忌配对表 ──
CREATE TABLE IF NOT EXISTS chemical_incompatibilities (
    id              SERIAL PRIMARY KEY,
    substance_a_id  INT NOT NULL REFERENCES chemical_substances(id) ON DELETE CASCADE,
    substance_b_id  INT NOT NULL REFERENCES chemical_substances(id) ON DELETE CASCADE,
    is_compatible   BOOLEAN NOT NULL DEFAULT FALSE,
    reason          TEXT,
    regulation_ref  VARCHAR(100) DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_chemical_incomp_a ON chemical_incompatibilities(substance_a_id);
CREATE INDEX IF NOT EXISTS idx_chemical_incomp_b ON chemical_incompatibilities(substance_b_id);

-- ── 安全距离表 ──
CREATE TABLE IF NOT EXISTS chemical_safety_distances (
    id              SERIAL PRIMARY KEY,
    facility_pair   VARCHAR(100) NOT NULL,
    min_distance_m  DOUBLE PRECISION NOT NULL,
    regulation_ref  VARCHAR(100) DEFAULT ''
);

-- ── 法规版本表 ──
CREATE TABLE IF NOT EXISTS chemical_regulation_versions (
    id                SERIAL PRIMARY KEY,
    regulation_number VARCHAR(30)  NOT NULL,
    title             VARCHAR(200) NOT NULL,
    current_version   VARCHAR(20)  NOT NULL DEFAULT '',
    has_full_text     BOOLEAN      NOT NULL DEFAULT FALSE,
    deprecated_versions TEXT,
    change_notes      TEXT
);

-- ============================================================================
-- 数据迁移：从 ChemicalSubstanceDatabase.cs 导出
-- ============================================================================

-- 使用 CTE + INSERT 批量导入物质+别名+类别

-- ▸ 1. 苯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('苯', 'Benzene', '71-43-2', '1114', 'C6H6', '液体', -11, 80.1, 1.2, 8.0, 560, 0.88, 2.77, 50);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '纯苯'   FROM chemical_substances WHERE name='苯' UNION ALL SELECT id, '安息油' FROM chemical_substances WHERE name='苯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',          'GB 30000.7',  '类别2'   FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '致癌性',            'GB 30000.23', '类别1A'  FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '严重眼损伤/刺激',   'GB 30000.20', '类别2'   FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '特异性靶器官毒性 反复接触', 'GB 30000.26', '类别1' FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '吸入危害',           'GB 30000.27', '类别1'   FROM chemical_substances WHERE name='苯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '强酸'     FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='苯' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='苯';

-- ▸ 2. 甲苯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('甲苯', 'Toluene', '108-88-3', '1294', 'C7H8', '液体', 4, 110.6, 1.2, 7.1, 535, 0.87, 3.14, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '甲基苯' FROM chemical_substances WHERE name='甲苯' UNION ALL SELECT id, 'Toluol' FROM chemical_substances WHERE name='甲苯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',          'GB 30000.7',  '类别2'  FROM chemical_substances WHERE name='甲苯' UNION ALL
SELECT id, '皮肤腐蚀/刺激',     'GB 30000.19', '类别2'  FROM chemical_substances WHERE name='甲苯' UNION ALL
SELECT id, '特异性靶器官毒性 反复接触', 'GB 30000.26', '类别2' FROM chemical_substances WHERE name='甲苯' UNION ALL
SELECT id, '吸入危害',           'GB 30000.27', '类别1'  FROM chemical_substances WHERE name='甲苯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂' FROM chemical_substances WHERE name='甲苯' UNION ALL
SELECT id, '强酸'   FROM chemical_substances WHERE name='甲苯' UNION ALL
SELECT id, '硝酸'   FROM chemical_substances WHERE name='甲苯';

-- ▸ 3. 二甲苯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('二甲苯', 'Xylene', '1330-20-7', '1307', 'C8H10', '液体', 25, 138.5, 1.0, 7.0, 463, 0.86, 3.66, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '混合二甲苯' FROM chemical_substances WHERE name='二甲苯' UNION ALL SELECT id, 'Xylol' FROM chemical_substances WHERE name='二甲苯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',       'GB 30000.7',  '类别3' FROM chemical_substances WHERE name='二甲苯' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别2' FROM chemical_substances WHERE name='二甲苯' UNION ALL
SELECT id, '吸入危害',        'GB 30000.27', '类别1' FROM chemical_substances WHERE name='二甲苯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂' FROM chemical_substances WHERE name='二甲苯' UNION ALL
SELECT id, '强酸'   FROM chemical_substances WHERE name='二甲苯';

-- ▸ 4. 甲醇
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('甲醇', 'Methanol', '67-56-1', '1230', 'CH3OH', '液体', 11, 64.7, 6.0, 36.5, 464, 0.79, 1.11, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '木醇'   FROM chemical_substances WHERE name='甲醇' UNION ALL SELECT id, '木精'   FROM chemical_substances WHERE name='甲醇' UNION ALL SELECT id, '甲基醇' FROM chemical_substances WHERE name='甲醇';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',          'GB 30000.7',  '类别2'                FROM chemical_substances WHERE name='甲醇' UNION ALL
SELECT id, '急性毒性',           'GB 30000.18', '类别3（经口/经皮/吸入）' FROM chemical_substances WHERE name='甲醇' UNION ALL
SELECT id, '特异性靶器官毒性 一次接触', 'GB 30000.25', '类别1'         FROM chemical_substances WHERE name='甲醇';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='甲醇' UNION ALL
SELECT id, '强酸'     FROM chemical_substances WHERE name='甲醇' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='甲醇' UNION ALL
SELECT id, '过氧化物' FROM chemical_substances WHERE name='甲醇';

-- ▸ 5. 乙醇
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('乙醇', 'Ethanol', '64-17-5', '1170', 'C2H5OH', '液体', 13, 78.3, 3.3, 19.0, 363, 0.79, 1.59, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '酒精' FROM chemical_substances WHERE name='乙醇' UNION ALL SELECT id, '火酒' FROM chemical_substances WHERE name='乙醇';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体', 'GB 30000.7', '类别2' FROM chemical_substances WHERE name='乙醇';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂' FROM chemical_substances WHERE name='乙醇' UNION ALL
SELECT id, '强酸'   FROM chemical_substances WHERE name='乙醇' UNION ALL
SELECT id, '硝酸'   FROM chemical_substances WHERE name='乙醇';

-- ▸ 6. 丙酮
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('丙酮', 'Acetone', '67-64-1', '1090', 'C3H6O', '液体', -18, 56.1, 2.5, 13.0, 465, 0.79, 2.0, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '二甲酮' FROM chemical_substances WHERE name='丙酮' UNION ALL SELECT id, '阿西通' FROM chemical_substances WHERE name='丙酮' UNION ALL SELECT id, '醋酮' FROM chemical_substances WHERE name='丙酮';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',          'GB 30000.7',  '类别2' FROM chemical_substances WHERE name='丙酮' UNION ALL
SELECT id, '严重眼损伤/刺激',   'GB 30000.20', '类别2' FROM chemical_substances WHERE name='丙酮' UNION ALL
SELECT id, '特异性靶器官毒性 一次接触', 'GB 30000.25', '类别3' FROM chemical_substances WHERE name='丙酮';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='丙酮' UNION ALL
SELECT id, '强酸'     FROM chemical_substances WHERE name='丙酮' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='丙酮' UNION ALL
SELECT id, '过氧化氢' FROM chemical_substances WHERE name='丙酮';

-- ▸ 7. 乙酸乙酯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('乙酸乙酯', 'Ethyl acetate', '141-78-6', '1173', 'C4H8O2', '液体', -4, 77.1, 2.2, 11.5, 426, 0.90, 3.04, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '醋酸乙酯' FROM chemical_substances WHERE name='乙酸乙酯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',         'GB 30000.7',  '类别2' FROM chemical_substances WHERE name='乙酸乙酯' UNION ALL
SELECT id, '严重眼损伤/刺激',  'GB 30000.20', '类别2' FROM chemical_substances WHERE name='乙酸乙酯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂' FROM chemical_substances WHERE name='乙酸乙酯' UNION ALL
SELECT id, '强酸'   FROM chemical_substances WHERE name='乙酸乙酯';

-- ▸ 8. 环氧乙烷
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('环氧乙烷', 'Ethylene oxide', '75-21-8', '1040', 'C2H4O', '气体（加压液化）', -18, 10.7, 3.0, 100, 429, 0.87, 1.52, 10);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氧化乙烯' FROM chemical_substances WHERE name='环氧乙烷' UNION ALL SELECT id, 'EO' FROM chemical_substances WHERE name='环氧乙烷' UNION ALL SELECT id, '噁烷' FROM chemical_substances WHERE name='环氧乙烷';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃气体',        'GB 30000.3',  '类别1'   FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '加压气体',        'GB 30000.6',  '液化气体' FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '致癌性',          'GB 30000.23', '类别1B'  FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '生殖细胞致突变性','GB 30000.22', '类别1B'  FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '急性毒性',        'GB 30000.18', '类别3（吸入）' FROM chemical_substances WHERE name='环氧乙烷';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '酸'       FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '碱'       FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '氨'       FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '胺类'     FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='环氧乙烷' UNION ALL
SELECT id, '金属氯化物' FROM chemical_substances WHERE name='环氧乙烷';

-- ▸ 9. 过氧化氢
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('过氧化氢', 'Hydrogen peroxide', '7722-84-1', '2015', 'H2O2', '液体', NULL, 150.2, NULL, NULL, NULL, 1.46, 1.0, 50);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '双氧水' FROM chemical_substances WHERE name='过氧化氢';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性液体',     'GB 30000.14', '类别1（≥60%）'    FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A'           FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '急性毒性',        'GB 30000.18', '类别4（经口/经皮/吸入）' FROM chemical_substances WHERE name='过氧化氢';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃液体' FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '易燃固体' FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '丙酮'     FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '乙醇'     FROM chemical_substances WHERE name='过氧化氢' UNION ALL
SELECT id, '甲醇'     FROM chemical_substances WHERE name='过氧化氢';

-- ▸ 10. 硝酸
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('硝酸', 'Nitric acid', '7697-37-2', '2031', 'HNO3', '液体', NULL, 83, NULL, NULL, NULL, 1.50, 2.17, 100);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '硝镪水'   FROM chemical_substances WHERE name='硝酸' UNION ALL SELECT id, '发烟硝酸' FROM chemical_substances WHERE name='硝酸';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性液体',     'GB 30000.14', '类别1'           FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1'           FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A'          FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '急性毒性',        'GB 30000.18', '类别3（吸入）'   FROM chemical_substances WHERE name='硝酸';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃液体' FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '易燃固体' FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '碱'       FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '氰化物'   FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '甲醇'     FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '乙醇'     FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '丙酮'     FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '甲苯'     FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '苯'       FROM chemical_substances WHERE name='硝酸' UNION ALL
SELECT id, '乙酸'     FROM chemical_substances WHERE name='硝酸';

-- ▸ 11. 硝酸铵
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('硝酸铵', 'Ammonium nitrate', '6484-52-2', '1942', 'NH4NO3', '固体', NULL, 210, NULL, NULL, NULL, 1.72, NULL, 50);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '硝铵' FROM chemical_substances WHERE name='硝酸铵' UNION ALL SELECT id, 'AN' FROM chemical_substances WHERE name='硝酸铵';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性固体', 'GB 30000.15', '类别2'               FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '爆炸物',     'GB 30000.2',  '非整体爆炸物（敏化后）' FROM chemical_substances WHERE name='硝酸铵';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃固体' FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '硫磺'     FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '铝粉'     FROM chemical_substances WHERE name='硝酸铵' UNION ALL
SELECT id, '易燃液体' FROM chemical_substances WHERE name='硝酸铵';

-- ▸ 12. 高锰酸钾
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('高锰酸钾', 'Potassium permanganate', '7722-64-7', '1490', 'KMnO4', '固体', NULL, NULL, NULL, NULL, NULL, 2.7, NULL, 50);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '灰锰氧' FROM chemical_substances WHERE name='高锰酸钾' UNION ALL SELECT id, 'PP粉' FROM chemical_substances WHERE name='高锰酸钾';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性固体',     'GB 30000.15', '类别1'           FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '急性毒性',        'GB 30000.18', '类别4（经口）'   FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '对水生环境危害',  'GB 30000.28', '类别1'           FROM chemical_substances WHERE name='高锰酸钾';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃液体' FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '易燃固体' FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '甘油'     FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '乙醇'     FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '硫酸'     FROM chemical_substances WHERE name='高锰酸钾' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='高锰酸钾';

-- ▸ 13. 重铬酸钠
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('重铬酸钠', 'Sodium dichromate', '10588-01-9', '3086', 'Na2Cr2O7', '固体', NULL, 400, NULL, NULL, NULL, 2.35, NULL, 50);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '红矾钠' FROM chemical_substances WHERE name='重铬酸钠';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性固体',       'GB 30000.15', '类别1'         FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '致癌性',           'GB 30000.23', '类别1B'        FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '生殖细胞致突变性', 'GB 30000.22', '类别1B'        FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '急性毒性',         'GB 30000.18', '类别2（经口）' FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别1B'        FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '对水生环境危害',   'GB 30000.28', '类别1'         FROM chemical_substances WHERE name='重铬酸钠';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃液体' FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '易燃固体' FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='重铬酸钠' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='重铬酸钠';

-- ▸ 14. 氯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氯', 'Chlorine', '7782-50-5', '1017', 'Cl2', '气体（加压液化）', NULL, -34.5, NULL, NULL, NULL, 1.47, 2.48, 5);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '液氯' FROM chemical_substances WHERE name='氯' UNION ALL SELECT id, '氯气' FROM chemical_substances WHERE name='氯' UNION ALL SELECT id, '绿气' FROM chemical_substances WHERE name='氯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '加压气体',        'GB 30000.6',  '液化气体'       FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '氧化性气体',      'GB 30000.5',  '类别1'          FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '急性毒性',         'GB 30000.18', '类别2（吸入）'  FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别2'          FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '严重眼损伤/刺激',  'GB 30000.20', '类别2'          FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '对水生环境危害',   'GB 30000.28', '类别1'          FROM chemical_substances WHERE name='氯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氨'     FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '氢'     FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '乙炔'   FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '烃类'   FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='氯' UNION ALL
SELECT id, '可燃物'   FROM chemical_substances WHERE name='氯';

-- ▸ 15. 氨
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氨', 'Ammonia', '7664-41-7', '1005', 'NH3', '气体（加压液化）', NULL, -33.4, 15.0, 28.0, 651, 0.82, 0.59, 10);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氨气'     FROM chemical_substances WHERE name='氨' UNION ALL SELECT id, '液氨'     FROM chemical_substances WHERE name='氨' UNION ALL SELECT id, '阿摩尼亚' FROM chemical_substances WHERE name='氨';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃气体',         'GB 30000.3',  '类别2'          FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '加压气体',         'GB 30000.6',  '液化气体'       FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '急性毒性',         'GB 30000.18', '类别3（吸入）'  FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别1B'         FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '对水生环境危害',   'GB 30000.28', '类别1'          FROM chemical_substances WHERE name='氨';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'     FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '卤素'       FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '酸'         FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '次氯酸盐'   FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '氯'         FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '氯化氢'     FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '溴'         FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '碘'         FROM chemical_substances WHERE name='氨' UNION ALL
SELECT id, '环氧乙烷'   FROM chemical_substances WHERE name='氨';

-- ▸ 16. 硫化氢
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('硫化氢', 'Hydrogen sulfide', '7783-06-4', '1053', 'H2S', '气体', NULL, -60.3, 4.3, 46.0, 260, NULL, 1.19, 5);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氢硫酸'   FROM chemical_substances WHERE name='硫化氢' UNION ALL SELECT id, '硫化氢气' FROM chemical_substances WHERE name='硫化氢';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃气体',       'GB 30000.3',  '类别1'          FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '加压气体',       'GB 30000.6',  '液化气体'       FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '急性毒性',       'GB 30000.18', '类别2（吸入）'  FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '对水生环境危害', 'GB 30000.28', '类别1'          FROM chemical_substances WHERE name='硫化氢';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '过氧化氢' FROM chemical_substances WHERE name='硫化氢' UNION ALL
SELECT id, '氯气'     FROM chemical_substances WHERE name='硫化氢';

-- ▸ 17. 乙炔
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('乙炔', 'Acetylene', '74-86-2', '1001', 'C2H2', '气体（溶解）', -18, -84, 2.5, 82.0, 305, NULL, 0.91, 1);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '电石气' FROM chemical_substances WHERE name='乙炔' UNION ALL SELECT id, '乙炔气' FROM chemical_substances WHERE name='乙炔';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃气体',  'GB 30000.3', '类别1'                           FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '加压气体',  'GB 30000.6', '溶解气体'                         FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '爆炸物',    'GB 30000.2', '不安定爆炸物（无空气也可爆炸）'   FROM chemical_substances WHERE name='乙炔';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧'         FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '氧化剂'     FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '卤素'       FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '铜'         FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '银'         FROM chemical_substances WHERE name='乙炔' UNION ALL
SELECT id, '汞及其化合物' FROM chemical_substances WHERE name='乙炔';

-- ▸ 18. 氢气
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氢气', 'Hydrogen', '1333-74-0', '1049', 'H2', '气体（压缩）', NULL, -252.8, 4.0, 75.0, 500, 0.07, 0.07, 5);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氢' FROM chemical_substances WHERE name='氢气';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃气体', 'GB 30000.3', '类别1'    FROM chemical_substances WHERE name='氢气' UNION ALL
SELECT id, '加压气体', 'GB 30000.6', '压缩气体' FROM chemical_substances WHERE name='氢气';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂' FROM chemical_substances WHERE name='氢气' UNION ALL
SELECT id, '氧'     FROM chemical_substances WHERE name='氢气' UNION ALL
SELECT id, '卤素'   FROM chemical_substances WHERE name='氢气' UNION ALL
SELECT id, '氯'     FROM chemical_substances WHERE name='氢气';

-- ▸ 19. 硫酸
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('硫酸', 'Sulfuric acid', '7664-93-9', '1830', 'H2SO4', '液体', NULL, 330, NULL, NULL, NULL, 1.84, 3.4, 100);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '磺镪水'   FROM chemical_substances WHERE name='硫酸' UNION ALL SELECT id, '发烟硫酸' FROM chemical_substances WHERE name='硫酸' UNION ALL SELECT id, '硫酸水'   FROM chemical_substances WHERE name='硫酸';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A' FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1'  FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '严重眼损伤/刺激','GB 30000.20', '类别1'  FROM chemical_substances WHERE name='硫酸';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃液体' FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '碱'       FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '有机物'   FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '还原剂'   FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '金属粉末' FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '氰化物'   FROM chemical_substances WHERE name='硫酸' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='硫酸';

-- ▸ 20. 盐酸
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('盐酸', 'Hydrochloric acid', '7647-01-0', '1789', 'HCl', '液体（氯化氢水溶液）', NULL, 108.6, NULL, NULL, NULL, 1.18, 1.27, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氢氯酸'     FROM chemical_substances WHERE name='盐酸' UNION ALL SELECT id, '氯化氢溶液' FROM chemical_substances WHERE name='盐酸' UNION ALL SELECT id, '盐镪水'     FROM chemical_substances WHERE name='盐酸';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1' FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1B' FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '严重眼损伤/刺激','GB 30000.20', '类别1' FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '特异性靶器官毒性 一次接触', 'GB 30000.25', '类别3' FROM chemical_substances WHERE name='盐酸';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '碱'     FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '氧化剂' FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '氰化物' FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '金属'   FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '胺类'   FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '氨'     FROM chemical_substances WHERE name='盐酸' UNION ALL
SELECT id, '氢氧化钠' FROM chemical_substances WHERE name='盐酸';

-- ▸ 21. 氢氧化钠
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氢氧化钠', 'Sodium hydroxide', '1310-73-2', '1823', 'NaOH', '固体', NULL, 1388, NULL, NULL, NULL, 2.13, NULL, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '烧碱'   FROM chemical_substances WHERE name='氢氧化钠' UNION ALL SELECT id, '火碱'   FROM chemical_substances WHERE name='氢氧化钠' UNION ALL SELECT id, '苛性钠' FROM chemical_substances WHERE name='氢氧化钠' UNION ALL SELECT id, '固碱'   FROM chemical_substances WHERE name='氢氧化钠';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1'  FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A' FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '严重眼损伤/刺激','GB 30000.20', '类别1'  FROM chemical_substances WHERE name='氢氧化钠';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '酸'           FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '氯化氢'       FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '铝'           FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '锌'           FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '锡'           FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '硝基化合物'   FROM chemical_substances WHERE name='氢氧化钠' UNION ALL
SELECT id, '氰化氢'       FROM chemical_substances WHERE name='氢氧化钠';

-- ▸ 22. 氢氧化钾
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氢氧化钾', 'Potassium hydroxide', '1310-58-3', '1813', 'KOH', '固体', NULL, 1320, NULL, NULL, NULL, 2.04, NULL, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '苛性钾' FROM chemical_substances WHERE name='氢氧化钾' UNION ALL SELECT id, '钾碱'   FROM chemical_substances WHERE name='氢氧化钾';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1'  FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A' FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '急性毒性',        'GB 30000.18', '类别4（经口）' FROM chemical_substances WHERE name='氢氧化钾';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '酸'     FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '氯化氢' FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '铝'     FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '锌'     FROM chemical_substances WHERE name='氢氧化钾' UNION ALL
SELECT id, '锡'     FROM chemical_substances WHERE name='氢氧化钾';

-- ▸ 23. 氢氟酸
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氢氟酸', 'Hydrofluoric acid', '7664-39-3', '1790', 'HF', '液体', NULL, 19.5, NULL, NULL, NULL, 1.15, 0.7, 1);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氟化氢溶液' FROM chemical_substances WHERE name='氢氟酸' UNION ALL SELECT id, '氟氢酸'     FROM chemical_substances WHERE name='氢氟酸';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '急性毒性',         'GB 30000.18', '类别1（经皮）' FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别1A'        FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '金属腐蚀物',       'GB 30000.17', '类别1'         FROM chemical_substances WHERE name='氢氟酸';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '碱'     FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '氨'     FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '氨水'   FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '玻璃'   FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '硅酸盐' FROM chemical_substances WHERE name='氢氟酸' UNION ALL
SELECT id, '金属'   FROM chemical_substances WHERE name='氢氟酸';

-- ▸ 24. 乙酸
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('乙酸', 'Acetic acid', '64-19-7', '2789', 'CH3COOH', '液体', 39, 118.1, 4.0, 19.9, 463, 1.05, 2.07, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '醋酸'   FROM chemical_substances WHERE name='乙酸' UNION ALL SELECT id, '冰醋酸' FROM chemical_substances WHERE name='乙酸' UNION ALL SELECT id, '冰乙酸' FROM chemical_substances WHERE name='乙酸';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',       'GB 30000.7',  '类别3'  FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1A' FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '金属腐蚀物',     'GB 30000.17', '类别1'  FROM chemical_substances WHERE name='乙酸';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '过氧化氢' FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '铬酸'     FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '碱'       FROM chemical_substances WHERE name='乙酸' UNION ALL
SELECT id, '氢氧化钠' FROM chemical_substances WHERE name='乙酸';

-- ▸ 25. 氰化钠
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氰化钠', 'Sodium cyanide', '143-33-9', '1689', 'NaCN', '固体', NULL, 1496, NULL, NULL, NULL, 1.6, NULL, 1);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '山奈'     FROM chemical_substances WHERE name='氰化钠' UNION ALL SELECT id, '山奈钠'   FROM chemical_substances WHERE name='氰化钠' UNION ALL SELECT id, '氰化钠盐' FROM chemical_substances WHERE name='氰化钠';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '急性毒性',         'GB 30000.18', '类别1（经口/经皮/吸入）' FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '对水生环境危害',   'GB 30000.28', '类别1'                   FROM chemical_substances WHERE name='氰化钠';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '酸'     FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '氧化剂' FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '硝酸'   FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '盐酸'   FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '硫酸'   FROM chemical_substances WHERE name='氰化钠' UNION ALL
SELECT id, '水（遇水可能释放HCN）' FROM chemical_substances WHERE name='氰化钠';

-- ▸ 26. 甲醛
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('甲醛', 'Formaldehyde', '50-00-0', '2209', 'CH2O', '液体（甲醛溶液）', 50, -19.5, 7.0, 73.0, 430, 1.08, 1.03, 5);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '福尔马林'   FROM chemical_substances WHERE name='甲醛' UNION ALL SELECT id, '甲醛溶液' FROM chemical_substances WHERE name='甲醛' UNION ALL SELECT id, '蚁醛'     FROM chemical_substances WHERE name='甲醛' UNION ALL SELECT id, '甲醛水'   FROM chemical_substances WHERE name='甲醛';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',       'GB 30000.7',  '类别3（甲醛溶液）'       FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '急性毒性',       'GB 30000.18', '类别3（经口/经皮/吸入）' FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1B'                  FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '致癌性',         'GB 30000.23', '类别1B'                  FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '皮肤致敏',       'GB 30000.21', '类别1'                   FROM chemical_substances WHERE name='甲醛';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '过氧化氢' FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '胺类'     FROM chemical_substances WHERE name='甲醛' UNION ALL
SELECT id, '氨'       FROM chemical_substances WHERE name='甲醛';

-- ▸ 27. 苯乙烯
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('苯乙烯', 'Styrene', '100-42-5', '2055', 'C8H8', '液体', 31, 145, 1.1, 8.0, 490, 0.91, 3.6, 500);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '乙烯基苯' FROM chemical_substances WHERE name='苯乙烯' UNION ALL SELECT id, '苏合香烯' FROM chemical_substances WHERE name='苯乙烯' UNION ALL SELECT id, 'ST'     FROM chemical_substances WHERE name='苯乙烯';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃液体',       'GB 30000.7',  '类别3'  FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别2'  FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '严重眼损伤/刺激','GB 30000.20', '类别2'  FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '致癌性',         'GB 30000.23', '类别2'  FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '特异性靶器官毒性 反复接触', 'GB 30000.26', '类别1' FROM chemical_substances WHERE name='苯乙烯';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '过氧化物'     FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '氧化剂'       FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '强酸'         FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '过氧化氢'     FROM chemical_substances WHERE name='苯乙烯' UNION ALL
SELECT id, '聚合引发剂'   FROM chemical_substances WHERE name='苯乙烯';

-- ▸ 28. 三氯甲烷
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('三氯甲烷', 'Chloroform', '67-66-3', '1888', 'CHCl3', '液体', NULL, 61.2, NULL, NULL, NULL, 1.48, 4.12, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氯仿'   FROM chemical_substances WHERE name='三氯甲烷' UNION ALL SELECT id, '哥罗仿' FROM chemical_substances WHERE name='三氯甲烷';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '急性毒性',         'GB 30000.18', '类别4（经口/经皮）' FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别2'              FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '严重眼损伤/刺激',  'GB 30000.20', '类别2'              FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '致癌性',           'GB 30000.23', '类别2'              FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '特异性靶器官毒性 反复接触', 'GB 30000.26', '类别1'     FROM chemical_substances WHERE name='三氯甲烷';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '强碱'   FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '碱金属' FROM chemical_substances WHERE name='三氯甲烷' UNION ALL
SELECT id, '铝'     FROM chemical_substances WHERE name='三氯甲烷';

-- ▸ 29. 丙三醇
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('丙三醇', 'Glycerol', '56-81-5', '', 'C3H8O3', '液体', 160, 290, NULL, NULL, 370, 1.26, 3.1, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '甘油'   FROM chemical_substances WHERE name='丙三醇' UNION ALL SELECT id, '丙三醇' FROM chemical_substances WHERE name='丙三醇';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='丙三醇' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='丙三醇' UNION ALL
SELECT id, '硝酸'     FROM chemical_substances WHERE name='丙三醇' UNION ALL
SELECT id, '铬酸'     FROM chemical_substances WHERE name='丙三醇' UNION ALL
SELECT id, '过氧化物' FROM chemical_substances WHERE name='丙三醇';

-- ▸ 30. 氯化氢
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氯化氢', 'Hydrogen chloride', '7647-01-0', '1050', 'HCl', '气体（液化）', NULL, -85, NULL, NULL, NULL, NULL, 1.27, 20);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氯化氢气' FROM chemical_substances WHERE name='氯化氢' UNION ALL SELECT id, '盐酸气'   FROM chemical_substances WHERE name='氯化氢' UNION ALL SELECT id, '无水盐酸' FROM chemical_substances WHERE name='氯化氢';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '加压气体',        'GB 30000.6',  '液化气体'       FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '急性毒性',         'GB 30000.18', '类别3（吸入）'  FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别1A'         FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '金属腐蚀物',       'GB 30000.17', '类别1'          FROM chemical_substances WHERE name='氯化氢';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '碱'       FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '胺类'     FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '氨'       FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '氢氧化钠' FROM chemical_substances WHERE name='氯化氢' UNION ALL
SELECT id, '活泼金属' FROM chemical_substances WHERE name='氯化氢';

-- ▸ 31. 二氧化硫
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('二氧化硫', 'Sulfur dioxide', '7446-09-5', '1079', 'SO2', '气体（液化）', NULL, -10, NULL, NULL, NULL, 1.46, 2.26, 20);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '亚硫酸酐' FROM chemical_substances WHERE name='二氧化硫' UNION ALL SELECT id, '亚硫酐'   FROM chemical_substances WHERE name='二氧化硫';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '加压气体',        'GB 30000.6',  '液化气体'       FROM chemical_substances WHERE name='二氧化硫' UNION ALL
SELECT id, '急性毒性',         'GB 30000.18', '类别3（吸入）'  FROM chemical_substances WHERE name='二氧化硫' UNION ALL
SELECT id, '皮肤腐蚀/刺激',    'GB 30000.19', '类别1B'         FROM chemical_substances WHERE name='二氧化硫' UNION ALL
SELECT id, '金属腐蚀物',       'GB 30000.17', '类别1'          FROM chemical_substances WHERE name='二氧化硫';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氨'     FROM chemical_substances WHERE name='二氧化硫' UNION ALL
SELECT id, '碱'     FROM chemical_substances WHERE name='二氧化硫' UNION ALL
SELECT id, '强还原剂' FROM chemical_substances WHERE name='二氧化硫';

-- ▸ 32. 氧气
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氧气', 'Oxygen', '7782-44-7', '1072', 'O2', '气体（压缩/液化）', NULL, -183, NULL, NULL, NULL, 1.14, 1.11, 200);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '液氧'   FROM chemical_substances WHERE name='氧气' UNION ALL SELECT id, '氧气瓶' FROM chemical_substances WHERE name='氧气' UNION ALL SELECT id, 'O2'    FROM chemical_substances WHERE name='氧气';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '氧化性气体', 'GB 30000.5', '类别1'                           FROM chemical_substances WHERE name='氧气' UNION ALL
SELECT id, '加压气体',   'GB 30000.6', '压缩气体/冷冻液化气体'           FROM chemical_substances WHERE name='氧气';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '易燃物' FROM chemical_substances WHERE name='氧气' UNION ALL
SELECT id, '还原剂' FROM chemical_substances WHERE name='氧气' UNION ALL
SELECT id, '油类'   FROM chemical_substances WHERE name='氧气' UNION ALL
SELECT id, '乙炔'   FROM chemical_substances WHERE name='氧气' UNION ALL
SELECT id, '氢气'   FROM chemical_substances WHERE name='氧气';

-- ▸ 33. 硫磺
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('硫磺', 'Sulfur', '7704-34-9', '1350', 'S8', '固体', 207, 444.6, NULL, NULL, 232, 2.07, NULL, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '硫黄' FROM chemical_substances WHERE name='硫磺' UNION ALL SELECT id, '硫磺粉' FROM chemical_substances WHERE name='硫磺';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '易燃固体', 'GB 30000.8', '类别2' FROM chemical_substances WHERE name='硫磺';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='硫磺' UNION ALL
SELECT id, '硝酸铵'   FROM chemical_substances WHERE name='硫磺' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='硫磺' UNION ALL
SELECT id, '氯酸盐'   FROM chemical_substances WHERE name='硫磺' UNION ALL
SELECT id, '硝酸盐'   FROM chemical_substances WHERE name='硫磺';

-- ▸ 34. 铝粉
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('铝粉', 'Aluminium powder', '7429-90-5', '1396', 'Al', '固体（粉末）', NULL, 2470, NULL, NULL, NULL, 2.7, NULL, 0);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '银粉'   FROM chemical_substances WHERE name='铝粉' UNION ALL SELECT id, '铝银粉' FROM chemical_substances WHERE name='铝粉';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '遇水放出易燃气体', 'GB 30000.13', '类别2'              FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '易燃固体',          'GB 30000.8',  '（粉尘有爆炸性）'   FROM chemical_substances WHERE name='铝粉';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '氧化剂'   FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '酸'       FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '碱'       FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '硝酸铵'   FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '高锰酸钾' FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '卤代烃'   FROM chemical_substances WHERE name='铝粉' UNION ALL
SELECT id, '水'       FROM chemical_substances WHERE name='铝粉';

-- ▸ 35. 氨溶液
INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state, flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
VALUES ('氨溶液', 'Ammonia solution', '1336-21-6', '2672', 'NH3·H2O', '液体', NULL, 38, NULL, NULL, NULL, 0.91, NULL, 10);
INSERT INTO chemical_aliases (substance_id, alias_text) SELECT id, '氨水'     FROM chemical_substances WHERE name='氨溶液' UNION ALL SELECT id, '氢氧化铵' FROM chemical_substances WHERE name='氨溶液' UNION ALL SELECT id, '阿摩尼亚水' FROM chemical_substances WHERE name='氨溶液';
INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category)
SELECT id, '皮肤腐蚀/刺激',  'GB 30000.19', '类别1B' FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '严重眼损伤/刺激','GB 30000.20', '类别1'  FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '对水生环境危害', 'GB 30000.28', '类别1'  FROM chemical_substances WHERE name='氨溶液';
INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with)
SELECT id, '酸'       FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '盐'       FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '卤素'     FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '次氯酸盐' FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '氯'       FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '氢氟酸'   FROM chemical_substances WHERE name='氨溶液' UNION ALL
SELECT id, '氯化氢'   FROM chemical_substances WHERE name='氨溶液';

-- ============================================================================
-- 精确禁忌配对
-- ============================================================================

-- 获取 ID 后插入精确配对
INSERT INTO chemical_incompatibilities (substance_a_id, substance_b_id, is_compatible, reason, regulation_ref)
SELECT a.id, b.id, TRUE,  '同类易燃液体可同库分区存放',                             'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='苯'   AND b.name='丙酮'
UNION ALL SELECT a.id, b.id, TRUE,  '同类易燃液体可同库分区存放',                     'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='丙酮' AND b.name='苯'
UNION ALL SELECT a.id, b.id, FALSE, '氧化剂与易燃液体严禁同库',                       'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='硝酸' AND b.name='乙酸'
UNION ALL SELECT a.id, b.id, FALSE, '酸碱中和放热反应，严禁同库混存',                 'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='氢氧化钠' AND b.name='盐酸'
UNION ALL SELECT a.id, b.id, FALSE, '氧化剂与易燃液体严禁同库，可能引发火灾爆炸',     'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='甲醇' AND b.name='硝酸'
UNION ALL SELECT a.id, b.id, FALSE, '强氧化剂与易燃液体严禁同库，过氧化氢遇有机物剧烈分解', 'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='过氧化氢' AND b.name='丙酮'
UNION ALL SELECT a.id, b.id, FALSE, '酸性气体与碱性气体混合产生氯化铵烟雾，严禁同区', 'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='氨' AND b.name='氯化氢'
UNION ALL SELECT a.id, b.id, FALSE, '氯与氨反应生成三氯化氮(易爆)，严禁同区混存',    'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='氯' AND b.name='氨'
UNION ALL SELECT a.id, b.id, TRUE,  '同类易燃液体（均为C类）可同库分区存放',          'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='甲苯' AND b.name='二甲苯'
UNION ALL SELECT a.id, b.id, FALSE, '强氧化剂与易燃液体(甘油)严禁混存，接触可能自燃', ''        FROM chemical_substances a, chemical_substances b WHERE a.name='高锰酸钾' AND b.name='丙三醇'
UNION ALL SELECT a.id, b.id, FALSE, '环氧乙烷遇氨可能发生聚合反应放热爆炸',           ''        FROM chemical_substances a, chemical_substances b WHERE a.name='环氧乙烷' AND b.name='氨'
UNION ALL SELECT a.id, b.id, TRUE,  '同类易燃液体可同库分区存放',                     'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='丙酮' AND b.name='乙醇'
UNION ALL SELECT a.id, b.id, FALSE, '硝酸铵为强氧化剂，硫磺为易燃固体，混合可形成爆炸性混合物', '' FROM chemical_substances a, chemical_substances b WHERE a.name='硝酸铵' AND b.name='硫磺'
UNION ALL SELECT a.id, b.id, FALSE, '酸碱中和放热，产生有毒氟化铵，严禁混存',         ''        FROM chemical_substances a, chemical_substances b WHERE a.name='氢氟酸' AND b.name='氨溶液'
UNION ALL SELECT a.id, b.id, FALSE, '过氧化物可引发苯乙烯剧烈聚合放热，存在爆炸风险', ''        FROM chemical_substances a, chemical_substances b WHERE a.name='苯乙烯' AND b.name='过氧化氢'
UNION ALL SELECT a.id, b.id, TRUE,  '同属酸性气体(还原性)，可同库但需有效隔离和通风', ''        FROM chemical_substances a, chemical_substances b WHERE a.name='硫化氢' AND b.name='二氧化硫'
UNION ALL SELECT a.id, b.id, FALSE, '易燃气体与助燃气体严禁同库，乙炔遇氧爆炸极限极宽(2.5-82%)', 'GB 15603' FROM chemical_substances a, chemical_substances b WHERE a.name='乙炔' AND b.name='氧气'
UNION ALL SELECT a.id, b.id, TRUE,  '两种强酸可同库分区存放，但需注意硝酸为氧化性需防腐蚀隔离', '' FROM chemical_substances a, chemical_substances b WHERE a.name='硝酸' AND b.name='盐酸'
UNION ALL SELECT a.id, b.id, FALSE, '氰化钠遇酸产生剧毒氰化氢(HCN)气体，严禁共库',   ''        FROM chemical_substances a, chemical_substances b WHERE a.name='氰化钠' AND b.name='盐酸'
UNION ALL SELECT a.id, b.id, FALSE, '金属粉末与氧化剂混合可形成爆炸性混合物，严禁混存', ''      FROM chemical_substances a, chemical_substances b WHERE a.name='铝粉' AND b.name='硝酸铵'
UNION ALL SELECT a.id, b.id, TRUE,  '无明确配伍禁忌，可同库分区存放',                 ''        FROM chemical_substances a, chemical_substances b WHERE a.name='三氯甲烷' AND b.name='丙酮';

-- ============================================================================
-- 安全距离数据
-- ============================================================================
INSERT INTO chemical_safety_distances (facility_pair, min_distance_m, regulation_ref) VALUES
('储罐-储罐',            15,    'GB 50160'),
('储罐-建筑',            25,    'GB 50160'),
('储罐-消防通道',        15,    'GB 50160'),
('储罐-厂区边界',        30,    'GB 50160'),
('液化烃储罐-储罐',      20,    'GB 50160'),
('液化烃储罐-厂区围墙',  35,    'GB 50160'),
('甲类仓库-建筑',        20,    'GB 50160 / GB 50016'),
('甲类仓库-明火点',      30,    'GB 50160'),
('甲类仓库-办公楼',      30,    'GB 50160'),
('甲类工艺装置-重要设施', 30,    'GB 50160'),
('甲类工艺装置-明火点',  30,    'GB 50160'),
('乙炔气柜-建筑',        25,    'GB 50160'),
('氨罐-厂外道路',        20,    'GB 50160'),
('氢气长管拖车-明火点',  25,    'GB 50160'),
('消防站-甲类装置',      15,    'GB 50160'),
('氯气储存区-居住区',    200,   'GB 50160（依据重大危险源等级）'),
('液化烃储罐-办公楼',    35,    'GB 50160'),
('易燃液体储罐-装卸站',  15,    'GB 50160'),
('甲类仓库-厂内道路',    15,    'GB 50016'),
('甲类厂房-甲类厂房',    12,    'GB 50016');

-- ============================================================================
-- 法规版本数据
-- ============================================================================
INSERT INTO chemical_regulation_versions (regulation_number, title, current_version, has_full_text, deprecated_versions, change_notes) VALUES
('GB 15603',  '常用化学危险品贮存通则',                    '2022',             TRUE,  '1995', '2022版更新了禁忌物料配存表、新增了危险化学品仓库分类储存要求'),
('GB 30000',  '化学品分类和标签规范',                      '2013',             TRUE,  '',     '系列标准共29部分（GB 30000.1-29），另有2024修订GB 30000.1-2024'),
('GB 30000.1','化学品分类和标签规范 第1部分:通则',          '2024',             TRUE,  '2013', '2024版更新了定义、分类标准，与GHS第8修订版接轨'),
('GB 50160',  '石油化工企业设计防火规范',                  '2008（2018局部修订）', FALSE, '',     '包含防火间距、储罐间距等关键安全距离数据'),
('GB 50016',  '建筑设计防火规范',                          '2014（2018局部修订）', FALSE, '2006', '规定了甲/乙/丙/丁/戊类厂房仓库的耐火等级与防火间距'),
('GB 18218',  '危险化学品重大危险源辨识',                  '2018',             FALSE, '2009', '重大危险源分级标准，定义了各危化品临界量'),
('GB 30871',  '危险化学品企业特殊作业安全规范',             '2022',             TRUE,  '2014', '2022版新增电子作业票、能量隔离等要求'),
('JT/T 617',  '危险货物道路运输规则',                      '2018',             FALSE, '',     '系列标准共7部分，规定了道路危险货物运输的各项要求');

COMMIT;
