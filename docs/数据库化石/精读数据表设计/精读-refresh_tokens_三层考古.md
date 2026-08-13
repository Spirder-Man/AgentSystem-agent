# refresh_tokens 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码写入/验证路径溯源（结构→数据→代码，三证齐备）
> **用途**：JWT 刷新令牌表——登录后 refresh token 的哈希存储（换取新 access token 的凭证）

---

## 第 0 步：先立 B

**JWT 刷新令牌表**：token_hash（SHA-256 哈希）→ username → 过期时间。access token 过期后，前端用 refresh token 换新 token；换新时旧 refresh token 被删除（一次性令牌）。

---

## 第 1 步：结构（4 字段，安全设计合格）

| 字段 | 类型 | 备注 |
|------|------|------|
| token_hash | varchar(128) | **PK——存哈希不存明文** ✅ |
| username | varchar(100) | NOT NULL |
| expires_at | timestamptz | NOT NULL |
| created_at | timestamptz | 默认 CURRENT_TIMESTAMP |

**约束/索引**：pkey（=token_hash）+ idx_expires + idx_username——索引齐全 ✅。

**设计取舍**：无 revoked_at / last_used_at 字段——吊销 = DELETE（ValidateAndRemoveRefreshTokenAsync 原子删除），简化设计。

---

## 第 2 步：数据（434 行）

| 维度 | 结果 | 信号 |
|------|------|------|
| 行数 | 434 | — |
| 时间跨度 | 6-25 ~ 8-12 | — |
| 按用户 | admin 404 / viewer 24 / auditor 6 | — |
| 未过期 | 7（全 admin） | — |
| **已过期** | **427（98.4%）** | ⚠️ |
| **过期超 7 天** | **420** | ⚠️ |
| 最老过期令牌 | 2026-07-02 | — |

**健康点**：token_hash 全为 Base64 哈希（无明文令牌入库）✅；PK=hash 天然去重 ✅。

---

## 第 3 步：三层考古（写入/验证路径 + 落差）

### 写入路径

```
登录 → StoreRefreshTokenAsync（L1445-1468）
  INSERT ... ON CONFLICT (token_hash) DO NOTHING   ← 幂等 ✅
刷新 → ValidateAndRemoveRefreshTokenAsync（L1470-1494）
  DELETE WHERE token_hash=@h AND expires_at > now() RETURNING username  ← 原子验证+删除 ✅
```

**机制评价**：安全设计合格——哈希存储、一次性令牌（刷新即删）、原子验证。

### ⚠️ 落差 1（P3，#39）：过期令牌无限堆积

**发现**：427/434（98.4%）已过期、420 条过期超 7 天、最老过期令牌躺在 7-02——**全项目无任何清理任务**（grep 确认只有 INSERT + 验证 DELETE，无定期清理/启动清理）。

**根因**：每次登录 INSERT 新 token，但令牌只在"刷新"时被 DELETE——404 个 admin 令牌大多"登录后未刷新"就废弃，永不被清理。

**影响**：表体积缓慢膨胀（434 行 6-25 至今 48 天）；长期运行无上限；虽有小索引可查，但无清理策略违反数据留存纪律（对照 DataRetentionTests 存在但未覆盖此表）。

**修复方案**（待讨论）：启动或定时任务 DELETE 过期令牌（如 `DELETE FROM refresh_tokens WHERE expires_at < now() - INTERVAL '7 days'`）；或登录时清理该用户旧令牌。

**核销验证 SQL**（修复后执行）：
```sql
SELECT count(*) FILTER (WHERE expires_at < now() - INTERVAL '7 days') AS 过期超7天
FROM refresh_tokens;
-- 预期：≈0（清理任务生效后）
```

### 落差 2（观察项）：无 last_used_at

无法统计"令牌实际使用频率"——轮换策略调优缺数据。设计简化，非缺陷，观察级。

---

## 第 4 步：一句话总结

> **refresh_tokens 是"安全设计合格、卫生习惯差"的表：哈希存储（无明文）、PK 天然去重、一次性令牌（刷新即 DELETE）、幂等写入——安全四件套齐全；但 434 行里 427 行已过期（420 条超 7 天）且无任何清理任务，最老的过期令牌从 7-02 躺到今天，纯靠"登录不清理"堆积。**

---

## 精读 SQL 速用卡

```sql
-- ── 过期堆积检查 ──
SELECT count(*) AS 总行数,
       count(*) FILTER (WHERE expires_at >= now()) AS 未过期,
       count(*) FILTER (WHERE expires_at < now() - INTERVAL '7 days') AS 过期超7天
FROM refresh_tokens;

-- ── 按用户 ──
SELECT username, count(*),
       count(*) FILTER (WHERE expires_at >= now()) AS 未过期
FROM refresh_tokens GROUP BY username;

-- ── 清理（修复后由任务执行）──
-- DELETE FROM refresh_tokens WHERE expires_at < now() - INTERVAL '7 days';
```
