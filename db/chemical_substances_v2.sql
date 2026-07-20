-- ============================================================================
-- 化工危化品结构化数据库 v2 - SQLite Schema
-- 创建日期: 2026-06-27
-- 
-- 设计原则（零失误架构）:
--   1. 危险类别/安全距离/临界量等确定性数据 100% 走数据库查询
--   2. RAG 仅用于法规解释与案例参考，不参与数值/分类判定
--   3. 数据库未命中的化学品 → 标准化拒绝，不得猜测
-- 
-- 数据来源: GB 30000 系列, GB 18218, 危险化学品目录(2015版)
-- ============================================================================

-- 化学品主表
CREATE TABLE IF NOT EXISTS substances (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,           -- 标准中文名
    name_en TEXT,                         -- 英文名
    cas_number TEXT,                      -- CAS号
    un_number TEXT,                       -- UN编号
    formula TEXT,                         -- 分子式
    physical_state TEXT,                  -- 物理状态
    flash_point_c REAL,                   -- 闪点(℃)
    boiling_point_c REAL,                 -- 沸点(℃)
    explosive_lower REAL,                 -- 爆炸下限(%)
    explosive_upper REAL,                 -- 爆炸上限(%)
    auto_ignition_c REAL,                 -- 自燃温度(℃)
    relative_density REAL,                -- 相对密度
    vapor_density REAL,                   -- 蒸气密度
    major_hazard_threshold_tons REAL     -- GB18218 重大危险源临界量(吨), 0表示非重大危险源
);

-- 危险类别关联表（1:N, 一种化学品可有多个危险类别）
CREATE TABLE IF NOT EXISTS hazard_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    category TEXT NOT NULL,               -- 危险类别（如"易燃液体"）
    gb_standard TEXT NOT NULL,            -- GB标准编号（如"GB 30000.7"）
    sub_category TEXT,                    -- 子类别（如"类别2"）
    hazard_code TEXT                      -- GHS危险性代码（如"H225"）
);

CREATE INDEX IF NOT EXISTS idx_hc_substance ON hazard_categories(substance_id);
CREATE INDEX IF NOT EXISTS idx_hc_gb ON hazard_categories(gb_standard);

-- 别名表（如"双氧水"→"过氧化氢"）
CREATE TABLE IF NOT EXISTS substance_aliases (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    alias TEXT NOT NULL UNIQUE
);

CREATE INDEX IF NOT EXISTS idx_alias_substance ON substance_aliases(substance_id);
CREATE INDEX IF NOT EXISTS idx_alias_alias ON substance_aliases(alias);

-- 储存禁忌类别表
CREATE TABLE IF NOT EXISTS incompatibility_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    incompatible_with TEXT NOT NULL       -- 禁忌类别/化学品名
);

CREATE INDEX IF NOT EXISTS idx_ic_substance ON incompatibility_categories(substance_id);

-- 储存兼容性精确规则表（两化学品配对）
CREATE TABLE IF NOT EXISTS storage_compatibility_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_a TEXT NOT NULL,            -- 化学品A名称
    substance_b TEXT NOT NULL,            -- 化学品B名称
    is_compatible INTEGER NOT NULL,       -- 1=可同库, 0=不可同库
    reason TEXT NOT NULL,                 -- 原因说明
    regulation_ref TEXT                   -- 法规依据
);

-- 安全距离表
CREATE TABLE IF NOT EXISTS safety_distances (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    facility_pair TEXT NOT NULL,          -- 设施对（如"甲类仓库-明火点"）
    facility_alias TEXT,                  -- 别名（逗号分隔，如"甲库-明火,甲类仓库与明火点"）
    min_distance_m REAL NOT NULL,         -- 最小距离(米)
    regulation_ref TEXT NOT NULL,         -- 法规依据（如"GB 50160"）
    clause_ref TEXT,                      -- 条款引用（如"第5.2.1条"）
    notes TEXT                            -- 备注说明
);

CREATE INDEX IF NOT EXISTS idx_sd_pair ON safety_distances(facility_pair);

-- 法规版本追踪表
CREATE TABLE IF NOT EXISTS regulation_versions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    regulation_number TEXT NOT NULL,      -- 法规编号（如"GB 15603"）
    title TEXT,                            -- 法规名称
    current_version TEXT,                  -- 现行版本
    deprecated_versions TEXT,             -- 已废止版本（逗号分隔）
    has_full_text INTEGER DEFAULT 0,      -- 知识库是否收录全文
    change_notes TEXT                     -- 关键变更说明
);

CREATE INDEX IF NOT EXISTS idx_rv_number ON regulation_versions(regulation_number);

-- ============================================================================
-- 视图：化学品完整信息（含危险类别聚合）
-- ============================================================================
CREATE VIEW IF NOT EXISTS v_substance_full AS
SELECT 
    s.id, s.name, s.name_en, s.cas_number, s.un_number, s.formula,
    s.physical_state, s.flash_point_c, s.boiling_point_c,
    s.explosive_lower, s.explosive_upper, s.auto_ignition_c,
    s.relative_density, s.vapor_density, s.major_hazard_threshold_tons,
    GROUP_CONCAT(DISTINCT hc.category || COALESCE(',' || hc.sub_category, ''), '; ') AS hazard_categories_str,
    GROUP_CONCAT(DISTINCT hc.gb_standard, ', ') AS gb_standards_str,
    GROUP_CONCAT(DISTINCT sa.alias, ', ') AS aliases_str
FROM substances s
LEFT JOIN hazard_categories hc ON hc.substance_id = s.id
LEFT JOIN substance_aliases sa ON sa.substance_id = s.id
GROUP BY s.id;
