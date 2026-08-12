-- ═══════════════════════════════════════════════════════════════
-- 005_drift_probe_tables.sql — 认知漂移监测·测量记录表
--
-- 用途：承载"测量批次"（drift_probes）+ "断言明细"（drift_details）。
--       每次测量 = 对一段 AI 输出抽取断言 → 与锚点（drift_anchors）比对
--       → 得到漂移率（drift_score）与每条断言的去向。
--
-- 设计：
--   · drift_probes 以 (session_id, turn_no, trigger_type) 为幂等键——
--     同一轮对话重放测量时覆盖更新，不重复堆积
--   · domain_breakdown 存分域漂移 JSONB（[{domain, err, total, score}]）
--   · drift_details 经 probe_id 外键级联删除（重放时先删旧明细再插新）
--
-- 执行方式：psql -f db/migrations/005_drift_probe_tables.sql
-- 依赖：004_drift_anchor_baseline.sql 已执行（drift_anchors 表存在）
-- ═══════════════════════════════════════════════════════════════

-- ── 测量批次表 ──
CREATE TABLE IF NOT EXISTS drift_probes (
  id BIGSERIAL PRIMARY KEY,
  session_id VARCHAR(64) NOT NULL,    -- 会话 ID（可映射 TraceId）
  turn_no INT NOT NULL,               -- 对话轮次
  trigger_type VARCHAR(16) NOT NULL DEFAULT 'reply',  -- reply=被动 / probe=主动 / code_change=变更 / session_end=归档
  context_tokens INT,                 -- 测量时的上下文 token 数（漂移 vs 上下文长度的关系曲线）
  claim_count INT NOT NULL DEFAULT 0, -- 抽取断言数
  match_count INT NOT NULL DEFAULT 0, -- 完全匹配数
  drift_score NUMERIC(6,4) NOT NULL DEFAULT 0,  -- 加权漂移率 0~1（Σ(sev·err)/Σsev）
  domain_breakdown JSONB,             -- 分域明细 [{domain, total, err, score}]
  anchor_version INT NOT NULL DEFAULT 1,  -- 比对所用锚点版本（防"测量仪自漂移"）
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (session_id, turn_no, trigger_type)
);

-- ── 断言明细表 ──
CREATE TABLE IF NOT EXISTS drift_details (
  id BIGSERIAL PRIMARY KEY,
  probe_id BIGINT NOT NULL REFERENCES drift_probes(id) ON DELETE CASCADE,
  entity_key VARCHAR(255) NOT NULL,   -- 被谈论的锚点实体
  domain VARCHAR(32) NOT NULL,        -- 分域
  severity SMALLINT NOT NULL DEFAULT 1, -- 锚点严重度（结构级错误加权）
  expected TEXT,                      -- 锚点基准值
  actual TEXT,                        -- AI 文本中实际提及的强标记
  match NUMERIC(3,2) NOT NULL DEFAULT 0, -- 0 / 0.5 / 1
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ── 查询索引 ──
CREATE INDEX IF NOT EXISTS idx_drift_probes_session ON drift_probes(session_id, turn_no);
CREATE INDEX IF NOT EXISTS idx_drift_details_probe ON drift_details(probe_id);
