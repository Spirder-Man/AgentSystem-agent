# long_term_memories 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：跨会话长期记忆库——AI 记忆系统的心脏（故事线 A 终点站）

---

## 第 0 步：先立 B

AI 的"私人笔记本"——跨会话持久化记忆，与 knowledge_*（公共法规知识）严格分工：

```
Record 管道：对话 → LLM事实提取(FactExtractor) → 向量化(nomic-embed 768维) → pgvector
Retrieve 管道：Query → 向量化 → pgvector 语义检索 → 重排序 → 注入上下文
生命周期：写入 → 重要度(LLM初始评估) → 命中强化(+0.02/次) → 冲突停用(软删除)
```

- **类比**：knowledge_* 是图书馆（公共知识），long_term_memories 是私人笔记本（个体记忆）
- **关联**：source_session_id → sessions（会话溯源）；MemoryCoordinator 检索时按 user_id 过滤

---

## 第 1 步：结构（字段骨架，13 字段）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'long_term_memories'
ORDER BY ordinal_position;
```

**实测**（2026-08-13 L0）：

| 序号 | 字段 | 类型 | 可空 | 备注 |
|------|------|------|------|------|
| 1 | id | uuid | NO | 应用层 Guid.NewGuid() 生成（非数据库序列） |
| 2 | user_id | varchar(100) | NO | ⚠️ 实测 1157/1157 全空 |
| 3 | memory_type | varchar(50) | NO | user_preference / chemical_fact / compliance_experience / regulation_ref |
| 4 | content | text | NO | ⚠️ 51% <20 字符 |
| 5 | embedding | vector(768) | YES | nomic-embed-text |
| 6 | source_session_id | uuid | YES | ⚠️ 全部孤儿引用（sessions 表 0 行） |
| 7 | source_turn_index | int | YES | 默认 0 |
| 8 | importance | float8 | YES | 默认 0.5，LLM 评估 + 命中 +0.02 |
| 9 | hit_count | int | YES | 默认 0 |
| 10 | last_hit_at | timestamptz | YES | — |
| 11 | is_active | boolean | YES | 默认 true，软删除标记 |
| 12 | created_at | timestamptz | YES | 默认 CURRENT_TIMESTAMP |
| 13 | updated_at | timestamptz | YES | 默认 CURRENT_TIMESTAMP |

**索引**（6 个，比 knowledge_chunks 完备）：
- `idx_ltm_embedding_hnsw` — **HNSW vector_cosine_ops**（m=16, ef_construction=64）✅
- `idx_ltm_memory_type` / `idx_ltm_is_active` / `idx_ltm_user_active` / `idx_ltm_user_id` — btree
- 约束仅 `long_term_memories_pkey`（无 UNIQUE、无 CHECK）

---

## 第 2 步：数据（全量分组统计）

**查询 SQL**：
```sql
-- 行数 + 时间范围
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM long_term_memories;
-- 类型/活跃/用户分布
SELECT memory_type, count(*) FROM long_term_memories GROUP BY 1 ORDER BY 2 DESC;
SELECT is_active, count(*) FROM long_term_memories GROUP BY 1 ORDER BY 2 DESC;
SELECT user_id, count(*) FROM long_term_memories GROUP BY 1 ORDER BY 2 DESC;
-- 重要度/命中/长度
SELECT round(avg(importance)::numeric, 3), count(*) FILTER (WHERE importance >= 0.8) ...
SELECT min(hit_count), max(hit_count), round(avg(hit_count), 2), count(*) FILTER (WHERE hit_count = 0) ...
SELECT min(length(content)), max(length(content)), round(avg(length(content)), 1), count(*) FILTER (WHERE length(content) < 20) ...
-- 重复检测
SELECT count(*) AS 总行数, count(DISTINCT content) AS 去重内容数, count(*) - count(DISTINCT content) AS 重复条数
FROM long_term_memories;
-- sessions 关联
SELECT count(DISTINCT lt.source_session_id), count(DISTINCT s.id),
       count(*) FILTER (WHERE lt.source_session_id IS NOT NULL AND s.id IS NULL)
FROM long_term_memories lt LEFT JOIN sessions s ON s.id = lt.source_session_id;
```

**实测**：

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数/时间 | 1157 条，2026-06-29 ~ 2026-07-29 | 7-30 重灌后无新增 |
| memory_type | regulation_ref 469 / compliance_experience 290 / chemical_fact 256 / user_preference 142 | 四类齐全 |
| **is_active** | **停用 1114 / 活跃 43（96.3% 停用）** | 记忆空心化 |
| **活跃创建日** | **43 条全部创建于 7-29 当天** | 前月记忆全部阵亡 |
| user_id | 1157/1157 全空 | 无用户归属 |
| importance | min 0.4 / max 1.0 / avg 0.741 / >=0.8 有 495 / <0.5 有 121 | 分布合理 |
| hit_count | max 48 / avg 1.34 / 零命中 720（62%） | 写入多命中少 |
| 内容长度 | avg 21.8 / **<20 字符 591 条（51%）** / >200 有 0 | 记忆碎片化 |
| 更新 | 被更新过 1135/1157（98%） | 命中/停用高频 |
| **重复** | **总 1157 / 去重 687 / 重复 470（40.6%）** | 无去重门禁 |
| **sessions** | 引用的会话 350 个 / sessions 表 0 行 / 孤儿 1157 条 | 出处不可追溯 |

---

## 第 3 步：三层考古（写入路径 + 落差分析）

### 3.1 数据源写入路径全景图

```
RecordAsync（LongTermMemoryService）→ LLM FactExtractor 提取事实
  → GenerateEmbeddingAsync（nomic-embed 768 维，失败降级 null）
  → AddLongTermMemoryAsync（直接 INSERT，无任何幂等/去重检查）
  → ResolveConflictsAsync（事后补救：DeactivateConflictingMemoriesAsync 停用"语义相似"旧记忆）
Retrieve：MemoryCoordinator → ExpandQueryWithAliases → RetrieveAsync(topK=3) → 注入上下文
命中强化：RecordHitAsync → hit_count+1, importance+0.02（DatabaseService L1637-1643）
```

### 3.2 落差分析

#### 落差 1：记忆空心化——冲突停用机制级联清空（P1，#17）

**现象**：96.3%（1114/1157）记忆被停用；687 个唯一内容中 **648 个全部停用，仅 39 个含活跃版本**；活跃 43 条全部创建于 7-29 当天。

**补查证据**：
- [A] 停用与写入**逐日同步**（7-20 写 458 停 467、7-24 写 189 停 196……）→ 常态机制非批量脚本
- [C] 活跃记忆创建日全部 = 7-29 → 6-29~7-28 写的 1087 条全阵亡

**根因**：ResolveConflictsAsync → DeactivateConflictingMemoriesAsync 语义相似度阈值过宽——每条新记忆写入都把大量旧记忆判为"冲突"停掉，级联清空。且无"新记忆确实更好"的校验。

**影响**：一个月沉淀的事实点 94.3% 阵亡；RetrieveAsync 实际检索范围只剩 43 条；记忆系统名存实亡。

#### 落差 2：重复记忆泛滥——RecordAsync 无去重门禁（P1，#18）

**现象**：470 条重复（40.6%）："甲类仓库" **99 条**（99 个不同写入时刻，跨 6-29~7-29）、"GB 50140-2005" 56 条、"GB 50160 §5.2.1" 35 条。

**补查证据**：
- [B] "甲类仓库" 99 distinct created_at → 逐次写入非批量导入
- [E] 冲突解析未收敛：99 条停 97 留 **2 条活跃**（上下文不同逃过停用）
- [F] 碎片化：同一法规多种粒度（"GB 50140-2005" 56 + "GB 50140-2005《建筑灭火器配置验收及检查规范》" 30 = 86 条）

**根因**：RecordAsync 直接 INSERT 无幂等检查（对比 knowledge_chunks 有 source_path UNIQUE 文档级幂等）；FactExtractor 无归一化；ResolveConflictsAsync 是"事后停用"不是"事前防重"。

**影响**：表体积膨胀 40%；语义检索命中重复结果；重要度/命中统计失真。

#### 落差 3：sessions 表 0 行——记忆出处全部孤儿（P2，#19）

**现象**：1157 条 source_session_id 全非空，但 350 个不同 session_id 在 sessions 表均不存在（sessions 表存在但 0 行）。

**根因**：会话持久化从未接线——短期记忆活在内存 ConcurrentDictionary（MemoryService._sessions），sessions 表"建了但没人写"。

**影响**：记忆不可追溯"哪次对话来的"；会话级隐私删除（记忆随会话删除）无法实现；sessions 表成为幽灵表。

#### 落差 4：user_id 100% 空（P3，#20）

**现象**：1157/1157 全空（模型默认 "default"）。

**影响**：多用户场景记忆串扰——A 用户检索可能命中 B 用户记忆；与审计日志"100% 匿名"呼应（匿名化治理抹除或从未写入）。

#### 落差 5：7-30 后记忆系统停摆（P2）

**现象**：最后写入 7-29 10:42；7-30 知识库重灌、8-11 整库替换后无新增。

**影响**：RecordAsync 是否仍在运行时触发需确认——若记忆只活在评测链路，正式对话的记忆功能实际未启用。

#### 落差 6：62% 记忆零命中（P3）

**现象**：720 条 hit_count=0（62%）；avg 1.34；max 48。

**影响**：写入多、命中少——大量记忆从未被检索使用（部分因停用不可检索，部分因内容碎片化不可命中）。

---

## 第 4 步：一句话总结

> **long_term_memories 是设计最完整、运行最惨烈的表：HNSW 索引、生命周期机制（重要度/命中/软删除）全有，但写入无去重门禁导致 470 条重复，冲突停用阈值过宽导致 94% 记忆被级联清空（1157→43），sessions 表从未写入使记忆出处全部孤儿化——AI 的"私人笔记本"一个月写满又自我格式化，最终只留下 43 条记忆。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 字段结构（含 vector 维度）──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'long_term_memories'
ORDER BY ordinal_position;

-- ── [2] 全量：行数 + 时间范围 ──
SELECT count(*) AS 总行数, min(created_at)::text AS 最早, max(created_at)::text AS 最晚
FROM long_term_memories;

-- ── [3] 核心健康度三查（活跃率/重复率/零命中率）──
SELECT is_active, count(*) FROM long_term_memories GROUP BY 1;  -- 预期活跃率 > 50%
SELECT count(*) AS 总行数, count(DISTINCT content) AS 去重内容数,
       count(*) - count(DISTINCT content) AS 重复条数
FROM long_term_memories;  -- 预期重复 = 0
SELECT count(*) FILTER (WHERE hit_count = 0) AS 零命中,
       round(avg(hit_count), 2) AS 平均命中 FROM long_term_memories;

-- ── [4] 停用分布（批量停用 vs 逐日停用）──
SELECT updated_at::date AS 停用日, count(*) FROM long_term_memories
WHERE is_active = false GROUP BY 1 ORDER BY 2 DESC;

-- ── [5] 活跃记忆创建日分布（空心化证据）──
SELECT created_at::date AS 创建日, count(*) FROM long_term_memories
WHERE is_active = true GROUP BY 1 ORDER BY 1;

-- ── [6] 重复 TOP5 ──
SELECT user_id, memory_type, content, count(*) AS 重复数
FROM long_term_memories GROUP BY user_id, memory_type, content
HAVING count(*) > 1 ORDER BY 重复数 DESC LIMIT 5;

-- ── [7] sessions 孤儿引用验证 ──
SELECT count(DISTINCT lt.source_session_id) AS 记忆引用的会话数,
       count(DISTINCT s.id) AS sessions表实际会话数,
       count(*) FILTER (WHERE lt.source_session_id IS NOT NULL AND s.id IS NULL) AS 孤儿数
FROM long_term_memories lt LEFT JOIN sessions s ON s.id = lt.source_session_id;

-- ── [8] 约束 + 索引（HNSW 向量索引确认）──
SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
WHERE connamespace='public'::regnamespace AND conrelid='long_term_memories'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename='long_term_memories' ORDER BY indexname;
```
