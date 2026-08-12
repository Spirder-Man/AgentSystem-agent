-- ═══════════════════════════════════════════════════════════════
-- 004_drift_anchor_baseline.sql — 认知漂移监测·锚点基线
--
-- 用途：建立"AI 对项目认知"的测量基准（类比测量仪器的基准电压源）。
--       锚点 = 项目事实（架构/端口/配置/数据/约束），全部来自
--       系统血谱与代码实锤（每条 source 标注出处行号），禁止凭记忆编写。
--
-- 维护纪律：
--   · 血谱校准（最后校准日期更新）后，如需新增/修正锚点 → 追加 INSERT（version+1）
--   · 敏感值（密码/Token）禁止落库 —— 敏感锚点只登记"键名存在"事实
--   · 本迁移与 002_chemical_knowledge_graph.sql 同属"官方铜牌"，部署期 psql 执行
--
-- 基线锚点版本：v1（血谱最后校准 2026-08-01）
-- 执行方式：psql -f db/migrations/004_drift_anchor_baseline.sql
-- ═══════════════════════════════════════════════════════════════

-- ── 锚点表（幂等建表，重复执行不炸） ──
CREATE TABLE IF NOT EXISTS drift_anchors (
  id SERIAL PRIMARY KEY,
  domain VARCHAR(32) NOT NULL,        -- 分域: architecture/port/config/data/constraint
  entity_type VARCHAR(32) NOT NULL,   -- 实体类型: component/table/port/path/key/rule/sequence
  entity_key VARCHAR(255) NOT NULL,   -- 实体名（AI 输出中可被断言匹配的键）
  canonical_value TEXT NOT NULL,      -- 基准值（敏感锚点只写语义描述，不写真实值）
  value_hash VARCHAR(64),             -- 敏感锚点的 SHA-256（预留，当前不用）
  severity SMALLINT NOT NULL DEFAULT 1,  -- 0=参考 1=重要 2=结构级(错则架构认知崩坏)
  version INT NOT NULL DEFAULT 1,     -- 锚点版本（血谱校准后递增）
  source VARCHAR(255),                -- 出处（文档/代码 + 行号），证据链
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (entity_key, version)
);

-- ─────────────────────────────────────────────
-- 种子锚点 v1（50 条，来源全部实锤）
-- ─────────────────────────────────────────────

-- ═══ 架构域 architecture（25 条） ═══
INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, severity, version, source) VALUES
('architecture','component','前端器官','agent1-web: Vue 3 + TypeScript, Vite 开发服务器',2,1,'系统血谱.md L39'),
('architecture','component','API器官','Agent1.Api: ASP.NET Core Web API, JWT 认证, 15 个 Controller',2,1,'系统血谱.md L40'),
('architecture','component','核心引擎','Agent1: .NET 控制台/类库, Semantic Kernel 编排 LLM 推理',2,1,'系统血谱.md L41'),
('architecture','storage','主存储','PostgreSQL: 知识文档元数据/分块向量/知识图谱/审计日志',2,1,'系统血谱.md L47'),
('architecture','storage','兜底存储','SQLite: Data/chemical_substances.db 单文件',1,1,'系统血谱.md L48'),
('architecture','storage','进程内存','BM25/向量检索索引/知识图谱缓存/配置对象',1,1,'系统血谱.md L49'),
('architecture','path','知识库五子目录','knowledgebase/: 国标/化工专业条例/园区规则/历史案例/H166',1,1,'系统血谱.md L145'),
('architecture','table','动脉A双表','knowledge_documents + knowledge_chunks(分块向量,外键关联)',2,1,'系统血谱.md L170-171'),
('architecture','port','向量嵌入服务','nomic-embed 于 :8081, 启动参数 -b 2048 -ub 2048',1,1,'系统血谱.md L174-175'),
('architecture','table','图谱五张表','substances/aliases/禁忌边/safety_distances/regulation_versions',2,1,'系统血谱.md L201-206'),
('architecture','component','图谱门面','ChemicalSubstanceDatabase 81 行纯转发, 统一对外接口',2,1,'系统血谱.md L212'),
('architecture','rule','图谱无兜底','PG 不可用→启动失败, 有意设计(官方铜牌不可编造)',2,1,'系统血谱.md L220'),
('architecture','rule','图谱命名陷阱','ChemicalSubstanceDatabase 名为 Database 实为 PG 门面, 非独立数据库',2,1,'系统血谱.md L221'),
('architecture','constraint','SQLite种子规模','35 物质/21 距离/20 配伍/7 法规版本',1,1,'系统血谱.md L234'),
('architecture','component','配置唯一入口','Config/AppConfig.cs 全系统唯一配置入口',2,1,'系统血谱.md L262'),
('architecture','sequence','三级降级顺序','SQLite(Level1)→PG图谱(Level2)→硬编码字典(Level3)→兜底话术',2,1,'系统血谱.md L305-321'),
('architecture','rule','降级反直觉','SQLite 排 Level1, PG 排 Level2(SQLite 含 clause 条款原文字段更全)',2,1,'系统血谱.md L324'),
('architecture','sequence','出口闸门链','FC违约→FactExtractor→OutputSanitizer→FactAssembler→ResponseMerger',2,1,'系统血谱.md L338-353'),
('architecture','rule','双通道末端验证','API路径 ConclusionVerifier 法规验证 / CLI路径 OutputValidator 库白名单',1,1,'系统血谱.md L355-357'),
('architecture','rule','缓存TTL分级','RAG/数据库命中10分钟, 字典命中5分钟, 兜底0(不缓存)',1,1,'系统血谱.md L380-382'),
('architecture','rule','FC策略声明','调用方显式声明: 默认Required / 评测Auto / HyDE反思None',2,1,'系统血谱.md L396-398'),
('architecture','constraint','评测工具裁剪','info_query→6个工具 / compliance_judgment→5个工具',1,1,'系统血谱.md L406'),
('architecture','sequence','长任务模式','POST 202 + scanId → 后台执行 → 前端轮询状态',1,1,'系统血谱.md L420-432'),
('architecture','rule','双通道管线','API路径解耦管线 / CLI路径 6步线性流水线',1,1,'系统血谱.md L126-127'),
('architecture','rule','FC违约闸门契约','toolCalls==0 → 丢弃LLM输出走BuildNoResult, 依赖FC=Required契约',2,1,'系统血谱.md L339-341');

-- ═══ 端口域 port（7 条） ═══
INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, severity, version, source) VALUES
('port','port','API监听端口','ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000',2,1,'Agent1.Api/Program.cs L42'),
('port','port','Vite代理目标','vite.config.ts 默认 http://127.0.0.1:15000(SSH隧道); .env 实际 localhost:15001; .env.mock 127.0.0.1:19999',2,1,'vite.config.ts L11 + agent1-web/.env L8 [校准: 血谱L81写5001已过时]'),
('port','port','embedding端口','8081 (nomic-embed)',2,1,'系统血谱.md L174'),
('port','port','多模态视觉端口','8083 (qwen2.5-vl, llama-server --mmproj 实例, 与Reranker分离)',2,1,'MultimodalService.cs L20 + AppConfig.cs L198'),
('port','port','Reranker端口','8082 (bge-reranker-v2-m3, /rerank)',2,1,'AppConfig.cs L394'),
('port','port','PostgreSQL端口','5432',1,1,'DatabaseIntegrationTests.cs L40'),
('port','port','Seq端口','5341',1,1,'Agent1.Api/Program.cs L97');

-- ═══ 配置域 config（7 条） ═══
INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, severity, version, source) VALUES
('config','key','敏感/非敏感分离','.env 存敏感(密钥/密码), appsettings.json 存非敏感(端口/路径/模型参数)',2,1,'系统血谱.md L257-259'),
('config','key','JWT_KEY','由环境变量提供, 生产强制设置; 值敏感不落库',2,1,'Agent1.Api/Program.cs L211'),
('config','key','AUTH_ACCOUNTS_JSON','.env 中的认证账号 JSON; 值敏感不落库',1,1,'同源拷贝联动手册.md L110'),
('config','key','CORS_ORIGINS','默认 http://localhost:3000,http://localhost:5173',1,1,'Agent1.Api/Program.cs L279'),
('config','key','OTEL导出端点','默认 http://localhost:4317',1,1,'Agent1.Api/Program.cs L296'),
('config','key','quality-rules.json','R001 兜底禁写缓存 / R002 低质量缓存不得直接作答',1,1,'系统血谱.md L265-272'),
('config','key','VITE_PROXY_TARGET','Vite 代理目标环境变量(非Mock模式)',1,1,'vite.config.ts L11');

-- ═══ 数据域 data（5 条） ═══
INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, severity, version, source) VALUES
('data','table','审计日志表','audit_logs + chain_hash SHA256 链式哈希(防篡改)',2,1,'IDatabaseService.cs L70-78'),
('data','path','file_tracker.json','增量加载记录文件 mtime, 只处理变更文件',1,1,'系统血谱.md L185'),
('data','constraint','评测集','Data/ComplianceEvalSet.json v1.1 实际 15 条用例',2,1,'Data/ComplianceEvalSet.json [校准: 64条为过时记载]'),
('data','path','盲测集','Data/ComplianceBlindEvalSet.json 独立验证',1,1,'系统血谱.md L275'),
('data','constraint','缓存预热来源','评测集占位 50 条热点查询预热缓存',1,1,'Agent1.Api/Program.cs L357');

-- ═══ 约束域 constraint（6 条） ═══
INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, severity, version, source) VALUES
('constraint','rule','乱码闸门','GarbledTextDetector 三规则确定性拒收, 乱码块不准入库',2,1,'系统血谱.md L166'),
('constraint','rule','GB白名单三源并集','PG图谱 regulation_versions ∪ SQLite种子 ∪ 硬编码字典',2,1,'系统血谱.md L357'),
('constraint','rule','死循环检测','三维: 复读同一句话/符号喷射/长度超限',1,1,'系统血谱.md L402'),
('constraint','rule','结论判定优先级','三级: 结构化标签→法规编号匹配→关键词回退',1,1,'EvalEngine.cs L1080-1082'),
('constraint','rule','快速模式','分块 count>0 跳过全量扫描直接充血',1,1,'系统血谱.md L185'),
('constraint','rule','增量唯一约束','source_path UNIQUE, 并发增量返回 409',1,1,'系统血谱.md L170 + Agent1.Api/Program.cs L549-553');

-- ── 自检：期望 50 条 ──
-- SELECT version, domain, count(*) FROM drift_anchors GROUP BY version, domain ORDER BY version, domain;
