# chemical_substances 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码/迁移脚本溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：危化品知识图谱核心节点表——"苯怎么存"类确定性查询的事实底座

---

## 第 0 步：先立 B

危化品"档案字典"——每种危化品一行：CAS/UN 号、分子式、闪点/沸点/爆炸极限、GB18218 重大危险源临界量；配套：
- chemical_aliases（别名，液氨→氨）
- chemical_hazard_categories（GHS 危险类别，关联 GB 30000 分册）
- chemical_incompatible_categories（禁忌物）

**架构要点**：非"查询打库"模式——ChemicalKnowledgeGraph 启动时全量加载到内存图（_nodes/_nameIndex/_aliasIndex），查询走内存；DB 是持久化底座。消费方：ChemicalComplianceTools.LookupChemicalProperties（KernelFunction 工具）→ 内存图 Lookup → 别名还原。

**分工**：字典优先（确定性），RAG 兜底（knowledge_* 法规原文）。

---

## 第 1 步：结构（字段骨架，17 字段）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_substances'
ORDER BY ordinal_position;
```

**实测**（2026-08-13 L0）：

| 序号 | 字段 | 类型 | 可空 | 备注 |
|------|------|------|------|------|
| 1 | id | integer | NO | SERIAL |
| 2 | name | varchar(100) | NO | **UNIQUE**（物质级幂等） |
| 3 | name_en | varchar(100) | NO | 默认 '' |
| 4 | cas_number | varchar(30) | NO | 默认 ''，⚠️ 无索引 |
| 5 | un_number | varchar(10) | NO | 默认 '' |
| 6 | formula | varchar(50) | NO | 分子式 |
| 7 | physical_state | varchar(50) | NO | ⚠️ 11 种文本表述不统一 |
| 8 | flash_point_c | float8 | YES | null=不适用（气体/固体） |
| 9 | boiling_point_c | float8 | YES | — |
| 10 | explosive_lower | float8 | YES | 爆炸下限 %体积 |
| 11 | explosive_upper | float8 | YES | 爆炸上限 %体积 |
| 12 | auto_ignition_c | float8 | YES | 自燃温度 |
| 13 | relative_density | float8 | YES | 相对密度（水=1） |
| 14 | vapor_density | float8 | YES | 蒸气密度 |
| 15 | major_hazard_threshold_tons | float8 | YES | GB18218 临界量（0=非重大危险源） |
| 16 | created_at | timestamptz | NO | 默认 NOW() |
| 17 | updated_at | timestamptz | NO | 默认 NOW() |

**约束/索引**：pkey + name UNIQUE；仅 2 个索引（无 cas_number 索引）。

---

## 第 2 步：数据（全量分组统计）

**查询 SQL**：
```sql
-- 行数 + 时间范围
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM chemical_substances;
-- 物理状态分布 + 重大危险源分布
SELECT physical_state, count(*) FROM chemical_substances GROUP BY 1 ORDER BY 2 DESC;
SELECT (CASE WHEN major_hazard_threshold_tons > 0 THEN '重大危险源' ELSE '非重大' END), count(*)
FROM chemical_substances GROUP BY 1;
-- 空值全量排查
SELECT count(*) FILTER (WHERE cas_number IS NULL OR cas_number='') AS cas空,
       count(*) FILTER (WHERE flash_point_c IS NULL) AS 闪点空, ...
FROM chemical_substances;
-- 关联健康度（别名/危险类别/禁忌物孤儿验证）
SELECT count(*) FROM chemical_aliases a
LEFT JOIN chemical_substances s ON s.id = a.substance_id WHERE s.id IS NULL;
SELECT count(*) FROM chemical_hazard_categories hc
LEFT JOIN chemical_substances s ON s.id = hc.substance_id WHERE s.id IS NULL;
```

**实测**：

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数/时间 | 35 行，2026-07-28 10:33:28（同一秒） | 002 迁移单事务导入 |
| 物理状态 | 液体 16 / 固体 7 / 气体类 12（11 种文本） | ⚠️ 枚举不统一 |
| 重大危险源 | 27/35 有 GB18218 临界量 | — |
| 危险类别 | 20 类 GHS（皮肤腐蚀 19 / 急性毒性 16 / 易燃液体 10…）覆盖 20 个 GB 30000 分册 | — |
| 空值 | 名称/CAS/分子式/状态 0 空；**闪点空 21/35（60%）** | 液体仅 14 种有闪点 |
| 别名 | 83 个全唯一、**0 孤儿**、0 歧义 | 液氨→氨、氯气→氯 ✅ |
| 子表孤儿 | hazard_categories / incompatible_categories **0 孤儿** | FK 完整 ✅ |

---

## 第 3 步：三层考古（写入路径 + 时间线 + 落差分析）

### 3.1 数据源写入路径全景图

```
002_chemical_knowledge_graph.sql（迁移种子，35 条 INSERT + 别名/类别/禁忌物）→ 单事务导入
ChemicalKnowledgeGraph.AddSubstance（运行时写入，含内存同步 + PG 持久化，L444-484）
   → 实测：从未被运行时调用，表是只读档案
读取：ChemicalKnowledgeGraph.LoadFromDatabase（启动全量加载）→ 内存图 → Lookup/LookupByCas
消费：ChemicalComplianceTools.LookupChemicalProperties（KernelFunction 工具）
```

### 3.2 时间线还原

```
7-28 10:33：002 迁移执行 → 35 种物质 + 83 别名 + 危险类别 + 禁忌物（单事务，now() 同值）
8-11：本地库残留 chemical_aliases 空壳表（0 行/无约束/无索引）→ 002 重放时
      CREATE TABLE IF NOT EXISTS 跳过建表 → INSERT 种子失败
      → 003_cleanup_stale_chemical_aliases.sql 删除空壳（双人审批 + 备份 dump）
      → 002 可完整重放
8-11 整库替换：本表 35 行保留 ✅
当前：静态档案，AddSubstance 零调用
```

### 3.3 落差分析

#### 落差 1：覆盖缺口（P2，#21）

**现象**：35 种物质 vs GB18218 重点监管危化品 74 种——覆盖不足一半。

**细节**：核心物质齐备（苯/甲苯/二甲苯/甲醇/乙醇/丙酮/环氧乙烷/过氧化氢/硝酸/硝酸铵/高锰酸钾/氯/氨/硫化氢/乙炔/氢气/硫酸/盐酸/氢氧化钠/氢氟酸/乙酸/氰化钠/甲醛/苯乙烯/三氯甲烷/丙三醇/氯化氢/二氧化硫/氧气/硫磺/铝粉…），且别名兜底（液氨→氨、氯气→氯）；但磷化氢、光气、氰化氢、黄磷、液化石油气等常见重点监管物质缺失。

**影响**："查无此物"→ 降级 RAG/LLM，有幻觉风险（用户问"光气怎么存"会走 LLM 自由发挥）。

#### 落差 2：physical_state 文本不统一（P3，#22）

**现象**：11 种表述——"气体（压缩/液化）"vs"气体（加压液化）"同义不同写；"液体（甲醛溶液）""液体（氯化氢水溶液）"把物质名混入状态字段。无 CHECK 约束。

**影响**：按状态过滤/统计不精确（当前仅展示用途，危害低）。

#### 落差 3：cas_number 无索引（P3，#23）

**现象**：LookupByCas 是查询入口（ChemicalSubstanceDatabase.LookupByCas）但 cas_number 无索引。

**影响**：35 行量小无实害；扩展至数百种后全表扫描变慢。

#### 观察项（非问题）

- 表为静态只读档案（7-28 后零更新）：数据可靠但法规版本更新（GB18218 临界量调整）只能改迁移脚本，无运行时维护渠道。
- 闪点空 60%：气体/固体"不适用"合理；但液体中仅 14 种有闪点，闪点查询覆盖有限。

---

## 第 4 步：一句话总结

> **chemical_substances 是 5 张已精读表里最健康的一张：35 种物质、name 幂等、别名完备（液氨→氨）、关联零孤儿、数据专业可靠（苯闪点 -11°C、GB18218 临界量 50 吨）——是"确定性知识"的正面样板，与 knowledge_chunks 的水印垃圾形成鲜明对比；唯一短板是覆盖 35/74 种重点监管危化品，且表是静态只读档案（7-28 后零更新），扩展只能靠改迁移脚本。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_substances'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围（判断是否单事务导入）──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM chemical_substances;

-- ── [3] 物理状态分布（枚举统一性检查）──
SELECT physical_state, count(*) FROM chemical_substances GROUP BY 1 ORDER BY 2 DESC;

-- ── [4] 空值全量排查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE cas_number IS NULL OR cas_number='') AS cas空,
       count(*) FILTER (WHERE flash_point_c IS NULL) AS 闪点空,
       count(*) FILTER (WHERE boiling_point_c IS NULL) AS 沸点空
FROM chemical_substances;

-- ── [5] 约束 + 索引 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='chemical_substances'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='chemical_substances' ORDER BY indexname;

-- ── [6] 别名健康度（孤儿/歧义）──
SELECT count(*) FROM chemical_aliases a
LEFT JOIN chemical_substances s ON s.id = a.substance_id WHERE s.id IS NULL;  -- 预期 0
SELECT alias_text, count(DISTINCT substance_id) FROM chemical_aliases
GROUP BY alias_text HAVING count(DISTINCT substance_id) > 1;  -- 预期 0 行

-- ── [7] 子表孤儿验证 ──
SELECT 'hazard_categories' AS 表, count(*) AS 孤儿数
FROM chemical_hazard_categories hc LEFT JOIN chemical_substances s ON s.id = hc.substance_id
WHERE s.id IS NULL
UNION ALL
SELECT 'incompatible_categories', count(*)
FROM chemical_incompatible_categories ic LEFT JOIN chemical_substances s ON s.id = ic.substance_id
WHERE s.id IS NULL;
```
