# chemical_hazard_categories 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码加载路径验证（结构→数据→代码，三证齐备）
> **用途**：物质危险类别标注表——每物质 × GHS 危险类别的 N:N 标注（前端危险标签展示 + CheckCompatibility 类别级判定）

---

## 第 0 步：先立 B

**物质危险类别标注表**：物质 × GHS 危险类别（"苯"→易燃液体/致癌性/吸入危害）的多值标注。
- 用途 1：前端展示物质危险标签（MSDS 式）
- 用途 2：CheckCompatibility 类别级判定（L328-344）的匹配对象——**#27 死规则的根因所在**（它的 category 是 GHS 术语，而 incompatible_categories 写的是通俗词）
- 代码加载：L156-171 三字段全读入内存 HazardCategoryRef

---

## 第 1 步：结构

| 字段 | 类型 | 备注 |
|------|------|------|
| id | integer | SERIAL PK |
| substance_id | integer | FK → chemical_substances ON DELETE CASCADE |
| category | varchar(100) | GHS 危险类别 |
| gb_standard | varchar(30) | DEFAULT ''（GB 30000 系列编号） |
| sub_category | varchar(50) | DEFAULT ''（GHS 危险级别：类别1/1A/2/3） |

**约束**：仅 pkey + FK（无 UNIQUE(substance_id, category)，家族裸表通病；但数据干净无重复）。

---

## 第 2 步：数据（116 行，002 家族质量最高）

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 116 | — |
| 覆盖 | 34/35 种物质有标注（**丙三醇无标注——甘油无 GHS 分类，符合事实**） | ✅ |
| 每物质类别数 | 1~6 类，平均 3.3（3 类×12 种最多） | ✅ |
| 同物质同类别重复 | 0 | ✅ |
| **字段利用** | sub_category 116/116 有值、gb_standard 116/116 有值——**无幽灵字段** | ✅ |
| gb_standard 一致性 | 同类别绑定唯一 GB 编号（皮肤腐蚀/刺激=GB 30000.19 等，1:1 严格） | ✅ |
| sub_category 分级 | 规范 GHS 级别（苯：易燃液体/类别2、致癌性/类别1A；二甲苯：易燃液体/类别3） | ✅ |
| 孤儿 | 0 | ✅ |

**类别清单抽样**：氨=对水生环境危害/急性毒性/加压气体/皮肤腐蚀/易燃气体 ✓；高锰酸钾=氧化性固体 ✓；环氧乙烷=生殖细胞致突变性/致癌性 ✓——专业准确。

---

## 第 3 步：三层考古（落差分析）

### 落差 1（P3，#34）：丙三醇无类别标注（0 类）

**发现**：[7] 查无类别物质 = 丙三醇唯一一种。

**分析**：甘油（丙三醇）在 GHS 下确实无危险分类——**标注符合事实，不是错误**。但代码层影响：CheckCompatibility 的"同类兼容推断"（L347-349）依赖 aCats/bCats 非空，丙三醇参与比较时类别集为空 → 该分支失效（仅降级到精确边/类别级禁忌）。

**修复方案**（待讨论）：接受现状（正确标注）；或代码层对空类别集显式跳过（当前无异常，仅分支失效）。

### 落差 2（P3，#35）：裸表

无 UNIQUE(substance_id, category)（运行时 AddSubstance 重复写类别会重复行）；无 substance_id 索引。家族通病，当前 116 行无实害。

### 跨表呼应

**#27 死规则的根因就埋在这里**：本表 category 用 GHS 术语（"氧化性固体/液体/气体"），而 chemical_incompatible_categories.incompatible_with 用通俗词（"氧化剂"）——两表词汇表错位，contains 匹配 82% 规则永不触发。修复 #27 时本表是"对齐基准"（术语权威源）。

---

## 第 4 步：一句话总结

> **chemical_hazard_categories 是 002 家族数据质量最高的标注表：116 行 GHS 术语规范、gb_standard 1:1 绑定、sub_category 分级专业（1A/2/3）、三字段全有值零幽灵——唯一"缺陷"是丙三醇 0 类标注（甘油无 GHS 分类，符合事实），真正的包袱是它作为术语权威源却与禁忌表词汇表错位（#27 根因）。**

---

## 精读 SQL 速用卡

```sql
-- ── 无类别标注的物质 ──
SELECT s.name FROM chemical_substances s
LEFT JOIN chemical_hazard_categories hc ON hc.substance_id = s.id
WHERE hc.id IS NULL;

-- ── gb_standard 一致性 ──
SELECT category, count(DISTINCT gb_standard) AS 不同GB数
FROM chemical_hazard_categories
GROUP BY category HAVING count(DISTINCT gb_standard) > 1;

-- ── 字段利用（幽灵字段检查）──
SELECT count(*) FILTER (WHERE sub_category <> '') AS sub有值,
       count(*) FILTER (WHERE gb_standard <> '') AS gb有值,
       count(*) AS 总行数
FROM chemical_hazard_categories;
```
