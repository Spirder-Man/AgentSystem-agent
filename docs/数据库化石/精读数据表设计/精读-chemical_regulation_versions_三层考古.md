# chemical_regulation_versions 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码读取路径验证 + knowledge_documents 交叉验证（三证齐备）
> **用途**：法规版本档案表——GB 50160 等法规的"当前版本 + 全文有无 + 废止历史"登记簿

---

## 第 0 步：先立 B

**法规版本档案表**：regulation_number → 当前版本 / 有无全文 / 废止历史 / 变更说明。查询入口 GetRegulationVersion（L387-398）：NormalizeGbNumbers 规范化 + 双向 contains 模糊匹配 + FirstOrDefault。

- 用途：回答"XX 标准最新版本"、判断知识库法规是否过期
- 与 #28 溯源缺口呼应（regulation_ref 指向的规范本身是否有全文）

---

## 第 1 步：结构（7 字段，裸表）

| 字段 | 类型 | 备注 |
|------|------|------|
| id | integer | SERIAL PK |
| regulation_number | varchar(30) | NOT NULL，**无 UNIQUE** |
| title | varchar(200) | NOT NULL |
| current_version | varchar(20) | DEFAULT '' |
| has_full_text | boolean | DEFAULT false |
| deprecated_versions | text | 可空 |
| change_notes | text | 可空 |

**约束**：仅 pkey——家族裸表通病。

---

## 第 2 步：数据（8 行，质量全绿）

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 8 | — |
| 空值 | 版本空 0、编号空 0、标题空 0、说明空 0、废止空仅 3（合理：无废止历史的） | ✅ |
| 重复 | 编号 0、标题 0 | ✅ |
| 内容准确性 | GB 15603-2022（废止1995）、GB 30000.1-2024（废止2013）、GB 50160-2008（2018局部修订）——全部与现行国标一致 | ✅ |
| change_notes | 专业："2022版更新了禁忌物料配存表"、"与GHS第8修订版接轨" | ✅ |
| has_full_text | 4/8 true | — |

**has_full_text 交叉验证（vs knowledge_documents 实际覆盖）**：4 个 false（50160/50016/18218/617）在知识库中确实无文档；4 个 true（15603/30000/30000.1/30871）有文档——**标记 8/8 全部准确** ✅

---

## 第 3 步：三层考古（落差分析）

### ⚠️ 落差 1（P2，#36）：GB 30000 泛化编号遮蔽 GB 30000.1-2024

**代码**（L392-397）：双向 contains + FirstOrDefault 按加载序（表 id 序）。

表里 "GB 30000"（总纲，2013，id=2）排在 "GB 30000.1"（第 1 部分，**2024 修订**，id=3）之前。用户问"GB 30000.1 最新版本"：
- normalizedQuery = "gb30000.1"
- 第一候选 "GB 30000"→"gb30000"：`"gb30000.1".Contains("gb30000")` = true → **命中总纲，返回 2013**
- id=3 的 "GB 30000.1-2024" 被 FirstOrDefault 短路遮蔽 → **把 2024 修订说成 2013**

与 #31（安全距离"储罐-建筑"遮蔽"液化烃储罐-办公楼"）同款模式——**全库第三处 contains+FirstOrDefault 泛化遮蔽**（#27 死规则、#31 距离、#36 版本）。

**修复方案**（待讨论）：精确匹配优先（Equals/最长编号优先）；或按编号长度倒序排序。

**核销验证 SQL**（修复后执行）：需代码级单元测试——查询"GB 30000.1"应返回 2024 而非 2013。

### 落差 2（P3，#37）：裸表

regulation_number 无 UNIQUE（运行时重复登记会双写）。

### 落差 3（P3，#38）：档案 8 部、全文仅 3 部

登记 8 部法规，但知识库只有 3 部全文（GB 15603、GB 30000 系列、GB 30871）。GB 50160/50016/18218/JT/T 617 无原文——**合规回答引用这些规范条款（如 #28 的 regulation_ref="GB 50160"）时无原文可查**，溯源链条在源头断料。has_full_text 标记本身准确，问题是覆盖现状。

---

## 第 4 步：一句话总结

> **法规版本表是"数据最准、查询又踩坑"的表：8 行内容与现行国标完全一致（版本号/废止历史/变更说明全对）、has_full_text 标记 8/8 准确，但 GetRegulationVersion 的 contains+FirstOrDefault 让"GB 30000.1 最新版本"命中总纲条目返回 2013 而非 2024——全库第三处泛化遮蔽（#27/#31/#36 同款模式），且 8 部登记法规只有 3 部有全文入库。**

---

## 精读 SQL 速用卡

```sql
-- ── 全量清单 ──
SELECT regulation_number, current_version, has_full_text,
       deprecated_versions, change_notes
FROM chemical_regulation_versions ORDER BY id;

-- ── has_full_text vs 知识库实际覆盖 ──
SELECT file_name FROM knowledge_documents
WHERE file_name ~ '50160|50016|18218|617|30000|15603|30871';

-- ── 编号前缀遮蔽检查（短编号是长编号的前缀）──
SELECT a.regulation_number AS 短编号, b.regulation_number AS 长编号
FROM chemical_regulation_versions a
JOIN chemical_regulation_versions b
  ON b.regulation_number LIKE a.regulation_number || '%' AND a.id <> b.id;
-- 预期：>0 即存在 contains 遮蔽风险
```
