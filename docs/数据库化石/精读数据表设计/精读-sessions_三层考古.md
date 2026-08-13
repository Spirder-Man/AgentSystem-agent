# sessions 三层精读报告

> **日期**：2026-08-13
> **方法**：数据库 L0 实测 + 代码写入路径溯源（结构→数据→代码，三证齐备）
> **用途**：会话持久化表——短期记忆会话的落库载体（设计意图）

---

## 第 0 步：先立 B

**会话持久化表**：对话会话（id UUID / user_id / session_data）的落库载体。设计意图：让短期记忆（MemoryService 的会话）可持久化、可追溯、可跨重启恢复。

---

## 第 1 步：结构（7 字段，设计完整）

| 字段 | 类型 | 备注 |
|------|------|------|
| id | uuid | PK |
| user_id | varchar(100) | NOT NULL |
| user_name | varchar(200) | 可空 |
| session_data | text | 会话内容（JSON？） |
| created_at / updated_at | timestamptz | 默认 CURRENT_TIMESTAMP |
| expires_at | timestamptz | 会话过期 |

**约束/索引**：pkey + idx_sessions_user_id + idx_sessions_expires_at——**索引齐全**（比 002 家族裸表规范）。

---

## 第 2 步：数据（0 行）

| 维度 | 结果 |
|------|------|
| 行数 | **0** |
| 最早/最晚 | 无 |

---

## 第 3 步：三层考古（写入路径）

**SaveSessionAsync 实锤**（AgentDialog.cs L927-931）：
```csharp
private Task SaveSessionAsync(SessionContext session, string input, string result)
{
    _sessionService.AddDialogTurn(session.SessionId, "User", input);   // → 内存 MemoryService
    _sessionService.AddDialogTurn(session.SessionId, "Assistant", result);
    return Task.CompletedTask;                                          // 无任何 DB 写入
}
```

**全项目 grep 确认**：无 `INSERT INTO sessions`、无 `StoreSession/SaveSession` DB 方法——sessions 表只有建表 DDL（DatabaseService L338-357）+ init_database.sql L17，**零写入代码**。

---

## 第 4 步：落差分析

**这就是 #19（P2）的代码级根因钉死**：

```
long_term_memories 1157 条 source_session_id 全孤儿
← 短期记忆会话只活在内存 ConcurrentDictionary（MemoryService._sessions）
← SaveSessionAsync 只调 AddDialogTurn（内存），从不 INSERT sessions 表
← sessions 表 0 行（幽灵表）
```

- 影响链：记忆不可追溯"哪次对话来的" → 会话级隐私删除（记忆随会话删除）无法实现 → 重启后会话全部丢失
- **本表无新增问题**：#19 已记录（2026-08-13 精读 long_term_memories 时发现），本次精读补上代码证据（SaveSessionAsync L927-931）

---

## 第 5 步：一句话总结

> **sessions 是"结构最规范、运行最彻底空转"的幽灵表：7 字段设计完整、2 索引齐全，但全项目零写入代码——SaveSessionAsync 只写内存 ConcurrentDictionary，数据库表 0 行，1157 条长期记忆的 source_session_id 因此全部孤儿（#19 根因在此钉死）。**

---

## 精读 SQL 速用卡

```sql
-- ── 行数（预期长期为 0，接线后应增长）──
SELECT count(*) FROM sessions;

-- ── 结构 ──
SELECT column_name, data_type, is_nullable FROM information_schema.columns
WHERE table_schema='public' AND table_name='sessions' ORDER BY ordinal_position;
```
