# chemical_safety_distances 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码读取路径验证（结构→数据→代码，三证齐备）
> **用途**：安全距离规则表——"甲类仓库和配电室间距多少米"防火间距数据底座（GB 50160/GB 50016 量化表）

---

## 第 0 步：先立 B

**安全距离规则表**：facility_pair（设施对文本）→ min_distance_m（最小间距）→ regulation_ref（法规依据）。查询入口 GetSafetyDistance（L367-375）：**双向 contains 模糊匹配 + FirstOrDefault 取第一条命中**。

---

## 第 1 步：结构（裸表）

| 字段 | 类型 | 备注 |
|------|------|------|
| id | integer | SERIAL PK |
| facility_pair | varchar(100) | NOT NULL，无 UNIQUE |
| min_distance_m | double precision | NOT NULL |
| regulation_ref | varchar(100) | DEFAULT '' |

**约束**：仅 pkey——无 UNIQUE、无索引、无 FK（与 incompatible_categories 同款裸表）。

---

## 第 2 步：数据（20 行，全绿）

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 20 | — |
| 空值/负数 | pair 空 0、距离空 0、负数 0 | ✅ |
| regulation_ref | **20/20 全有**（对照禁忌矩阵 #28 的 47.6% 空——本表引用最完整） | ✅ |
| 重复 | 0 | ✅ |
| 数值分布 | 12~200m，平均 32.1m | 200m 异常值 |
| 文本格式 | 20 个唯一 "A-B" 配对 | 格式统一 ✅ |

**全量清单**：储罐-储罐 15m / 储罐-建筑 25m / 液化烃储罐-储罐 20m / 液化烃储罐-办公楼 35m / 甲类仓库-建筑 20m / 甲类仓库-办公楼 30m / 氯气储存区-居住区 **200m**（重大危险源等级）……

---

## 第 3 步：三层考古（落差分析）

### ⚠️ 落差 1（P2，#31）：contains 模糊匹配 → 泛化遮蔽特化 + 顺序敏感

**代码**（L367-375）：
```csharp
return _safetyDistances.FirstOrDefault(s =>
    s.FacilityPair.Contains(key, StringComparison.OrdinalIgnoreCase)
    || key.Contains(s.FacilityPair, StringComparison.OrdinalIgnoreCase));
```

**缺陷 A：泛化遮蔽特化**。表里同时存在泛化条目"储罐-建筑"（25m，id=2）与特化条目"液化烃储罐-办公楼"（35m，id=17）。用户问"液化烃储罐和建筑多少米"（key="液化烃储罐-建筑"）：
- `key.Contains("储罐-建筑")` = true（子串命中）→ **返回 25m**
- 但液化烃储罐的实际规范距离是 35m（特化条目在 id=17，泛化条目 id=2 先命中，FirstOrDefault 短路）→ **答案被泛化条目遮蔽，少了 10m**

**缺陷 B：词序敏感**。用户说"A 和 B 的距离"，若 key 词序与表条目相反（"储罐-液化烃储罐" vs 表存"液化烃储罐-储罐"），双向 contains 均 false → 查不到 → 降级 LLM。

**缺陷 C：同款词汇表问题**。与 #27 死规则同模式——文本 contains 匹配对措辞零容错（"甲类仓库"vs"甲类厂房"、"厂区围墙"vs"厂区边界"）。

**修复方案**（待讨论）：方案 A：精确匹配优先，模糊降级（先 Equals 再 contains）；方案 B：特化条目优先（按文本长度倒序排序）；方案 C：facility_pair 拆结构化列（facility_a_type/facility_b_type）+ 精确索引

**核销验证 SQL**（修复后执行）：需代码级验证（单元测试：key="液化烃储罐-建筑"应返回 35m 而非 25m）

### 落差 2（P3，#32）：裸表（无 UNIQUE/索引）

facility_pair 无 UNIQUE（种子重复会双写）；无任何查询索引（启动全量加载内存，索引无实害，但与家族裸表通病一致）。

### 落差 3（P3，#33）：覆盖稀疏 + 量级混乱

20 条 vs GB 50160/50016 防火间距全表（储罐类型×容量×介质×周边设施，几百组合）；"氯气储存区-居住区"200m（依据重大危险源等级）与 12~35m 通用条目混存——特殊规则与通用规则未分层。

---

## 第 4 步：一句话总结

> **安全距离表是"数据最干净、查询最危险"的表：20 条数据 20/20 法规引用齐全、零空零负零重复，但 GetSafetyDistance 的 contains 模糊匹配 + FirstOrDefault 会把泛化条目（储罐-建筑 25m）优先于特化条目（液化烃储罐-办公楼 35m）返回——用户问"液化烃储罐离建筑多远"会少答 10 米。数据对、查询错。**

---

## 精读 SQL 速用卡

```sql
-- ── 全量清单 ──
SELECT id, facility_pair, min_distance_m, regulation_ref
FROM chemical_safety_distances ORDER BY id;

-- ── 质量检查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE facility_pair IS NULL OR facility_pair='') AS pair空,
       count(*) FILTER (WHERE min_distance_m IS NULL OR min_distance_m < 0) AS 距离异常,
       count(*) FILTER (WHERE regulation_ref IS NULL OR regulation_ref='') AS 法规空
FROM chemical_safety_distances;

-- ── 泛化/特化并存检查（子串遮蔽候选）──
SELECT a.facility_pair AS 泛化, b.facility_pair AS 特化
FROM chemical_safety_distances a
JOIN chemical_safety_distances b
  ON b.facility_pair LIKE '%' || a.facility_pair || '%' AND a.facility_pair <> b.facility_pair;
-- 预期：>0 即存在遮蔽风险（泛化条目会先命中 contains）
```
