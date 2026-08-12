-- ═══════════════════════════════════════════════════════════════
-- 006_drift_probe_templates.sql — 认知漂移监测·探针模板（黄金问题集）
--
-- 用途：主动测量入口。探针 = 覆盖血谱 L0 大动脉 / L1 静脉各血管的
--       "黄金问题"，定期喂给被测 AI（"这个项目的 X 是什么？"），
--       回答与期望锚点（drift_anchors.entity_key）比对得到漂移量。
--
-- 设计：
--   · 每条探针锚定一条期望锚点（anchor_key）——回答即使不提及锚点键名，
--     也会强制生成该锚点的断言（值匹配判定），区分"答错"与"未作答"
--   · vessel 标记探针归属血管（动脉A/动脉B/.../静脉1-5），source 标血谱行号
--   · drift_details 增列 probe_key，探针明细可追溯到具体问题
--
-- 执行方式：psql -f db/migrations/006_drift_probe_templates.sql
-- 依赖：004（drift_anchors）、005（drift_details）已执行
-- ═══════════════════════════════════════════════════════════════

-- ── 探针模板表（幂等建表） ──
CREATE TABLE IF NOT EXISTS drift_probe_templates (
  id SERIAL PRIMARY KEY,
  probe_key VARCHAR(64) NOT NULL UNIQUE,  -- 唯一键（probe_a1 等）
  vessel VARCHAR(32) NOT NULL,            -- 归属血管: 动脉A-D / 静脉1-5 / 约束
  domain VARCHAR(32) NOT NULL,            -- 分域（与锚点一致）
  question TEXT NOT NULL,                 -- 黄金问题（问被测 AI 的原文）
  anchor_key VARCHAR(255) NOT NULL,       -- 期望锚点键（drift_anchors.entity_key）
  severity SMALLINT NOT NULL DEFAULT 1,   -- 问题权重（照锚点严重度）
  enabled BOOLEAN NOT NULL DEFAULT TRUE,  -- 是否参与自动轮询
  source VARCHAR(255),                    -- 出处（血谱行号）
  version INT NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ── 探针明细追溯列 ──
ALTER TABLE drift_details ADD COLUMN IF NOT EXISTS probe_key VARCHAR(64);

-- ── 查询索引 ──
CREATE INDEX IF NOT EXISTS idx_drift_probe_templates_vessel ON drift_probe_templates(vessel, enabled);

-- ─────────────────────────────────────────────
-- 种子探针 v1（23 条：覆盖 4 大动脉 + 5 静脉 + 关键约束）
-- 问题 = 血谱 L0/L1 各血管的黄金问题；anchor_key 精确引用 004 锚点
-- ─────────────────────────────────────────────

-- ═══ 动脉 A：知识文档 → RAG 检索（4 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_a1','动脉A','architecture','知识库的分块文本与向量存在 PostgreSQL 的哪两张表？','动脉A双表',2,1,'系统血谱.md L170-171'),
('probe_a2','动脉A','architecture','文档向量化由哪个嵌入服务完成？它监听什么端口？','向量嵌入服务',1,1,'系统血谱.md L174-175'),
('probe_a3','动脉A','data','知识库增量加载靠什么文件记录文件变更状态？','file_tracker.json',1,1,'系统血谱.md L185'),
('probe_a4','动脉A','constraint','乱码文本块在入库前由什么组件拦截？','乱码闸门',2,1,'系统血谱.md L166');

-- ═══ 动脉 B：迁移脚本 → 知识图谱（3 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_b1','动脉B','architecture','知识图谱的官方数据存在哪五张表？','图谱五张表',2,1,'系统血谱.md L201-206'),
('probe_b2','动脉B','architecture','PostgreSQL 不可用时知识图谱还能启动吗？为什么？','图谱无兜底',2,1,'系统血谱.md L220'),
('probe_b3','动脉B','architecture','ChemicalSubstanceDatabase 是独立数据库还是门面？','图谱命名陷阱',2,1,'系统血谱.md L221');

-- ═══ 动脉 C：种子代码 → SQLite 兜底（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_c1','动脉C','architecture','SQLite 兜底库的文件路径是什么？','兜底存储',1,1,'系统血谱.md L48'),
('probe_c2','动脉C','architecture','SQLite 种子数据包含哪些内容（各类数量）？','SQLite种子规模',1,1,'系统血谱.md L234');

-- ═══ 动脉 D：配置与评测数据 → 运行时（3 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_d1','动脉D','config','敏感配置与非敏感配置分别存放在哪里？','敏感/非敏感分离',2,1,'系统血谱.md L257-259'),
('probe_d2','动脉D','data','合规评测集文件是什么？当前有多少条用例？','评测集',2,1,'系统血谱.md L274 [校准: 64条为过时记载]'),
('probe_d3','动脉D','architecture','全系统配置的唯一入口类是什么？','配置唯一入口',2,1,'系统血谱.md L262');

-- ═══ 静脉 1：三级降级瀑布（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_v1','静脉1','architecture','结构化查询（如"苯的安全距离"）的降级顺序是什么？','三级降级顺序',2,1,'系统血谱.md L305-321'),
('probe_v2','静脉1','architecture','为什么降级顺序是 SQLite 排第一而 PG 图谱排第二？','降级反直觉',2,1,'系统血谱.md L324');

-- ═══ 静脉 2：出口双通道闸门（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_v3','静脉2','architecture','回答离开系统前要经过哪五道闸门（按顺序）？','出口闸门链',2,1,'系统血谱.md L338-353'),
('probe_v4','静脉2','architecture','API 路径与 CLI 路径的末端验证分别是什么？','双通道末端验证',1,1,'系统血谱.md L355-357');

-- ═══ 静脉 3：记忆缓存旁路（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_v5','静脉3','architecture','缓存的分级 TTL 策略是什么（各命中类型的时长）？','缓存TTL分级',1,1,'系统血谱.md L380-382'),
('probe_v6','静脉3','data','缓存预热的数据来源是什么？预加载多少条？','缓存预热来源',1,1,'系统血谱.md L383');

-- ═══ 静脉 4：推理防御链（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_v7','静脉4','architecture','FC 策略由谁声明？三档分别用于什么场景？','FC策略声明',2,1,'系统血谱.md L396-398'),
('probe_v8','静脉4','architecture','评测路径如何控制变量（工具裁剪规则）？','评测工具裁剪',1,1,'系统血谱.md L406');

-- ═══ 静脉 5：API 长任务（1 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_v9','静脉5','architecture','长任务扫描接口的返回语义是什么（202/409）？','长任务模式',1,1,'系统血谱.md L420-432');

-- ═══ 关键约束（2 条） ═══
INSERT INTO drift_probe_templates (probe_key, vessel, domain, question, anchor_key, severity, version, source) VALUES
('probe_x1','约束','constraint','回答中引用法规版本时，白名单来自哪三个源的并集？','GB白名单三源并集',2,1,'系统血谱.md L357'),
('probe_x2','约束','constraint','LLM 推理的死循环检测从哪三个维度判断？','死循环检测',1,1,'系统血谱.md L402');

-- ── 自检：期望 23 条 ──
-- SELECT vessel, count(*) FROM drift_probe_templates GROUP BY vessel ORDER BY vessel;
