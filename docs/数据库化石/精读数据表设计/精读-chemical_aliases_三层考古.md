# chemical_aliases 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 迁移脚本溯源（结构→数据→代码，三证齐备）
> **用途**：危化品"别名翻译器"——口语/俗称 → 标准名映射（chemical_substances 直连子表）

---

## 第 0 步：先立 B

物质口语/俗称 → 标准名的映射：用户说"液氨""烧碱""双氧水"，系统靠它还原到"氨""氢氧化钠""过氧化氢"。查询链路：Lookup → `_aliasIndex` 反向命中 → 标准物质 → 返回属性。

- **类比**：字典的"参见条目"——查到俗称，指向正条
- **与 chemical_substances**：N:1（substance_id FK ON DELETE CASCADE）

---

## 第 1 步：结构（3 字段）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_aliases'
ORDER BY ordinal_position;
```

**实测**（2026-08-13 L0）：

| 序号 | 字段 | 类型 | 备注 |
|------|------|------|------|
| 1 | id | integer | SERIAL PK |
| 2 | substance_id | integer | FK → chemical_substances(id) ON DELETE CASCADE |
| 3 | alias_text | varchar(100) | NOT NULL，**UNIQUE(substance_id, alias_text)** |

**约束/索引**（002 家族最完备）：pkey + 复合 UNIQUE（物质内别名唯一）+ idx_chemical_aliases_text（按别名查询入口）。

```sql
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='chemical_aliases'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='chemical_aliases' ORDER BY indexname;
```

---

## 第 2 步：数据

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 83 | — |
| 别名覆盖 | 35/35 种物质全覆盖（1 个×4 / 2 个×16 / 3 个×13 / 4 个×2），平均 2.4 个/物质 | ✅ |
| 无别名物质 | 0 | ✅ |
| 质量 | 首尾空格 0、长度>20 0、**长度<2 仅 1 条（"氢"→氢气，合理）**、含英文 7 条（行业缩写） | ✅ |
| 孤儿/歧义 | 0 / 0（上轮已验证） | ✅ |

**全量别名抽样（83 条）**：纯苯/安息油（苯）、木醇/木精（甲醇）、酒精/火酒（乙醇）、双氧水（过氧化氢）、烧碱/火碱/苛性钠/固碱（氢氧化钠）、福尔马林（甲醛）、氯仿/哥罗仿（三氯甲烷）、甘油（丙三醇）、山奈（氰化钠）、电石气（乙炔）——均为真实口语俗称，质量高。

---

## 第 3 步：三层考古（写入路径 + 落差分析）

### 3.1 数据源写入路径全景图

```
002_chemical_knowledge_graph.sql 种子（83 条，随 35 种物质导入）
ChemicalKnowledgeGraph.AddSubstance → 别名 INSERT ... ON CONFLICT DO NOTHING（L458）
   → 复合 UNIQUE 存在 → 幂等真正生效 ✅
AddAlias（L494）→ 运行时补别名（物质不存在时 throw）
删除：FK ON DELETE CASCADE（删物质自动清别名）
```

### 3.2 落差分析

#### 落差 1（P3，#24）：2 条种子瑕疵别名

- **"丙三醇" → 丙三醇**：别名与标准名完全相同（002 种子把标准名也写进别名，冗余）
- **"氧气瓶" → 氧气**：容器名当物质别名（"氧气瓶"是设备不是化学品；查询"氧气瓶怎么存"会命中氧气——语义错但实际无害）

**影响**：低。冗余别名浪费 1 行；"氧气瓶"命中反而提供便利。

**修复方案**（待讨论）：002 种子删除"丙三醇"自指别名；"氧气瓶"可保留（便利性）或删除（严格性）。

**核销验证 SQL**（修复后执行）：
```sql
SELECT a.alias_text FROM chemical_aliases a
JOIN chemical_substances s ON s.id = a.substance_id
WHERE a.alias_text = s.name;
-- 预期：0 行（无自指别名）
```

### 3.3 架构观察（002 家族约束对比，后续子表伏笔）

| 表 | UNIQUE | 查询索引 | 幂等机制 |
|----|--------|----------|----------|
| chemical_aliases | ✅ (substance_id, alias_text) | ✅ alias_text | ✅ ON CONFLICT 有效 |
| chemical_hazard_categories | ❌ | 待查 | ❌ |
| chemical_incompatible_categories | ❌ | 待查 | ❌ |
| chemical_incompatibilities | ❌ (a,b) | a/b 各一 | ❌ |

---

## 第 4 步：一句话总结

> **chemical_aliases 是 002 家族最健康的子表：83 个别名、35 种物质全覆盖、约束索引幂等三全（ON CONFLICT DO NOTHING 真正生效）、零孤儿零歧义——只有 2 条种子瑕疵（"丙三醇"自指别名、"氧气瓶"容器名），属于可以顺手修掉的 P3 小伤。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 字段结构 ──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'chemical_aliases'
ORDER BY ordinal_position;

-- ── [2] 约束 + 索引 ──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='chemical_aliases'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='chemical_aliases' ORDER BY indexname;

-- ── [3] 别名覆盖（无别名物质）──
SELECT s.id, s.name FROM chemical_substances s
LEFT JOIN chemical_aliases a ON a.substance_id = s.id
WHERE a.id IS NULL;  -- 预期 0 行

-- ── [4] 自指别名检查 ──
SELECT a.alias_text FROM chemical_aliases a
JOIN chemical_substances s ON s.id = a.substance_id
WHERE a.alias_text = s.name;  -- 预期 0 行

-- ── [5] 别名质量（空格/长度）──
SELECT count(*) FILTER (WHERE alias_text <> btrim(alias_text)) AS 含空格,
       count(*) FILTER (WHERE length(alias_text) < 2) AS 超短
FROM chemical_aliases;
```
