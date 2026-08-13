# drift 漂移监测家族 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码写入路径溯源（结构→数据→代码，三证齐备）
> **用途**：认知漂移监测——"测量 AI 对项目认知"的基准电压源 + 测量记录（4 表：anchors / probe_templates / probes / details）
> **迁移**：004_drift_anchor_baseline.sql / 005_drift_probe_tables.sql / 006_drift_probe_templates.sql

---

## 第 0 步：先立 B

**认知漂移监测子系统**（Agent1/Services/DriftMonitor/，5 文件）：

```
drift_anchors（锚点基线）      ← 004 迁移，50 条"项目事实"基准（全部出自系统血谱+代码行号）
drift_probe_templates（探针）  ← 006 迁移，23 条"黄金问题"（问被测 AI"这个项目的 X 是什么？"）
drift_probes（测量批次）       ← 005 迁移，每次测量 = 抽取断言→比对锚点→漂移率
drift_details（断言明细）      ← 005 迁移，每条断言与基准的匹配结果（0/0.5/1）
```

类比：锚点=测量仪器的基准电压源；探针=定期校准动作；批次/明细=校准记录。用于检测"AI 对项目认知何时开始漂移"。

---

## 第 1 步：结构（4 表设计，全库最专业）

| 表 | 关键设计 | 评价 |
|----|----------|------|
| drift_anchors | UNIQUE(entity_key, version)、severity 三级（0/1/2 结构级）、source 出处行号、value_hash 预留 | ✅ 版本化锚点 |
| drift_probe_templates | probe_key UNIQUE、vessel 血管标记、anchor_key 锚定、enabled 开关 | ✅ 探针锚定锚点 |
| drift_probes | **UNIQUE(session_id, turn_no, trigger_type) 幂等键**、drift_score 加权漂移率、domain_breakdown JSONB、anchor_version 防"测量仪自漂移" | ✅ 幂等设计 |
| drift_details | FK 级联删除（ON DELETE CASCADE）、match 三值、probe_key 追溯（006 增列） | ✅ 级联追溯 |

对比 002 家族裸表通病：drift 家族 4 表**全部有 UNIQUE/索引/外键**，是全库约束设计标杆。

---

## 第 2 步：数据（全绿 + 两处小瑕疵）

| 表 | 行数 | 验证结果 |
|----|------|----------|
| drift_anchors | 50 | ✅ 分域均衡（架构25/配置7/端口7/约束6/数据5）；**source 出处 50/50 全有行号**；UNIQUE 0 重复；**敏感值检查 0 泄漏** |
| drift_probe_templates | 23 | ✅ 血管全覆盖（4 动脉+5 静脉+约束）；23/23 启用；**孤儿 anchor_key 0 条**；probe_key 唯一 23/23 |
| drift_probes | 4 | ⚠️ reply 3 批（e2e-demo）+ probe 1 批；id 序列 1,2,3,8（跳号） |
| drift_details | 8 | ✅ 外键 0 孤儿；match 分布 0×3/0.5×2/1×3；⚠️ **probe_key 空 7/8** |

**敏感值纪律实锤**：A4 检查 0 命中——JWT_KEY/AUTH_ACCOUNTS_JSON 锚点只登记"键名存在"事实不写值（004 迁移注释承诺兑现）。

---

## 第 3 步：三层考古（代码路径）

### 写入路径（DriftMonitor.SaveProbeAsync L249-310）

```csharp
// 批次 upsert：幂等键 (session_id, turn_no, trigger_type)
INSERT INTO drift_probes (...) VALUES (...)
ON CONFLICT (session_id, turn_no, trigger_type) DO UPDATE SET ... RETURNING id
// 明细重写：先删旧（级联）再插新——重放幂等
DELETE FROM drift_details WHERE probe_id = @p;
foreach (var m in matches) INSERT INTO drift_details (..., probe_key) ...
```

- **幂等机制合格**：同轮对话重放测量 → 覆盖更新不堆积（与 #17 记忆重复泛滥形成鲜明对比）
- **调度器**（DriftProbeScheduler L101）：drift_probe_templates 表不可用时"本轮跳过"——优雅降级
- **锚点注册表**（DriftAnchorRegistry L75）：`SELECT COALESCE(MAX(version),0)` 读取当前锚点版本

### 落差 1（#41，P3）：drift_details.probe_key 历史空 7/8

**发现**：006 迁移 `ALTER TABLE drift_details ADD COLUMN IF NOT EXISTS probe_key` 增列后，006 之前写入的 7 条明细无 probe_key——追溯断链（明细只能追到批次，追不到具体探针问题）。

**根因**：增列无历史回填（回填需按批次关联反查模板，成本高价值低）。

**修复方案**（待讨论）：不回填，新数据天然带 probe_key；或历史 7 条按 session 反查模板手工回填。

**核销验证 SQL**：`SELECT count(*) FROM drift_details WHERE probe_key IS NULL;` 预期：不再增长。

### 落差 2（#42，P3）：漂移监测运行量 demo 级，且抓到漂移无处置闭环

**发现**：
- 4 批测量/8 条明细全部来自 8-12 的 e2e-demo 会话——**生产运行量近零**（调度器未在真实对话中持续触发）
- **probe_a1（"知识库分块文本和向量存在哪两张表？"）drift_score = 1.0000**——被测 AI 完全未答出"动脉A双表"锚点（actual="未提及任何基准标记"）。监测系统**抓到了真实漂移**，但无任何处置记录（无告警、无跟进）
- id 序列跳号（1,2,3,8）非缺陷：ON CONFLICT upsert 每次尝试都消耗序列值，重放测量的自然结果

**根因**：监测子系统处于"建成未启用"状态——代码/表/种子齐备，但无生产触发（依赖主动调用或定时任务未开启）。

**修复方案**（待讨论）：启用 DriftProbeScheduler 定时轮询（探针 23 条黄金问题定期问被测 AI）；漂移率超阈值（如 >0.5）时告警/落审计。

**核销验证 SQL**：`SELECT count(*), max(created_at) FROM drift_probes;` 预期：批次随生产运行持续增长。

---

## 第 4 步：一句话总结

> **drift 家族是全库数据治理的标杆与遗憾并存体：设计四表全部 UNIQUE/外键/索引齐全、50 条锚点 100% 带出处行号且零敏感值泄漏、23 条探针零孤儿锚定、upsert+级联重写幂等规范——设计质量全库第一；但运行量仅 e2e-demo 4 批，probe_a1 已抓到"动脉A双表"漂移率 1.0 的真实认知漂移却无处置闭环，监测系统"建成未启用"（#41 probe_key 历史空 + #42 运行量 demo 级）。**

---

## 精读 SQL 速用卡

```sql
-- ── 锚点完整性 ──
SELECT domain, count(*), count(*) FILTER (WHERE source IS NULL) AS source空
FROM drift_anchors GROUP BY domain;

-- ── 探针孤儿检查 ──
SELECT t.probe_key, t.anchor_key FROM drift_probe_templates t
LEFT JOIN drift_anchors a ON a.entity_key=t.anchor_key WHERE a.entity_key IS NULL;

-- ── 漂移率趋势 ──
SELECT trigger_type, round(avg(drift_score)::numeric,4), max(created_at)
FROM drift_probes GROUP BY trigger_type;

-- ── 明细追溯断链 ──
SELECT count(*) FILTER (WHERE probe_key IS NULL) AS probe_key空 FROM drift_details;
```
