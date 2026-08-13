# audit_logs 三层精读报告

> **日期**：2026-08-12（2026-08-11 整库替换后首次精读）
> **方法**：数据库 L0 实测 + 代码溯源双线考古（结构→数据→代码，三证齐备）
> **用途**：本文件夹第一张"结构+数据+源码"三证齐备的精读样本，后续表精读参照此格式

---

## 第 0 步：先立 B

每次**重要操作**记一行账：谁、在哪、干了什么、从哪台电脑、什么时候 + 防篡改指纹。

- **类比**：监控室的录像带——平时没人看，出事（"谁删了数据？"）就回放对质
- **核心旅程覆盖**：合规查询（365）、记忆更新（597）、合规审核（85）、自查（25）、扫描（18）、应急（17）、法规审计（12）、工单（12）——5 条核心旅程一一对应

---

## 第 1 步：结构（字段骨架）

**查询 SQL**：
```sql
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'audit_logs'
ORDER BY ordinal_position;
```

**8 字段 → 4 组**：

| 序号 | 字段 | 类型 | 可空 | 默认值 | 分组 | 回答的问题 |
|------|------|------|------|--------|------|-----------|
| 1 | id | integer | **NO** | nextval 自增 | 身份组 | 第几条记录 |
| 2 | user_id | varchar(100) | YES | | 人物组 | 谁干的 |
| 3 | action | varchar(100) | **NO** | | 事件组 | 干了什么（主干） |
| 4 | module | varchar(100) | YES | | 事件组 | 在哪个功能 |
| 5 | detail | text | YES | | 事件组 | 细节补充 |
| 6 | ip_address | varchar(50) | YES | | 人物组 | 从哪台电脑 |
| 7 | chain_hash | text | YES | | 防篡改组 | 被改过吗 |
| 8 | created_at | timestamptz | YES | CURRENT_TIMESTAMP | 状态组 | 何时干的 |

**骨架故事**：`action` 是唯一必填内容字段，`user_id` 可空 = **"事件是账本主干，谁可以不知道，干了什么必须知道"**。

**约束 + 索引**（设计决策证据）：
- 约束：只有 `audit_logs_pkey`（主键）——**没有外键**，审计不被用户删除连坐
- 索引：idx_audit_logs_action / _created_at / _user_id —— 三个手动索引全在"过滤查询"字段上
- 序列：audit_logs_id_seq last_value = **1319** = max(id)，自增发号器

---

## 第 2 步：数据（真实行对照）

### 2.1 最新 5 条（设计←→数据逐列对照）

```sql
SELECT id, user_id, action, module, left(detail,60) AS detail前60字,
       ip_address, created_at::text AS created_at, left(chain_hash,64) AS chain_hash
FROM audit_logs ORDER BY id DESC LIMIT 5;
```

```
id    user_id    action           detail                                chain_hash
1319  admin      合规审核          查询: 苯和丙酮可以存放在同一库房吗？…    b93002760e0e5877…（64位）
1318  test-user  IntegrationTest  集成测试审计日志                        (空！)
1317  system     ChemicalCompliance 合规查询: 甲醇和硝酸…| TraceId=…      7f31c5e3da7f9d…
1316  (空)       记忆更新          会话: 5309064e | 工具: …| token:16     be6eee645151dd…
1315  (空)       记忆更新          会话: 5309064e | 工具: [无] | token:1223  4b8c51fe5d…
```

**逐行印证**：
- 1315/1316 无 user_id 照常记录 → "匿名操作也得留痕" ✅
- 1318 chain_hash 空 → 测试注入绕过哈希链 ⚠️
- module 五条全空 → 可疑信号 ⚠️
- detail 是 `\|` 分隔键值串 → text 自由格式印证：每类操作细节不同

### 2.2 全量分组统计（数据自己讲故事）

```sql
-- 时间范围
SELECT min(created_at)::text AS 最早, max(created_at)::text AS 最晚 FROM audit_logs;
-- action 全量分组统计
SELECT action, count(*) AS 行数 FROM audit_logs GROUP BY action ORDER BY 行数 DESC;
-- user_id 全量分组统计
SELECT COALESCE(NULLIF(user_id,''),'(空)') AS 用户, count(*) AS 行数
FROM audit_logs GROUP BY 1 ORDER BY 行数 DESC;
-- 哈希长度全量分组统计
SELECT length(chain_hash) AS 哈希长度, count(*) AS 行数
FROM audit_logs WHERE chain_hash IS NOT NULL AND chain_hash <> '' GROUP BY 1;
```

| 维度 | 实测 | 设计印证 |
|------|------|---------|
| 时间 | 2026-06-25 ~ 2026-07-30（一个月运营期） | 真实生产痕迹，非测试数据 |
| action | 记忆更新 597 / ChemicalCompliance 365 / 合规审核 85 / 测试 49 / 其余 253 | 5 条核心旅程全覆盖 ✅ |
| user_id | 597 空（记忆）/ system 406 / admin 112 / test-user 49 / default-user 25 | 多用户共存 |
| 哈希长度 | **全部 64 位** = SHA256（非 SHA-1） | SHA256 链式防篡改 ✅ |
| module | ⚠️ **1189/1189 全空** | 见落差 1 |
| ip_address | ⚠️ **1140 空 / 49 有值（全是 127.0.0.1 = 测试）** | 见落差 2 |
| 带 TraceId | 365 条 = ChemicalCompliance 365 条（严丝合缝） | AgentDialog 拼装 ✅ |
| 无哈希行 | **4 条**（id 1259/1260/1261/1318，全是 IntegrationTest） | 见落差 3 |
| 链头 | id=1, user=default-user, action=合规自查, hash=65df42b6… | GENESIS 起点 ✅ |

---

## 第 3 步：三层考古（决策 → 现象 → 数据源）

### 3.1 逐字段三层对照

| 字段 | 决策设计（为什么） | 数据现象（L0 实测） | 数据源脉络（代码证据） |
|------|------------------|-------------------|----------------------|
| `id` | SERIAL 自增，数据库发号 | 1~1319，有跳号 | [DatabaseService.cs#L363](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/DatabaseService.cs#L363) 建表 |
| `user_id` | 可空 = 允许匿名留痕 | 597 行空（**记忆更新 100% 匿名**）；system 406 / admin 112 / test-user 49 / default-user 25 | 调用方传什么是什么：[AgentDialog.cs#L247](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Dialog/AgentDialog.cs#L247) 写死 `"system"`；记忆模块不传 → 空 |
| `action` | **NOT NULL 必填** = 事件是账本主干 | 11 种 action，永不空 | 各业务模块写操作名：AgentDialog 写 `"ChemicalCompliance"` |
| `module` | 可空 = 预留"在哪个功能里干的" | ⚠️ **1189/1189 全空** | **设计残留**：[IAuditService.cs#L8](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/IAuditService.cs#L8) 与 [IDatabaseService.cs#L73](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/IDatabaseService.cs#L73) 接口**都没有 module 参数**，只有建表 SQL L366 有列——全链路无人传值 |
| `detail` | text 自由格式 = 每类操作细节不同 | `\|` 键值串；**TraceId 出现 365 次 = ChemicalCompliance 365 条，严丝合缝** | AgentDialog.cs L248 拼装（含 TraceId/工具数/安全警告数） |
| `ip_address` | 可空 = "从哪台电脑" | ⚠️ **1140 空 / 49 有值（全是 127.0.0.1 = 测试）** | **半实现字段**：[AuditService.cs#L75-76](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/AuditService.cs#L75) 注释明说"由调用方通过 details 传递"——接口有参数、生产调用点从没传 |
| `chain_hash` | text = SHA256 链式防篡改（锁体） | 1185 条 64 位十六进制；**4 条测试直插无哈希**；id=1 为 GENESIS 链头 | [AuditService.cs#L36-44](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/AuditService.cs#L36)：`SHA256(prevHash??"GENESIS" \| user \| op \| detail \| UTC微秒)` |
| `created_at` | timestamptz 默认 CURRENT_TIMESTAMP | 6-25 16:55 ~ 7-30 11:18 | 微秒归一化 + DB 默认值双保险 |

### 3.2 设计图纸 vs 实现现实（落差）

#### 落差 1：`module` 是彻头彻尾的"设计残留"

**现象**：1189 行全空。**源头**：建表 SQL 预留了列（[DatabaseService.cs#L366](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/DatabaseService.cs#L366)），但 `IAuditService.LogOperationAsync`、`IDatabaseService.AddAuditLogAsync` 两个接口的签名里**都没有 module 参数**，所有调用点（AgentDialog、巡检、记忆模块）也无从传值。→ **schema 超前于代码的化石**：当初设计时认为"需要记功能归属"，接口演进时被丢弃，表结构没跟着改。

#### 落差 2：`ip_address` 是"半实现字段"

**现象**：49 条有值全是测试写的 127.0.0.1（= IntegrationTest 恰好 49 条，一条不多一条不少）。**源头**：接口有 `ipAddress` 参数，但 AuditService 自己注释承认"由调用方通过 details 传递"——生产调用点（AgentDialog 等）没人传 → 审计的"从哪台电脑"能力实际是**空转的**。

#### 落差 3：4 条记录无哈希 = 哈希链上的"缺口"

**现象**：id 1259/1260/1261/1318，全部 `test-user / IntegrationTest`。**源头铁证**：[DatabaseIntegrationTests.cs#L236-241](file:///d:/桌面/agent/项目/Agent1/Agent1.Tests/DatabaseIntegrationTests.cs#L236) 测试直接调 `_db.AddAuditLogAsync(...)`（**绕过 AuditService**），chainHash 参数不传 = null → 直插无哈希。且 1318 夹在链中间（1317→1319 有哈希），1319 的哈希实际链在 1317 上——**1318 是链上的"断点"，验证时被 [VerifyIntegrityAsync L191](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/AuditService.cs#L191) `ChainHash != null` 条件跳过**，睁一只眼闭一只眼。

### 3.3 数据源写入路径全景图

```
路径 A（正常·1185 条）：业务模块 → IAuditService.LogOperationAsync → AuditService
   └─ ComputeChainHash(SHA256) 算哈希 → DatabaseService.AddAuditLogAsync → audit_logs
      ├─ AgentDialog.cs#L247     → ChemicalCompliance 365 条（system + TraceId）
      ├─ 记忆模块（匿名）        → 记忆更新 597 条
      ├─ 合规审核（admin）       → 85 条
      └─ 巡检/扫描/应急/工单/法规审计 → 其余 8 种 action

路径 B（测试直插·4 条）：DatabaseIntegrationTests.cs#L236-241
   └─ _db.AddAuditLogAsync（chainHash 不传 = null）→ 无哈希 ✅ 铁证

两个"幽灵字段"：module（建表有/接口无）· ip_address（接口有/调用点无）
```

### 3.4 底层机制：哈希链公式

```
chain_hash(n) = SHA256( chain_hash(n-1) ?? "GENESIS" | user_id | action | detail | UTC微秒时间 )
```

**验证方法**：id=1 的哈希 `65df42b6…` 可自己算：
```
input = "GENESIS|default-user|合规自查|<detail>|2026-06-25T08:55:03.628544Z"
SHA256(input) = 65df42b65684b0f291590e17a49ac98e59fe6ad5...  ← 对得上就是锁芯正确
```

> 东八区 16:55 → UTC 08:55，微秒归一化（[AuditService.cs#L37-44](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/Infrastructure/AuditService.cs#L37)）保证跨重启格式一致。

### 3.5 自我纠错记录

| 误判 | 纠正 | 教训 |
|------|------|------|
| 哈希长度 = "40 位 SHA-1"（抽样 `left(chain_hash,40)` 误导） | 全量分组统计：**全部 64 位 SHA256** | 抽样观察会骗人，全量分组统计才是 L0 |
| 无哈希 = 1 条（只看了 1318） | 全量分组统计：**4 条**（1259/1260/1261/1318） | 验证必须全量，不能只看"最新 N 条" |

---

## 第 4 步：时间地层剖面（数据记录的系统进化史）

```sql
SELECT action, min(created_at)::text AS 最早, max(created_at)::text AS 最晚, count(*) AS 行数
FROM audit_logs GROUP BY action ORDER BY 行数 DESC;
```

| 阶段 | action | 说明 |
|------|--------|------|
| 6-25 ~ 7-10 | 记忆更新/合规自查/法规审计/工单 | 早期核心能力上线 |
| 6-29 起 | ChemicalCompliance/EmergencyResponse | 合规问答 + 应急上线 |
| 7-14 起 | 合规审核（admin） | 人工审核流上线 |
| 7-17 起 | ComplianceScan/储存兼容性/危化品查询 | 扫描与查询能力 |
| 7-30 11:18 | 最后一条（合规审核） | 8-8 E2E 前最后一次写入 |

**数据库像地层，action 全量分组统计暴露了功能开发的先后节奏——这是设计文档里永远看不到的"当时的问题"。**

---

## 第 5 步：一句话总结

> **audit_logs 骨架讲"事件为主干、人物可匿名、防篡改上锁"，数据讲"module 被闲置、ip_address 空转、测试流量不上链、哈希链 1185/1189 真实覆盖"——设计意图和实现现实，都得数据+骨架+代码三线对齐才完整。数据库像地层，action 时间线就是系统的进化史。**

---

## 精读 SQL 速用卡

```sql
-- ── [1] 元数据：字段结构（8 字段骨架）──
SELECT ordinal_position AS 序号, column_name AS 字段名, data_type AS 类型,
       COALESCE(character_maximum_length::text,'') AS 最大长度,
       is_nullable AS 可空, COALESCE(column_default,'') AS 默认值,
       COALESCE(udt_name,'') AS 底层类型
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'audit_logs'
ORDER BY ordinal_position;

-- ── [2] 真实数据：最新 5 条 ──
SELECT id, user_id, action, module, left(detail,60) AS detail前60字,
       ip_address, created_at::text AS created_at, left(chain_hash,64) AS chain_hash
FROM audit_logs ORDER BY id DESC LIMIT 5;

-- ── [3] 全量分组统计 ──
SELECT min(created_at)::text AS 最早, max(created_at)::text AS 最晚 FROM audit_logs;
SELECT action, count(*) AS 行数 FROM audit_logs GROUP BY action ORDER BY 行数 DESC;
SELECT COALESCE(NULLIF(user_id,''),'(空)') AS 用户, count(*) AS 行数
FROM audit_logs GROUP BY 1 ORDER BY 行数 DESC;

-- ── [4] 真相验证：module 全空 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE module IS NULL OR module = '') AS module为空,
       count(*) FILTER (WHERE module IS NOT NULL AND module <> '') AS module有值
FROM audit_logs;

-- ── [5] 真相验证：无哈希行数 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE chain_hash IS NULL OR chain_hash = '') AS 无哈希,
       count(*) FILTER (WHERE chain_hash IS NOT NULL AND chain_hash <> '') AS 有哈希
FROM audit_logs;

-- ── [6] 哈希长度全量分组统计（SHA256=64位）──
SELECT length(chain_hash) AS 哈希长度, count(*) AS 行数
FROM audit_logs WHERE chain_hash IS NOT NULL AND chain_hash <> '' GROUP BY 1;

-- ── [7] ip_address 空值统计 ──
SELECT count(*) FILTER (WHERE ip_address IS NULL OR ip_address='') AS ip为空,
       count(*) FILTER (WHERE ip_address IS NOT NULL AND ip_address<>'') AS ip有值,
       count(*) AS 总行数 FROM audit_logs;

-- ── [8] 约束/索引/序列（设计决策证据）──
SELECT conname, contype, pg_get_constraintdef(oid) AS 约束定义
FROM pg_constraint
WHERE connamespace = 'public'::regnamespace AND conrelid = 'audit_logs'::regclass;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'audit_logs' ORDER BY indexname;
SELECT last_value FROM pg_sequences
WHERE schemaname = 'public' AND sequencename = 'audit_logs_id_seq';
```
