# search_logs 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码写入路径全仓 grep（结构→数据→代码，三证齐备）
> **用途**：检索日志表——RAG 检索的观测载体（设计意图：查询文本/结果数/耗时/数据源优先级）

---

## 第 0 步：先立 B

**检索日志表**：每次 RAG 检索的观测记录——query（查询文本）、results_count（结果数）、execution_time_ms（耗时）、source_priority（数据源优先级）。设计意图：检索质量分析（响应速度、召回效果、数据源分流统计）。

---

## 第 1 步：结构（6 字段，观测设计合理）

| 字段 | 类型 | 备注 |
|------|------|------|
| id | integer | SERIAL PK |
| query | text | NOT NULL（查询文本） |
| results_count | integer | 可空（结果数） |
| execution_time_ms | integer | 可空（耗时毫秒） |
| source_priority | varchar(50) | 可空（数据源优先级） |
| created_at | timestamptz | 默认 CURRENT_TIMESTAMP |

**约束/索引**：pkey + idx_search_logs_created_at（按时间查）——结构完备。

---

## 第 2 步：数据（0 行）

| 维度 | 结果 |
|------|------|
| 行数 | **0** |
| 其他分布 | 全空 |

---

## 第 3 步：三层考古（写入路径）

**全仓 grep 实锤**：
- 建表 DDL：DatabaseService L384-400（CreateSearchLogTableAsync）+ 启动调用（L95）✅
- 测试断言：DatabaseIntegrationTests L103（断言表存在）✅
- **写入代码：全项目 0 处 `INSERT INTO search_logs`、0 处 LogSearch/RecordSearch 方法** ❌

---

## 第 4 步：落差分析

### ⚠️ 落差 1（P3，#40）：search_logs 幽灵表——检索观测从未接线

**发现**：表建了（含索引）、启动时建表流程跑了、测试还断言表存在——但**没有任何代码往里面写**。检索性能观测（execution_time_ms/source_priority）设计出来了却从未落地。

**根因**：建表先行、写入未接——与 sessions（#19）同款"幽灵表模式"（设计在 DDL、实现留空）；检索链路（ChemicalRAG.VectorSearchAsync 等）没有埋点。

**影响**：
- 检索质量分析缺数据：无法回答"RAG 检索平均耗时多少""双数据源分流比例多少"（source_priority 字段明显是为双通道观测设计的）
- 与 #10（水印垃圾 50.9%）、#13（分类不一致）呼应——**如果 search_logs 接线，用户实际检索命中垃圾块的比例本可被观测**
- 等保/审计：检索行为日志缺失

**修复方案**（待讨论）：在 VectorSearchAsync 等检索入口埋点写 search_logs（query/results_count/execution_time_ms/source_priority）；或若观测价值已被 MetricsCollector 替代，则废弃此表（删表）

**核销验证 SQL**（修复后执行）：
```sql
SELECT count(*) FROM search_logs;
-- 预期：>0（埋点接线后随检索增长）
```

---

## 第 5 步：一句话总结

> **search_logs 是"设计完备、从未接线"的幽灵表：6 字段观测设计合理（耗时/结果数/数据源优先级是检索质量分析的标准维度）、建表+索引+测试断言全齐，但全项目零写入代码——检索链路 0 埋点，0 行数据。与 sessions 同款幽灵表模式，且它的缺位让 #10 水印垃圾块"实际命中率"永远无法被观测。**

---

## 精读 SQL 速用卡

```sql
-- ── 行数（预期长期为 0，埋点后增长）──
SELECT count(*) FROM search_logs;

-- ── 结构 ──
SELECT column_name, data_type, is_nullable FROM information_schema.columns
WHERE table_schema='public' AND table_name='search_logs' ORDER BY ordinal_position;
```
