# Agent1 化工合规平台 — 前端架构与 UI 交互设计方案

> **版本**: v1.0 | **日期**: 2026-07-10 | **作者**: 全栈架构师角色
>
> **定位**: 本文档是前端开发的唯一架构依据，所有页面、组件、Store、路由设计均以此为基准。

---

# 第一部分：后端逻辑功能架构梳理

## 1.1 核心推理模块（12 个 ModuleType）

| 枚举值 | 数值 | 功能定位 | 前端感知方式 |
|--------|------|----------|-------------|
| `CoTSolid` | 1 | Chain-of-Thought 逐步推理，一次性返回 | 合规自查页「推理详情」面板 |
| `CoTStream` | 2 | CoT 流式推理，实时逐词输出 | SSE 流式展示，打字机效果 |
| `ReActSolid` | 3 | Reasoning + Acting 循环，一次性返回 | 工具调用链可视化 |
| `ReActStream` | 4 | ReAct 流式推理 | SSE 实时工具调用状态 |
| `Reflection` | 5 | 自我验证反思层 | [内部] 对前端透明 |
| `RAG` | 6 | 检索增强生成 | 知识库文档引用角标 |
| `UnifiedDialog` | 7 | 统一对话入口 | 对话式交互界面 |
| `ComplianceCheck` | 8 | 合规审核专项 | 合规自查页核心引擎 |
| `TicketFollowup` | 9 | 整改工单跟进 | 工单详情页状态流转 |
| `RegulatoryAudit` | 10 | 法规审计专项 | 巡检执行引擎 |
| `EmergencyResponse` | 11 | 应急响应 | [预留] 应急响应页面 |
| `KnowledgeGraph` | 12 | 知识图谱查询 | [预留] 知识图谱可视化 |

## 1.2 ChemicalComplianceTools — 9 个 KernelFunction

| 函数名 | 参数 | 功能 | 前端触发场景 |
|--------|------|------|-------------|
| `CheckHazardCategory` | substanceName | 查询危险类别 (GB30000) | 合规自查、化学品查询 |
| `CheckStorageCompatibility` | substanceA, substanceB | 储存兼容性 (GB15603) | 合规自查、资产共储检查 |
| `GetSafetyDistance` | facilityType | 安全距离 (GB50160) | 合规自查、巡检 |
| `LookupChemicalProperties` | substanceName | 化学品属性全览 | 化学品详情卡片 |
| `GetMajorHazardThreshold` | substanceName | 重大危险源临界量 (GB18218) | 风险预警面板 |
| `CheckRegulationVersion` | regulationNumber | 法规版本跟踪 | [辅助] 法规引用校验 |
| `GetCurrentTime` | — | 获取当前时间 | [辅助] 时间戳 |
| `Calculate` | expression | 数学计算 | [辅助] 数值计算 |
| `LookupHazardLabel` | imagePath | GHS 标签识别 (多模态) | [预留] 图像上传识别 |

**3 级检索链路**:
```
用户查询 → SQLite 精确匹配 → 静态字典 (Dictionary) → RAG (HyDE 增强 + BGE Reranker)
```

## 1.3 知识库检索链路

```
用户输入
  → IntentRouter (关键词意图识别)
  → MemoryCoordinator.PreInference
     ├─ ResponseCacheService (5min TTL, 按质量分级)
     ├─ ShortTermMemory (关键词匹配 + 对话上下文)
     └─ LongTermMemory (语义检索 + 化工别名扩展, topK=3)
  → AgentDialog.ExecuteBusiness (SK Auto Function Calling)
  → OutputSanitizer (双通道解耦: 法规引用硬拦截)
  → SafetyGuard.ValidateOutput (高危断言检测)
  → ConclusionVerifier (法规引用验证 vs 幻觉检测)
```

## 1.4 记忆系统 (3 层)

| 层级 | 组件 | TTL | 检索方式 |
|------|------|-----|---------|
| L1 响应缓存 | ResponseCacheService | 5min (质量分级) | 精确 key 匹配 |
| L2 短期记忆 | ShortTermMemoryService | 会话级 | 关键词 + 上下文压缩 |
| L3 长期记忆 | LongTermMemoryService | 持久化 | 语义检索 + 别名扩展 |

## 1.5 审计服务 (SHA256 哈希链)

- 每次敏感操作（合规审核、巡检执行、工单流转）生成 SHA256 哈希链
- 支持完整性验证 (`VerifyIntegrityAsync`)
- DB (PostgreSQL) 持久化，内存降级路径

## 1.6 后端能力 → 前端页面映射

```
┌─────────────────────────────────────────────────────────────┐
│ 后端能力                    →  前端页面/组件                  │
├─────────────────────────────────────────────────────────────┤
│ AuthController (login/refresh) → 登录页 + 路由守卫           │
│ ComplianceController.check     → 合规自查页 (核心)           │
│ ComplianceController.summary   → 仪表盘合规总览卡片          │
│ ComplianceController.hazard    → 化学品快速查询抽屉          │
│ ComplianceController.storage   → 储存兼容性检查组件          │
│ InspectionController.plans/*   → 巡检计划管理页              │
│ InspectionController.execute   → 巡检执行页 (含实时进度)     │
│ InspectionController.rounds/*  → 巡检结果详情页              │
│ InspectionController.reports/* → 报告查看 & 导出             │
│ InspectionController.assets    → 资产台账管理页              │
│ InspectionController.scan      → 自动扫描触发按钮            │
│ InspectionController.quickCheck→ 快速合规检查入口            │
│ TicketsController.list         → 工单列表页                  │
│ TicketsController.updateStatus → 工单状态流转按钮组          │
│ AuditService (日志)            → 审计日志页                  │
│ HealthController               → 系统状态页                  │
│ Cache stats / Memory stats     → 系统监控面板                │
└─────────────────────────────────────────────────────────────┘
```

---

# 第二部分：前端架构设计

## 2.1 角色与权限矩阵

> 对齐后端 `AuthController` 三层角色: `admin` / `auditor` / `viewer`
>
> **⚠️ 2026-07-10 审查更新**: viewer 角色当前无任何业务 API 可访问。
> 后端所有业务 Controller 均使用 `[Authorize(Policy = "Auditor")]`，
> viewer 虽然定义了 Policy 但无 Controller 使用。
> 前端已收缩 viewer 权限为仅 `/login` + `/403`，待后端增加 `Viewer` 策略的 GET 端点后恢复。

| 功能模块 | 操作 | admin | auditor | viewer |
|---------|------|-------|---------|--------|
| **认证** | 登录/登出/刷新 | ✅ | ✅ | ✅ |
| **仪表盘** | 查看合规总览 | ✅ | ✅ | ❌ [^1] |
| **合规自查** | 执行合规查询 | ✅ | ✅ | ❌ |
| | 查看历史查询 | ✅ | ✅ | ❌ [^1] |
| | 导出查询结果 | ✅ | ✅ | ❌ |
| **巡检计划** | 创建计划 | ✅ | ✅ | ❌ |
| | 查看计划列表 | ✅ | ✅ | ❌ [^1] |
| | 编辑/删除计划 | ✅ | ❌ | ❌ |
| | 执行巡检 | ✅ | ✅ | ❌ |
| | 查看巡检结果 | ✅ | ✅ | ❌ [^1] |
| **巡检报告** | 查看报告 | ✅ | ✅ | ❌ [^1] |
| | 导出报告 (JSON) | ✅ | ✅ | ❌ [^1] |
| **工单管理** | 查看工单列表 | ✅ | ✅ | ❌ [^1] |
| | 状态流转 (accept/start...) | ✅ | ✅ | ❌ |
| | 指派责任人 | ✅ | ❌ | ❌ |
| **资产台账** | 查看资产列表 | ✅ | ✅ | ❌ [^1] |
| | 新增/编辑/删除资产 | ✅ | ❌ | ❌ |
| | 触发自动扫描 | ✅ | ✅ | ❌ |
| **审计日志** | 查看操作日志 | ✅ | ❌ | ❌ |
| | 验证哈希链完整性 | ✅ | ❌ | ❌ |
| **系统管理** | 查看系统状态 | ✅ | ✅ | ❌ |
| | 清除缓存 | ✅ | ❌ | ❌ |
| | 知识库更新触发 | ✅ | ❌ | ❌ |

[^1]: 标记 ❌ 的 viewer 列: 后端对应 API 使用 `[Authorize(Policy = "Auditor")]`，viewer 调用返回 401/403。前端已限制 viewer 仅能访问 `/login` 和 `/403`。待后端增加 `Viewer` 策略后恢复为 ✅。

### 权限实现方案

- **路由守卫**: `router.beforeEach` 检查 `authStore.canAccessRoute()` — viewer 访问任何业务路由均重定向至 `/403`
- **侧边栏**: `AppSidebar` 根据角色过滤菜单项 — viewer 仅显示"暂无可用功能"
- **Pinia Store**: `authStore.hasPermission()` — viewer 对所有业务操作的检查返回 `false`
- **API 层保护**: axios 拦截器处理 401/403，后端已有 `[Authorize(Policy = "Auditor")]`

---

## 2.2 页面路由与功能地图

```
/                            → redirect → /dashboard
/login                       → 登录页
/dashboard                   → 总览仪表盘

/compliance                  → 合规自查主页
/compliance/history           → 历史查询记录

/inspection                  → 巡检计划列表
/inspection/create            → 创建巡检计划
/inspection/:planId           → 计划详情
/inspection/:planId/execute   → 巡检执行 (含实时进度)
/inspection/rounds/:roundId   → 巡检轮次结果
/inspection/reports/:roundId  → 巡检报告

/tickets                     → 工单列表
/tickets/:id                 → 工单详情

/assets                      → 资产台账
/assets/:assetId              → 资产详情

/audit                       → 审计日志 (admin only)

/system                      → 系统状态 & 缓存管理

/403                         → 无权限提示页
```

### 路由详细定义

| 路径 | 组件 | 权限 | 调用的后端 API |
|------|------|------|---------------|
| `/login` | LoginPage | Public | POST /api/Auth/login |
| `/dashboard` | DashboardPage | 全部 | GET /api/Compliance/summary, GET /api/Tickets, GET /api/Inspection/plans |
| `/compliance` | ComplianceCheckPage | admin,auditor | POST /api/Compliance/check, POST /api/Compliance/hazard/query, POST /api/Compliance/storage/compatibility |
| `/compliance/history` | ComplianceHistoryPage | 全部 | GET /api/Compliance/cache/stats, GET /api/memory/long-term/search |
| `/inspection` | InspectionPlanListPage | 全部 | GET /api/Inspection/plans |
| `/inspection/create` | InspectionPlanCreatePage | admin,auditor | POST /api/Inspection/plans |
| `/inspection/:planId` | InspectionPlanDetailPage | 全部 | GET /api/Inspection/plans/:id |
| `/inspection/:planId/execute` | InspectionExecutePage | admin,auditor | POST /api/Inspection/plans/:id/execute |
| `/inspection/rounds/:roundId` | InspectionRoundDetailPage | 全部 | GET /api/Inspection/rounds/:id |
| `/inspection/reports/:roundId` | InspectionReportPage | 全部 | GET /api/Inspection/reports/:id, GET /api/Inspection/reports/:id/export |
| `/tickets` | TicketListPage | 全部 | GET /api/Tickets |
| `/tickets/:id` | TicketDetailPage | 全部 | GET /api/Tickets (提取单条), PUT /api/Tickets/:id/status |
| `/assets` | AssetListPage | 全部 | GET /api/Inspection/assets, POST /api/Inspection/scan |
| `/assets/:assetId` | AssetDetailPage | 全部 | GET /api/Inspection/assets (提取单条) |
| `/audit` | AuditLogPage | admin | [需新增] GET /api/Audit/logs |
| `/system` | SystemStatusPage | admin,auditor | GET /health, GET /cache/stats, POST /cache/clear |
| `/403` | ForbiddenPage | 全部 | — |

### 路由导航结构 (侧边栏)

```
├── 📊 总览仪表盘       /dashboard
├── 🔍 合规自查          /compliance
│   └── 历史记录         /compliance/history
├── 📋 巡检管理          /inspection
│   ├── 创建计划         /inspection/create
│   └── 计划详情         /inspection/:planId (动态)
├── 🎫 工单管理          /tickets
│   └── 工单详情         /tickets/:id (动态)
├── 📦 资产台账          /assets
├── 📜 审计日志          /audit          (admin only)
└── ⚙️ 系统管理          /system         (admin/auditor)
```

---

## 2.3 核心页面交互原型描述

### 2.3.1 合规自查页面 (`/compliance`)

```
┌─────────────────────────────────────────────────────────────────┐
│  🔍 合规自查                                            [历史记录]│
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  💬 输入您要查询的合规问题...                      [发送] │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │ 快捷模板: [危化品分类] [储存兼容性] [安全距离]  │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ── 双通道结果展示区 ──                                         │
│                                                                  │
│  ┌─────────────────────────┐ ┌──────────────────────────────┐   │
│  │  📋 法规引用 (结构化)    │ │  🤖 LLM 解释 (自然语言)      │   │
│  │                         │ │                              │   │
│  │  ✅ GB 15603-2022 §4.2  │ │  【合规判断】否               │   │
│  │  ✅ GB 30000.7-2013     │ │  苯与丙酮属于禁忌物料配对    │   │
│  │  ⚠️ 空引用: 0 条        │ │  ...                         │   │
│  │                         │ │                              │   │
│  │  工具调用链:             │ │  警告:                       │   │
│  │  CheckStorageCompat ✓   │ │  ⚠️ 丙酮存量超临界量80%     │   │
│  │  CheckHazardCategory ✓  │ │                              │   │
│  │  LookupChemProps ✓      │ │  [复制结果] [导出PDF]        │   │
│  └─────────────────────────┘ └──────────────────────────────┘   │
│                                                                  │
│  ── 进度指示 (查询中时显示) ──                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  ████████████░░░░░░░░  65%  LLM 推理中... 预计 8s        │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

**交互流程**:
1. 用户在输入框输入查询（支持纯文本 + 快捷模板点击填充）
2. 前端 POST `/api/Compliance/check`，显示进度条（MSW 模拟 3-25s 延迟）
3. 后端返回 `ComplianceResponse`（含 `toolsUsed`, `verifiedRegulations`, `hallucinatedRegulations`, `warnings`）
4. 左侧法规引用卡片：结构化展示 `verifiedRegulations`，空引用以警告样式标出
5. 右侧 LLM 解释卡片：`markdown-it` 渲染 Markdown 格式的 `response`
6. 底部显示工具调用链（配合图标）

**状态处理**:
- 503 错误 → 显示"服务繁忙"提示 + 自动重试倒计时
- 安全拦截 → 红色提示"输入被安全拦截"
- 缓存命中 → 标记"⚡ 缓存结果"

### 2.3.2 工单管理页面

**列表页 (`/tickets`)**:

```
┌─────────────────────────────────────────────────────────────────┐
│  🎫 整改工单                                        [导出列表]  │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  [🔍 搜索工单...]  [状态▼] [优先级▼] [法规▼]   [查询]  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  总工单: 5  |  处理中: 3  |  已关闭: 2                           │
│                                                                  │
│  ┌────┬──────────────────┬────────┬────────┬────────┬────────┐  │
│  │ ID │ 问题描述          │ 优先级  │ 状态   │ 责任人 │ 操作   │  │
│  ├────┼──────────────────┼────────┼────────┼────────┼────────┤  │
│  │  1 │ 苯与丙酮同库储存  │ 🔴Crit │ New    │ 未分配 │ 受理   │  │
│  │  2 │ 甲醇存量超临界量  │ 🟠High │ Confmd │ 李四   │ 开始   │  │
│  │  3 │ 消防通道标识不清  │ 🟡Med  │ 进行中 │ 王五   │ 完成   │  │
│  │  4 │ 通风系统老化      │ 🟠High │ Confmd │ 张三   │ 开始   │  │
│  │  5 │ 防雷接地超标      │ 🟠High │ 进行中 │ 赵六   │ 完成   │  │
│  └────┴──────────────────┴────────┴────────┴────────┴────────┘  │
│                                                                  │
│  < 第 1/1 页 >                                                   │
└─────────────────────────────────────────────────────────────────┘
```

**详情页 (`/tickets/:id`)**:

```
┌─────────────────────────────────────────────────────────────────┐
│  ← 返回列表      工单 #1 详情                                   │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  基本信息                                                 │   │
│  │  ┌──────────────┬────────────────────────────────────┐   │   │
│  │  │ 问题描述      │ 苯与丙酮同库储存违规                │   │   │
│  │  │ 整改措施      │ 立即分库储存 — 苯移至A区1号        │   │   │
│  │  │ 法规依据      │ GB 15603-2022 §4.2.2               │   │   │
│  │  │ 优先级        │ 🔴 严重 (Critical)                 │   │   │
│  │  │ 责任人        │ 未分配                             │   │   │
│  │  │ 建议期限      │ 2026-06-25                         │   │   │
│  │  │ 当前状态      │ New                                │   │   │
│  │  └──────────────┴────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  状态流转                                                 │   │
│  │                                                           │   │
│  │  New ──→ Confirmed ──→ InProgress ──→ Remediated          │   │
│  │  └──→ FalsePositive      └──→ Closed    └──→ VerifiedClosed│  │
│  │                                                           │   │
│  │  当前状态: ● New                                          │   │
│  │  可用操作: [受理] [驳回(误报)]                             │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  操作日志                                                 │   │
│  │  2026-06-23 10:00  系统自动生成 (来源:巡检 plan-001)      │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

**工单状态流转规则 (对齐后端状态机)**:

```
New ──[accept]──→ Confirmed ──[start]──→ InProgress ──[complete]──→ Remediated ──[verify]──→ VerifiedClosed
  │                                                                                               │
  └──[reject]──→ FalsePositive                                                                   │
                                     Confirmed/InProgress ──[close]──→ Closed
```

### 2.3.3 总览仪表盘 (`/dashboard`)

```
┌─────────────────────────────────────────────────────────────────┐
│  📊 合规总览仪表盘                       最后扫描: 2026-06-23    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ 总资产   │ │ 合规率   │ │ 待处理   │ │ 不合规   │           │
│  │   8      │ │   75%    │ │ 工单 3   │ │ 资产 2   │           │
│  │ 已检 6   │ │ ▲+5%     │ │ 🔴1 🟠2 │ │          │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
│                                                                  │
│  ┌──────────────────────────────┐ ┌───────────────────────────┐ │
│  │  📈 近期风险趋势 (ECharts)   │ │  📊 发现项分布 (饼图)     │ │
│  │  ┌────────────────────────┐  │ │  Critical: 2  ████████   │ │
│  │  │  ▂▃▅▇█▇▅▃▂▁           │  │ │  High:     1  ████      │ │
│  │  │  合规率趋势 (近7日)     │  │ │  Medium:   1  ████      │ │
│  │  └────────────────────────┘  │ │  Low:      0             │ │
│  └──────────────────────────────┘ └───────────────────────────┘ │
│                                                                  │
│  ┌──────────────────────────────┐ ┌───────────────────────────┐ │
│  │  📋 最新巡检计划              │ │  🎫 紧急工单              │ │
│  │  ● 甲类仓库周检 (已完成)     │ │  🔴 苯丙酮同库储存       │ │
│  │  ● 罐区月度检查 (进行中)     │ │  🟠 甲醇存量超临界量     │ │
│  │  ● 节前安全大检查 (草稿)     │ │  🟠 通风系统老化         │ │
│  └──────────────────────────────┘ └───────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

**数据源**: `GET /api/Compliance/summary` + `GET /api/Tickets` + `GET /api/Inspection/plans`

### 2.3.4 巡检计划管理页 (`/inspection`)

```
┌─────────────────────────────────────────────────────────────────┐
│  📋 巡检计划管理                                    [+ 新建计划] │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  [全部▼] [周检] [月检] [节前检查]                                │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  ▸ 甲类仓库周检                                          │   │
│  │    状态: ✅ 已完成 | 区域: 甲类仓库A区 | 检查人: 张三    │   │
│  │    合规率: 80% | 检查项: 5 | 工单: 2                     │   │
│  │    [查看详情] [查看报告] [重新执行]                        │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │  ▸ 罐区月度安全检查                                      │   │
│  │    状态: 🔄 进行中 | 区域: 储罐区 | 检查人: 李四         │   │
│  │    合规率: 待完成 | 检查项: 4 | 工单: 0                  │   │
│  │    [继续执行] [查看详情]                                   │   │
│  ├──────────────────────────────────────────────────────────┤   │
│  │  ▸ 节前安全大检查                                        │   │
│  │    状态: 📝 草稿 | 区域: 全园区 | 检查人: 王五           │   │
│  │    检查项: 8 | 创建于: 2026-06-21                        │   │
│  │    [编辑] [删除] [执行]                                    │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.3.5 资产台账页 (`/assets`)

```
┌─────────────────────────────────────────────────────────────────┐
│  📦 化学品资产台账                        [🔍 搜索] [自动扫描]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  ┌──────────┬──────────┬──────────┬──────────┬────────┐  │   │
│  │  │ 名称     │ CAS号    │ 位置     │ 存量(吨) │ 状态   │  │   │
│  │  ├──────────┼──────────┼──────────┼──────────┼────────┤  │   │
│  │  │ 🔴 苯    │ 71-43-2  │ 甲A区1号 │ 15       │ ❌不合规│  │   │
│  │  │ 🟢 丙酮  │ 67-64-1  │ 甲A区2号 │ 8        │ ✅合规  │  │   │
│  │  │ 🔴 甲醇  │ 67-56-1  │ 甲B区1号 │ 20       │ ❌不合规│  │   │
│  │  │ 🟢 硝酸  │ 7697-37-2│ 乙C区3号 │ 5        │ ✅合规  │  │   │
│  │  │ ⬜ 氢氧化钠│1310-73-2 │ 乙D区1号 │ 3        │ 未检查 │  │   │
│  │  │ 🟢 氯    │ 7782-50-5│ 甲C区2号 │ 2        │ ✅合规  │  │   │
│  │  └──────────┴──────────┴──────────┴──────────┴────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  图标说明: 🔴不合规 🟢合规 🟡警告 ⬜未检查                        │
└─────────────────────────────────────────────────────────────────┘
```

**自动扫描交互**: 点击 `[自动扫描]` → POST `/api/Inspection/scan` → 显示扫描进度 → 返回 `ScanResult` → 刷新列表并高亮新发现。

### 2.3.6 审计日志页 (`/audit`) — admin only

```
┌─────────────────────────────────────────────────────────────────┐
│  📜 审计日志                          [验证哈希链] [导出报告]   │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  时间范围: [2026-06-01] ~ [2026-07-10]  [用户▼] [查询]  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────┬──────────┬──────────┬──────────┬──────────────────┐   │
│  │ ID   │ 时间     │ 用户     │ 操作     │ 详情             │   │
│  ├──────┼──────────┼──────────┼──────────┼──────────────────┤   │
│  │  42  │ 06-23    │ system   │ 合规审核 │ 查询: 苯丙酮... │   │
│  │  41  │ 06-23    │ 张三     │ 巡检执行 │ plan-001, 5项   │   │
│  │  40  │ 06-23    │ system   │ 记忆更新 │ 工具: 3个       │   │
│  └──────┴──────────┴──────────┴──────────┴──────────────────┘   │
│                                                                  │
│  哈希链状态: ✅ 完整 (42 条记录)                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2.4 组件树规划

### 可复用组件清单

```
components/
├── common/
│   ├── AppLayout.vue              # 全局布局 (侧边栏 + 顶栏 + 内容区)
│   ├── PermissionButton.vue       # 权限控制按钮 (v-permission 封装)
│   ├── LoadingOverlay.vue         # LLM 推理进度覆盖层
│   ├── ErrorState.vue             # 错误状态占位 (含重试按钮)
│   ├── EmptyState.vue             # 空数据占位
│   └── PageHeader.vue             # 页面标题栏 (含面包屑)
│
├── compliance/
│   ├── ComplianceInput.vue        # 合规查询输入框 (含快捷模板)
│   ├── ComplianceResultCard.vue   # 合规结果容器 (双通道)
│   ├── RegulationRefPanel.vue     # 法规引用面板 (结构化列表)
│   ├── LlmExplanationPanel.vue    # LLM 解释面板 (Markdown 渲染)
│   ├── ToolCallChain.vue          # 工具调用链可视化 (时间线)
│   ├── QuickTemplateBar.vue       # 快捷查询模板栏
│   └── ComplianceProgress.vue     # 推理进度条
│
├── inspection/
│   ├── PlanCard.vue               # 巡检计划卡片
│   ├── PlanStatusBadge.vue        # 计划状态徽章
│   ├── InspectionResultTable.vue  # 巡检结果表格
│   ├── InspectionProgress.vue     # 巡检执行进度 (多阶段)
│   └── ReportPreview.vue          # 报告预览 (Markdown)
│
├── ticket/
│   ├── TicketTable.vue            # 工单列表表格
│   ├── TicketStatusBadge.vue      # 工单状态徽章 (含颜色映射)
│   ├── TicketPriorityTag.vue      # 优先级标签
│   ├── TicketStatusFlow.vue       # 状态流转可视化 (状态机图)
│   └── TicketActionButtons.vue    # 工单操作按钮组 (按状态动态显示)
│
├── asset/
│   ├── AssetTable.vue             # 资产台账表格
│   ├── AssetStatusIcon.vue        # 资产品合规状态图标
│   └── AssetDetailCard.vue        # 资产详情卡片
│
├── dashboard/
│   ├── StatCard.vue               # 统计指标卡片
│   ├── ComplianceRateGauge.vue    # 合规率仪表盘 (ECharts gauge)
│   ├── RiskTrendChart.vue         # 风险趋势图 (ECharts line)
│   ├── FindingPieChart.vue        # 发现项分布饼图
│   ├── RecentPlansList.vue        # 最近巡检计划列表
│   └── UrgentTicketsList.vue      # 紧急工单列表
│
└── charts/
    ├── BaseEChart.vue             # ECharts 基础封装
    ├── SeverityBarChart.vue       # 严重程度柱状图
    └── TimelineChart.vue          # 事件时间线图
```

### 核心组件 Props/Events 规格

#### `ComplianceResultCard.vue`

```typescript
// Props
interface ComplianceResultCardProps {
  /** 查询文本 */
  query: string;
  /** 是否加载中 */
  loading: boolean;
  /** 合规响应数据 */
  result: ComplianceResponse | null;
  /** 推理进度百分比 (0-100) */
  progress?: number;
}

// Events
interface ComplianceResultCardEmits {
  /** 请求重新查询 */
  (e: 'retry'): void;
  /** 导出结果 */
  (e: 'export', format: 'pdf' | 'json'): void;
  /** 复制结果到剪贴板 */
  (e: 'copy'): void;
}
```

#### `TicketStatusBadge.vue`

```typescript
// Props
interface TicketStatusBadgeProps {
  status: 'New' | 'Confirmed' | 'InProgress' | 'Remediated' | 'VerifiedClosed' | 'Closed' | 'FalsePositive';
  /** 是否显示中文文本 */
  showLabel?: boolean;
}

// 颜色映射
const statusColorMap: Record<string, string> = {
  New: 'info',           // 蓝色
  Confirmed: 'warning',  // 橙色
  InProgress: '',        // 默认 (蓝色)
  Remediated: 'success', // 绿色
  VerifiedClosed: 'success', // 绿色
  Closed: 'info',        // 灰色
  FalsePositive: 'danger', // 红色
};
```

#### `TicketActionButtons.vue`

```typescript
// Props
interface TicketActionButtonsProps {
  status: TicketStatusBadgeProps['status'];
  ticketId: number;
  assignee: string;
}

// 根据当前状态，计算可用操作按钮
// 对齐后端状态机: New→[accept, reject] Confirmed→[start, close] InProgress→[complete, close]
//                Remediated→[verify] VerifiedClosed/Closed/FalsePositive→[]
```

#### `ChemicalSelector.vue` (合规自查页化学品选择器)

```typescript
// Props
interface ChemicalSelectorProps {
  modelValue: string;
  placeholder?: string;
  /** 是否允许多选 */
  multiple?: boolean;
}

// Events
interface ChemicalSelectorEmits {
  (e: 'update:modelValue', value: string): void;
  (e: 'select', substance: ChemicalAsset): void;
}

// 特性: 支持模糊搜索 + 下拉建议 (从 GET /api/Inspection/assets 获取候选列表)
```

#### `StatCard.vue`

```typescript
// Props
interface StatCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  trend?: 'up' | 'down' | 'stable';
  trendValue?: string;
  icon?: string;       // Element Plus icon name
  color?: string;      // Tailwind color class
}
```

---

## 2.5 数据流设计 (Pinia Store)

### Store 架构总览

```
stores/
├── auth.ts           # 认证状态 (JWT, 角色, 登录/登出/刷新)
├── compliance.ts     # 合规自查状态 (查询历史, 缓存信息)
├── inspection.ts     # 巡检状态 (计划列表, 执行进度, 报告)
├── ticket.ts         # 工单状态 (列表, 筛选, 状态流转)
├── asset.ts          # 资产台账状态
├── dashboard.ts      # 仪表盘聚合状态
├── audit.ts          # 审计日志状态
└── system.ts         # 系统状态 (健康检查, 缓存, 内存)
```

### 详细 Store 定义

#### `authStore` — 认证

```typescript
// stores/auth.ts
interface AuthState {
  token: string | null;
  refreshToken: string | null;
  username: string;
  role: 'admin' | 'auditor' | 'viewer' | '';
  expiresAt: string | null;
}

// Getters
- isAuthenticated: boolean
- isAdmin: boolean
- isAuditor: boolean  // admin 也视为 auditor
- hasPermission(action: string): boolean

// Actions
- login(username: string, password: string): Promise<void>
  → POST /api/Auth/login → 存储 token/refreshToken → configureAuth()
- logout(): Promise<void>
  → POST /api/Auth/logout → 清除状态 → router.push('/login')
- refreshToken(): Promise<void>
  → POST /api/Auth/refresh → 更新 token

// 持久化: localStorage (token + refreshToken, 加密存储)
```

#### `complianceStore` — 合规自查

```typescript
// stores/compliance.ts
interface ComplianceState {
  /** 当前查询结果 */
  currentResult: ComplianceResponse | null;
  /** 查询历史 (最近 N 条) */
  history: ComplianceHistoryItem[];
  /** 加载状态 */
  loading: boolean;
  /** 推理进度 */
  progress: number;
  /** 查询耗时 */
  elapsedMs: number;
}

// Actions
- checkCompliance(query: string): Promise<void>
  → POST /api/Compliance/check
  → 更新 currentResult
  → 追加到 history (最近 20 条, localStorage 持久化)

- queryHazard(substanceName: string): Promise<HazardQueryResponse>
  → POST /api/Compliance/hazard/query

- checkStorageCompatibility(a: string, b: string): Promise<StorageCompatibilityResponse>
  → POST /api/Compliance/storage/compatibility

- clearHistory(): void
```

#### `inspectionStore` — 巡检

```typescript
// stores/inspection.ts
interface InspectionState {
  plans: InspectionPlan[];
  currentPlan: InspectionPlan | null;
  currentRound: InspectionRound | null;
  currentReport: InspectionReport | null;
  executingPlanId: string | null;
  executeProgress: number;  // 执行进度 0-100
}

// Actions
- fetchPlans(): Promise<void>
  → GET /api/Inspection/plans

- createPlan(req: CreatePlanRequest): Promise<string>
  → POST /api/Inspection/plans → 返回 planId

- fetchPlanDetail(planId: string): Promise<void>
  → GET /api/Inspection/plans/:id

- executePlan(planId: string): Promise<void>
  → POST /api/Inspection/plans/:id/execute
  → 轮询或等待返回 roundId

- fetchRound(roundId: string): Promise<void>
  → GET /api/Inspection/rounds/:id

- fetchReport(roundId: string): Promise<void>
  → GET /api/Inspection/reports/:id

- exportReport(roundId: string): Promise<ExportData>
  → GET /api/Inspection/reports/:id/export
```

#### `ticketStore` — 工单

```typescript
// stores/ticket.ts
interface TicketState {
  tickets: TicketItem[];
  total: number;
  openCount: number;
  loading: boolean;
  // 筛选条件
  filters: {
    status: string | null;
    priority: string | null;
    keyword: string;
  };
  currentTicket: TicketItem | null;
}

// Actions
- fetchTickets(): Promise<void>
  → GET /api/Tickets

- updateTicketStatus(id: number, action: TicketAction, assignee?: string, reason?: string): Promise<void>
  → PUT /api/Tickets/:id/status
  → 乐观更新列表 (先更新 UI，失败回滚)

- setFilters(filters: Partial<TicketState['filters']>): void

// Getters
- filteredTickets: TicketItem[]  (基于 filters 前端过滤)
```

#### `dashboardStore` — 仪表盘

```typescript
// stores/dashboard.ts
interface DashboardState {
  summary: ComplianceSummary | null;
  recentPlans: InspectionPlan[];
  urgentTickets: TicketItem[];
  loading: boolean;
}

// Actions
- fetchDashboardData(): Promise<void>
  → 并行请求:
    GET /api/Compliance/summary
    GET /api/Inspection/plans (取最近 3 条)
    GET /api/Tickets (取 open + Critical/High 优先级)
```

#### `assetStore` — 资产

```typescript
// stores/asset.ts
interface AssetState {
  assets: ChemicalAsset[];
  scanResult: ScanResult | null;
  scanning: boolean;
}

// Actions
- fetchAssets(): Promise<void>
  → GET /api/Inspection/assets

- runAutoScan(): Promise<void>
  → POST /api/Inspection/scan → 更新 scanResult

- quickCheck(query: string): Promise<QuickCheckResult>
  → POST /api/Inspection/quick-check
```

### Mock 数据与 API 对齐

沿用项目已有的 MSW Mock 机制 (`VITE_ENABLE_MOCK=true`)。Mock 数据结构严格对齐后端：

| Store Action | API 端点 | Mock Handler | Mock Data |
|-------------|----------|-------------|-----------|
| `authStore.login` | POST /api/Auth/login | `handlers.ts:56` | 动态生成 JWT |
| `complianceStore.checkCompliance` | POST /api/Compliance/check | `handlers.ts:120` | `compliance.ts:getComplianceResponse()` |
| `complianceStore.queryHazard` | POST /api/Compliance/hazard/query | `handlers.ts:135` | `compliance.ts:getHazardResponse()` |
| `complianceStore.checkStorage` | POST /api/Compliance/storage/compatibility | `handlers.ts:144` | `compliance.ts:getStorageCompatibilityResponse()` |
| `dashboardStore.fetchDashboardData` | GET /api/Compliance/summary | `handlers.ts:114` | `compliance.ts:mockComplianceSummary` |
| `inspectionStore.fetchPlans` | GET /api/Inspection/plans | `handlers.ts:156` | `inspection.ts:mockPlans` |
| `inspectionStore.executePlan` | POST /api/Inspection/plans/:id/execute | `handlers.ts:203` | `inspection.ts:getMockRound()` |
| `ticketStore.fetchTickets` | GET /api/Tickets | `handlers.ts:324` | `tickets.ts:mockTicketList` |
| `ticketStore.updateTicketStatus` | PUT /api/Tickets/:id/status | `handlers.ts:331` | `tickets.ts:applyTicketStatusUpdate()` |
| `assetStore.fetchAssets` | GET /api/Inspection/assets | `handlers.ts:296` | `inspection.ts:mockAssets` |
| `assetStore.runAutoScan` | POST /api/Inspection/scan | `handlers.ts:303` | `inspection.ts:mockScanResult` |

---

## 2.6 关键技术栈约束

### 2.6.1 框架与依赖

| 类别 | 技术 | 版本 | 约束说明 |
|------|------|------|---------|
| 框架 | Vue 3 | ^3.5.0 | Composition API + `<script setup lang="ts">` |
| 构建 | Vite 5 | ^5.4.0 | — |
| 状态管理 | Pinia | ^2.2.0 | 每个业务域一个 Store，禁止巨型 Store |
| 服务端状态 | @tanstack/vue-query | ^5.51.0 | 所有 GET 请求使用 `useQuery`，POST/PUT 使用 `useMutation` |
| HTTP 客户端 | Axios | ^1.7.2 | 统一使用 `@/lib/axios.ts` 实例，含 JWT 拦截器 |
| UI 组件库 | Element Plus | ^2.8.0 | 中文语言包 (`zh-cn`) |
| 图表 | ECharts + vue-echarts | ^5.5.1 / ^7.0.3 | 仪表盘图表，按需引入 |
| CSS | Tailwind CSS | ^3.4.10 | 补充 Element Plus 的样式定制 |
| Mock | MSW | ^2.4.0 | 开发模式 `VITE_ENABLE_MOCK=true` |
| Markdown 渲染 | markdown-it + highlight.js | ^14.1.0 / ^11.10.0 | 合规自查 LLM 解释面板 |
| 表单验证 | vee-validate + zod | ^4.13.2 / ^3.23.8 | 创建巡检计划等表单 |
| 类型检查 | TypeScript | ~5.5.4 | **严格模式，禁止 `any`** |

### 2.6.2 编码规范

1. **TypeScript**: 所有 `.vue` 和 `.ts` 文件启用 `strict: true`
   - ❌ 禁止 `any` 类型 (P0 约束，见 `prompts/constraints/no-any.constraint.md`)
   - 允许局部 `as` 类型断言，但必须注释原因
   - 所有 API 响应类型从 `types/api.ts` 导入
2. **组件结构顺序** (Vue SFC):
   ```
   <script setup lang="ts">  // 1. 导入 + 类型
                             // 2. Props/Emits 定义
                             // 3. Composables/Stores
                             // 4. 响应式数据
                             // 5. 计算属性
                             // 6. 方法
                             // 7. 生命周期钩子
   </script>
   <template>                // 模板
   </template>
   <style scoped>            // 样式 (尽量用 Tailwind，少写 scoped CSS)
   </style>
   ```
3. **命名规范**:
   - 页面组件: `PascalCasePage.vue` (如 `DashboardPage.vue`)
   - 通用组件: `PascalCase.vue` (如 `ComplianceResultCard.vue`)
   - Store: `camelCase` + `Store` 后缀 (如 `useAuthStore`)
   - Composable: `use` 前缀 (如 `useComplianceCheck`)
   - API 函数: `camelCase` (如 `checkCompliance`)
4. **国际化**: 所有用户可见文本使用 `$t('key')` 函数，键值定义在 `locales/zh-CN.ts`
5. **响应式设计**: 默认适配 1440px+ 桌面端，Sidebar 折叠适配 1024px，移动端暂不做适配

### 2.6.3 目录结构 (完整)

```
agent1-web/src/
├── App.vue
├── main.ts
├── assets/
│   └── main.css              # Tailwind 入口 + 全局样式
├── components/
│   ├── common/               # 通用组件 (见 2.4)
│   ├── compliance/           # 合规自查组件
│   ├── inspection/           # 巡检组件
│   ├── ticket/               # 工单组件
│   ├── asset/                # 资产组件
│   ├── dashboard/            # 仪表盘组件
│   └── charts/               # 图表组件
├── composables/
│   ├── useAuth.ts            # 认证逻辑封装
│   ├── useComplianceCheck.ts # 合规查询流程封装
│   ├── usePagination.ts      # 分页逻辑
│   └── usePermission.ts      # 权限检查
├── layouts/
│   └── DefaultLayout.vue     # 默认布局 (侧边栏 + 主内容)
├── lib/
│   └── axios.ts              # Axios 实例 + 拦截器
├── locales/
│   └── zh-CN.ts              # 国际化字符串
├── mocks/
│   ├── server.ts             # MSW Service Worker
│   ├── handlers.ts           # API Mock Handlers
│   └── data/                 # Mock 数据
│       ├── compliance.ts
│       ├── inspection.ts
│       └── tickets.ts
├── pages/
│   ├── LoginPage.vue
│   ├── DashboardPage.vue
│   ├── ComplianceCheckPage.vue
│   ├── ComplianceHistoryPage.vue
│   ├── InspectionPlanListPage.vue
│   ├── InspectionPlanCreatePage.vue
│   ├── InspectionPlanDetailPage.vue
│   ├── InspectionExecutePage.vue
│   ├── InspectionRoundDetailPage.vue
│   ├── InspectionReportPage.vue
│   ├── TicketListPage.vue
│   ├── TicketDetailPage.vue
│   ├── AssetListPage.vue
│   ├── AssetDetailPage.vue
│   ├── AuditLogPage.vue
│   ├── SystemStatusPage.vue
│   └── ForbiddenPage.vue
├── router/
│   └── index.ts              # 路由配置 + 导航守卫
├── stores/
│   ├── auth.ts
│   ├── compliance.ts
│   ├── inspection.ts
│   ├── ticket.ts
│   ├── asset.ts
│   ├── dashboard.ts
│   ├── audit.ts
│   └── system.ts
├── types/
│   └── api.ts                # API 类型定义 (已存在, 需扩展)
└── utils/
    ├── format.ts             # 日期/数字格式化
    ├── validation.ts         # Zod Schema 定义
    └── constants.ts          # 常量 (状态枚举, 颜色映射等)
```

### 2.6.4 开发环境启动

```bash
# Mock 模式 (前后端分离开发)
cd agent1-web
npm run dev:mock       # VITE_ENABLE_MOCK=true

# 真实 API 模式 (联调)
npm run dev            # VITE_ENABLE_MOCK=false, 需要后端 API 运行
```

---

## 附录 A: Vue Query 使用规范

```typescript
// ✅ 推荐: GET 请求使用 useQuery
const { data: summary, isLoading } = useQuery({
  queryKey: ['compliance', 'summary'],
  queryFn: () => apiClient.get<ComplianceSummary>('/api/Compliance/summary').then(r => r.data),
  staleTime: 30_000,  // 30s 内不重新请求
});

// ✅ 推荐: POST/PUT 使用 useMutation
const { mutate: checkCompliance, isPending } = useMutation({
  mutationFn: (query: string) =>
    apiClient.post<ComplianceResponse>('/api/Compliance/check', { query }).then(r => r.data),
  onSuccess: (data) => {
    complianceStore.currentResult = data;
  },
});

// ❌ 禁止: 在组件中直接调用 apiClient 绕过 Store/Vue Query
```

## 附录 B: 后端 Finding 状态机 → 前端工单按钮映射

```typescript
// utils/constants.ts
export const TICKET_ACTIONS_BY_STATUS: Record<string, Array<{
  action: TicketStatusUpdateRequest['action'];
  label: string;
  type: 'primary' | 'success' | 'warning' | 'danger' | 'info';
}>> = {
  New: [
    { action: 'accept', label: '受理', type: 'primary' },
    { action: 'reject', label: '驳回(误报)', type: 'danger' },
  ],
  Confirmed: [
    { action: 'start', label: '开始处理', type: 'primary' },
    { action: 'close', label: '关闭工单', type: 'info' },
  ],
  InProgress: [
    { action: 'complete', label: '完成整改', type: 'success' },
    { action: 'close', label: '关闭工单', type: 'info' },
  ],
  Remediated: [
    { action: 'verify', label: '验收通过', type: 'success' },
  ],
  // VerifiedClosed, Closed, FalsePositive → 无可用操作
};
```

## 附录 C: 后端 API 端点汇总

| Method | Endpoint | Controller | 权限 | 说明 |
|--------|---------|------------|------|------|
| POST | /api/Auth/login | Auth | Public | 登录 |
| POST | /api/Auth/refresh | Auth | Public | 刷新 Token |
| POST | /api/Auth/logout | Auth | Authenticated | 登出 |
| GET | /api/Compliance/summary | Compliance | Auditor | 合规总览 |
| POST | /api/Compliance/check | Compliance | Auditor | 合规审核 |
| POST | /api/Compliance/hazard/query | Compliance | Auditor | 危化品查询 |
| POST | /api/Compliance/storage/compatibility | Compliance | Auditor | 储存兼容性 |
| GET | /api/Inspection/plans | Inspection | Auditor | 计划列表 |
| POST | /api/Inspection/plans | Inspection | Auditor | 创建计划 |
| GET | /api/Inspection/plans/:id | Inspection | Auditor | 计划详情 |
| POST | /api/Inspection/plans/:id/execute | Inspection | Auditor | 执行巡检 |
| GET | /api/Inspection/rounds/:id | Inspection | Auditor | 巡检结果 |
| GET | /api/Inspection/reports/:id | Inspection | Auditor | 查看报告 |
| GET | /api/Inspection/reports/:id/export | Inspection | Auditor | 导出报告 |
| GET | /api/Inspection/assets | Inspection | Auditor | 资产台账 |
| POST | /api/Inspection/scan | Inspection | Auditor | 自动扫描 |
| POST | /api/Inspection/quick-check | Inspection | Auditor | 快速检查 |
| GET | /api/Tickets | Tickets | Auditor | 工单列表 |
| PUT | /api/Tickets/:id/status | Tickets | Auditor | 工单状态流转 |

--

# 第三部分：生产加固与工程化补充

> **定位**: 本部分将设计文档从"静态蓝图"升级为"面向交付的作战地图"，覆盖错误处理、测试架构、性能、安全、DX、持久化、UI 细节七个维度，按 Sprint 拆分可执行任务。

---

## 3.1 错误处理与韧性策略

### 3.1.1 全局错误边界 (`ErrorBoundary.vue`)

```
┌─────────────────────────────────────────────────────────┐
│  <ErrorBoundary>                                        │
│    ┌──────────────────────────────────────────────┐     │
│    │  捕获: 渲染异常 / 未处理 Promise / 组件错误  │     │
│    └──────────────────────────────────────────────┘     │
│    <slot />  ← 正常渲染                                │
│    <template #fallback="{ error, retry }">             │
│      ┌──────────────────────────────────────────┐      │
│      │  ⚠️ 页面加载异常                          │      │
│      │  错误详情: {{ error.message }}            │      │
│      │  [重试] [返回首页]                        │      │
│      └──────────────────────────────────────────┘      │
│    </template>                                         │
│  </ErrorBoundary>                                      │
└─────────────────────────────────────────────────────────┘
```

**实现要点**:
- 使用 Vue 的 `onErrorCaptured` 钩子捕获子组件树错误
- `fallback` 插槽提供降级 UI，包含 `retry` 回调（重置错误状态并重新渲染）
- 错误自动上报到 `console.error` + 可扩展的日志收集端点（`POST /api/log/client-error`）
- 开发环境展示完整错误堆栈，生产环境展示用户友好提示

### 3.1.2 API 错误统一拦截（增强 `lib/axios.ts`）

```typescript
// 标准化错误接口 (扩展现有 types/api.ts)
interface ApiError {
  error: string;              // 人类可读错误描述
  code?: string;              // 业务错误码 (如 'RATE_LIMITED', 'INVALID_INPUT')
  retryAfter?: number;        // 503 重试等待秒数
  details?: Record<string, string[]>;  // 字段级校验错误
}

// 错误码 → 用户友好消息映射
const ERROR_MESSAGES: Record<string, string> = {
  RATE_LIMITED: '请求过于频繁，请稍后重试',
  INVALID_INPUT: '输入内容不符合要求，请检查后重新提交',
  SESSION_EXPIRED: '会话已过期，请重新登录',
  UNAUTHORIZED: '您没有权限执行此操作',
};
```

**响应拦截器增强**（在现有 `lib/axios.ts` 中扩展）:

| HTTP 状态 | 处理策略 | 用户反馈 |
|-----------|---------|----------|
| 400 | 解析 `details` 展示字段级错误 | `ElMessage.warning` |
| 401 | 触发 Token 刷新（已有），失败则跳转登录 | `ElMessage.error('登录已过期')` |
| 403 | 跳转 `/403` 页面 | 无权限页面 |
| 422 | 业务校验失败，展示 `error` 字段 | `ElMessage.warning` |
| 429 | 限流，展示 `retryAfter` 倒计时 | `ElNotification` 含倒计时 |
| 500 | 服务端内部错误 | `ElMessage.error('服务异常，已记录日志')` |
| 503 | 指数退避重试（见 3.1.3） | `ElNotification` 含重试状态 |
| Network Error | 检测 `navigator.onLine`，离线提示 | `ElMessage.error('网络连接异常')` |

### 3.1.3 重试与退避策略 (`composables/useRetry.ts`)

```typescript
// composables/useRetry.ts
export function useRetry(options?: {
  maxRetries?: number;       // 默认 3
  baseDelayMs?: number;      // 默认 2000
  backoffMultiplier?: number; // 默认 2 (指数退避)
}) {
  // 返回:
  // - execute(fn): 执行并自动重试
  // - retryCount: Ref<number>
  // - nextRetryIn: Ref<number>  (倒计时秒数)
  // - isRetrying: Ref<boolean>
  // - cancel(): 取消重试
}
```

**合规自查页重试交互**:
```
[查询中 ████████░░  65%]  →  503 错误发生
  ↓
[⚠️ 服务繁忙，将在 8s 后自动重试 (第 1/3 次)]  [取消]
  ↓  8s 倒计时结束
[查询中 ████████░░  72%]  →  成功 / 再次失败 → 第 2 次重试 (16s)
  ↓  3 次全部失败
[❌ 查询失败: 请检查网络或稍后重试]  [手动重试] [返回]
```

### 3.1.4 全局加载与乐观更新

**全局请求状态栏** (`components/common/GlobalLoadingBar.vue`):
- 使用 Element Plus 的 `el-progress` 或自定义顶部细进度条（类似 NProgress）
- 监听 Axios 请求计数：`pendingRequests` ref，请求 +1，响应/错误 -1
- 仅当请求 > 500ms 时才显示，避免闪烁

**乐观更新流程**（工单状态流转示例）:
```
用户点击 [受理] → 前端立即更新 UI（状态: New→Confirmed）
  → 乐观标记: isOptimistic = true
  → PUT /api/Tickets/:id/status { action: 'accept' }
    ├─ 成功 → 清除乐观标记, invalidateQueries(['tickets'])
    └─ 失败 → 回滚状态 (New), ElNotification.error('操作失败: ...')
```

---

## 3.2 测试架构与质量门禁

### 3.2.1 三层测试金字塔

```
          ╱─────╲
         ╱  E2E  ╲          Playwright — 5 条关键用户路径
        ╱─────────╲
       ╱  组件测试  ╲        Vitest + Vue Test Utils — 6 个核心业务组件
      ╱─────────────╲
     ╱   单元测试     ╲      Vitest — 8 个 Pinia Store + utils 工具函数
    ╱─────────────────╲
```

#### L1: 单元测试（Vitest）

| 测试对象 | 测试内容 | 文件 |
|---------|---------|------|
| `useAuthStore` | login/logout/refresh Token 流程，权限检查 getter | `src/__tests__/stores/auth.test.ts` |
| `useTicketStore` | fetchTickets, updateTicketStatus (含乐观更新回滚), filters getter | `src/__tests__/stores/ticket.test.ts` |
| `useComplianceStore` | checkCompliance 成功/失败/缓存命中 | `src/__tests__/stores/compliance.test.ts` |
| `useInspectionStore` | createPlan, executePlan 进度状态 | `src/__tests__/stores/inspection.test.ts` |
| `constants.ts` | TICKET_ACTIONS_BY_STATUS 状态机完整性，statusColorMap 覆盖所有枚举值 | `src/__tests__/utils/constants.test.ts` |
| `format.ts` | 日期格式化，合规率百分比，严重程度标签 | `src/__tests__/utils/format.test.ts` |

#### L2: 组件测试（Vitest + Vue Test Utils）

| 组件 | 测试用例 |
|------|---------|
| `ComplianceResultCard` | 正常结果渲染、loading 状态、错误状态、空 regulations、含 HallucinatedRegulations 警告 |
| `TicketStatusBadge` | 所有 7 种状态的正确颜色和文字渲染 |
| `TicketActionButtons` | 每种状态下显示正确的按钮组合、执行 action 后 emit 正确事件 |
| `StatCard` | 所有数值/趋势/颜色变体 |
| `ChemicalSelector` | 搜索过滤、选择事件、清空操作 |
| `ErrorBoundary` | 子组件抛出错误后展示 fallback、retry 按钮恢复 |

#### L3: E2E 测试（Playwright + MSW）

**5 条关键用户路径**:

| # | 路径 | 步骤 |
|---|------|------|
| P1 | 登录 → 合规自查 → 查看结果 | ① 登录 admin ② 进入合规自查 ③ 输入"苯和丙酮能同库吗" ④ 等待结果渲染 ⑤ 验证法规引用于面板和 LLM 解释面板均有内容 |
| P2 | 巡检执行 → 生成工单 → 工单流转 | ① 进入巡检列表 ② 执行"甲类仓库周检" ③ 等待完成 ④ 进入工单列表验证新工单出现 ⑤ 受理→开始→完成 ⑥ 验证状态正确流转 |
| P3 | 仪表盘数据聚合 | ① 登录 ② 验证 4 个统计卡片数据 ③ 验证图表渲染 ④ 点击"最近计划"跳转 |
| P4 | 资产台账 → 自动扫描 | ① 进入资产列表 ② 点击"自动扫描" ③ 等待结果 ④ 验证新 Findings 出现 |
| P5 | 审计日志查看 | ① 以 admin 登录 ② 进入审计日志 ③ 应用筛选条件 ④ 验证列表刷新 ⑤ 点击"验证哈希链" |

### 3.2.2 测试目录结构

```
agent1-web/
├── src/
│   └── __tests__/
│       ├── stores/            # L1 单元测试
│       │   ├── auth.test.ts
│       │   ├── ticket.test.ts
│       │   ├── compliance.test.ts
│       │   └── inspection.test.ts
│       ├── components/        # L2 组件测试
│       │   ├── ComplianceResultCard.test.ts
│       │   ├── TicketStatusBadge.test.ts
│       │   ├── TicketActionButtons.test.ts
│       │   └── StatCard.test.ts
│       └── utils/
│           ├── constants.test.ts
│           └── format.test.ts
├── e2e/                       # L3 E2E
│   ├── compliance-check.spec.ts
│   ├── inspection-ticket-flow.spec.ts
│   ├── dashboard.spec.ts
│   ├── asset-scan.spec.ts
│   └── audit-log.spec.ts
├── playwright.config.ts
└── vitest.config.ts
```

### 3.2.3 覆盖率目标与 CI 门禁

| 指标 | 目标 | CI 阻断条件 |
|------|------|-----------|
| 单元测试覆盖率 (Store + Utils) | ≥ 85% | < 80% 阻断 |
| 组件测试覆盖率 | ≥ 75% | < 70% 阻断 |
| E2E 关键路径 | 5/5 通过 | 任一条失败阻断 |
| TypeScript 编译 | 0 error | 任何 error 阻断 |
| ESLint | 0 error, ≤ 5 warning | error > 0 阻断 |

**CI 命令** (`.github/workflows/ci.yml` 或本地):
```bash
npm run test:unit          # vitest run --coverage
npm run test:e2e           # playwright test
npm run type-check         # vue-tsc --noEmit
npm run lint               # eslint src/ --ext .ts,.vue
```

---

## 3.3 非功能需求：性能、可访问性、国际化深度

### 3.3.1 性能预算

| 指标 | 目标值 | 测量工具 |
|------|--------|----------|
| FCP (First Contentful Paint) | < 1.5s | Lighthouse / Web Vitals |
| LCP (Largest Contentful Paint) | < 2.5s | Lighthouse |
| TBT (Total Blocking Time) | < 200ms | Lighthouse |
| 首屏 JS 体积 | < 300KB (gzip) | `vite build --report` |
| LLM 推理进度反馈 | 首次渲染 < 100ms | 手动计时 |

**Vite 分包策略** (`vite.config.ts`):
```typescript
build: {
  rollupOptions: {
    output: {
      manualChunks: {
        'element-plus': ['element-plus'],
        'echarts': ['echarts', 'vue-echarts'],
        'vue-vendor': ['vue', 'vue-router', 'pinia'],
        'markdown': ['markdown-it', 'highlight.js'],
      },
    },
  },
  chunkSizeWarningLimit: 500,
}
```

**路由懒加载确认**: 所有页面组件必须使用动态导入 `() => import('@/pages/XxxPage.vue')`，禁止顶层静态 import。

**ECharts 按需引入**（`components/charts/BaseEChart.vue` 中）:
```typescript
import { use } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import { LineChart, PieChart, GaugeChart, BarChart } from 'echarts/charts';
import { GridComponent, TooltipComponent, LegendComponent, TitleComponent } from 'echarts/components';

use([CanvasRenderer, LineChart, PieChart, GaugeChart, BarChart,
     GridComponent, TooltipComponent, LegendComponent, TitleComponent]);
```

### 3.3.2 虚拟滚动

当工单列表或资产列表超过 100 条时，启用虚拟滚动：
- **优先方案**: Element Plus 2.8+ 的 `el-table-v2` 虚拟化表格组件
- **降级方案**: `vue-virtual-scroller` (`RecycleScroller` 组件)
- **触发条件**: 列表数据 > 100 条 或 用户手动开启

```vue
<!-- 示例: 使用 el-table-v2 -->
<el-table-v2
  v-if="tickets.length > 100"
  :columns="columns"
  :data="tickets"
  :width="1200"
  :height="600"
  fixed
/>
<el-table v-else :data="tickets">
  <!-- 普通表格，< 100 条 -->
</el-table>
```

### 3.3.3 可访问性 (A11y)

| 规则 | 要求 | 检查方式 |
|------|------|---------|
| 图标按钮 `aria-label` | 所有不含文本的 `<el-button icon>` 必须设置 `aria-label` | ESLint `vuejs-accessibility` 插件 |
| 表单 Label 关联 | `el-form-item` 必须使用 `label` 属性，自动生成 `<label>` 关联 | 组件强制 |
| 键盘导航 | Tab 可进入所有交互元素，Enter/Space 激活按钮，Escape 关闭弹窗 | 手动测试 |
| 焦点管理 | 弹窗/Drawer 打开后焦点移入，关闭后焦点回归触发元素 | `el-dialog` 自带 |
| 色彩对比度 | 状态标签文字与背景对比度 ≥ 4.5:1 | Lighthouse |
| 屏幕阅读器 | 仪表盘关键数据使用 `aria-live="polite"` 动态播报 | 手动测试 |

### 3.3.4 国际化完整方案

**依赖**: `vue-i18n` ^9.x

**初始化** (`src/i18n/index.ts`):
```typescript
import { createI18n } from 'vue-i18n';
import zhCN from '@/locales/zh-CN';

const i18n = createI18n({
  legacy: false,          // Composition API 模式
  locale: 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: { 'zh-CN': zhCN },
});

export default i18n;
```

**使用方式**:
- 模板: `{{ $t('compliance.check.queryPlaceholder') }}`
- `<script setup>`: `const { t } = useI18n(); t('ticket.status.New')`
- Element Plus 国际化: 已在 `main.ts` 中配置 `zhCn` locale

**国际化 Key 命名规范**: `{模块}.{组件}.{字段}`, 示例:
```typescript
// locales/zh-CN.ts
export default {
  common: { save: '保存', cancel: '取消', delete: '删除', retry: '重试' },
  auth: { login: '登录', logout: '登出', username: '用户名', password: '密码' },
  compliance: {
    check: {
      title: '合规自查', queryPlaceholder: '输入您要查询的合规问题...',
      quickTemplates: '快捷模板', sending: '推理中...',
    },
  },
  ticket: {
    status: {
      New: '新建', Confirmed: '已确认', InProgress: '处理中',
      Remediated: '已整改', VerifiedClosed: '已验证关闭',
      Closed: '已关闭', FalsePositive: '误报',
    },
    priority: { Critical: '严重', High: '高', Medium: '中', Low: '低' },
  },
  // ... 所有硬编码中文必须搬迁至此
};
```

---

## 3.4 开发体验（DX）与工程化设施

### 3.4.1 自动生成 API 类型与请求函数

**工具选型**: `openapi-typescript` + 自定义 `openapi-fetch` wrapper

**工作流**:
```
swagger.json (后端)  →  openapi-typescript  →  src/types/api.generated.ts
                     →  自定义脚本          →  src/api/client.ts (类型安全请求函数)
```

**`scripts/generate-api.ts`** (Node.js 脚本，放入 `scripts/` 目录作为开发者工具):
```typescript
// 从后端 swagger.json 生成 TypeScript 类型和请求函数
// 用法: npx tsx scripts/generate-api.ts
//
// 步骤:
// 1. 读取 http://localhost:5000/swagger/v1/swagger.json
// 2. 使用 openapi-typescript 生成 types
// 3. 生成类型安全的 API 客户端函数
//
// 生成产物:
//   src/types/api.generated.ts  — 自动生成的类型 (勿手动编辑)
//   src/api/endpoints.ts        — 类型安全的请求函数
```

**package.json scripts 新增**:
```json
{
  "gen:api": "tsx scripts/generate-api.ts",
  "gen:api:watch": "tsx watch scripts/generate-api.ts"
}
```

> **注意**: 此脚本仅读取 swagger.json 生成类型文件，不管理应用服务生命周期，不运行外部进程，符合 `scripts/` 目录开发者工具箱定位。

### 3.4.2 组件文档（Storybook）

**安装**: Storybook 8.x for Vite + Vue 3

**覆盖组件**（至少 6 个核心组件）:

| 组件 | Story 覆盖 |
|------|----------|
| `StatCard` | Default, UpTrend, DownTrend, WithIcon, Loading |
| `TicketStatusBadge` | AllStates (7 variants), WithLabel, IconOnly |
| `ComplianceResultCard` | SuccessResult, WithWarnings, WithHallucinations, Loading, Error |
| `TicketActionButtons` | NewStatus, ConfirmedStatus, InProgressStatus, RemediatedStatus, TerminalStatus |
| `ChemicalSelector` | Empty, WithOptions, Selected, Multiple, Loading |
| `LlmExplanationPanel` | PlainText, Markdown, WithWarnings, LongContent |

**目录结构**:
```
agent1-web/
├── .storybook/
│   ├── main.ts
│   └── preview.ts
└── src/
    └── components/
        ├── dashboard/StatCard.stories.ts
        ├── ticket/TicketStatusBadge.stories.ts
        ├── compliance/ComplianceResultCard.stories.ts
        └── ...
```

### 3.4.3 Git Hooks 与代码规范自动化

| 工具 | 时机 | 操作 |
|------|------|------|
| `husky` | pre-commit | 触发 `lint-staged` |
| `lint-staged` | 暂存文件 | `eslint --fix` + `prettier --write` + `vue-tsc --noEmit` |
| `commitlint` | commit-msg | 校验 Conventional Commits 格式 |

**`.lintstagedrc.js`**:
```javascript
export default {
  '*.{ts,vue}': ['eslint --fix', 'prettier --write'],
  '*.{css,json,md}': ['prettier --write'],
};
```

**`.husky/pre-commit`**:
```bash
#!/bin/sh
npx lint-staged
npx vue-tsc --noEmit --project tsconfig.json
```

### 3.4.4 环境变量设计

| 变量 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `VITE_API_BASE_URL` | string | `http://localhost:5000` | 后端 API 地址 |
| `VITE_ENABLE_MOCK` | boolean | `false` | 启用 MSW Mock（开发用） |
| `VITE_APP_TITLE` | string | `Agent1 化工合规平台` | 浏览器标题 |
| `VITE_ENABLE_DEVTOOLS` | boolean | `false` | 启用 Vue DevTools |
| `VITE_SENTRY_DSN` | string | — | 错误监控 DSN（生产环境） |
| `VITE_LLM_TIMEOUT` | number | `120000` | LLM 推理超时 (ms) |

**`.env` 文件约定**:
- `.env` — 通用默认值
- `.env.mock` — Mock 模式覆盖（`VITE_ENABLE_MOCK=true`）
- `.env.development` — 开发环境（可提交）
- `.env.production` — 生产环境（不提交敏感值，由 CI 注入）
- `.env.local` — 本地覆盖（`.gitignore`，不提交）

---

## 3.5 安全加固与前端防御

### 3.5.1 Token 存储安全升级

| 方案 | 安全性 | 实现复杂度 | 推荐 |
|------|--------|----------|------|
| `localStorage` 明文（当前） | ⚠️ 低 (XSS 可读) | 低 | 不推荐 |
| 内存 + `localStorage` 加密 | 🟡 中 (XSS 仍可读内存) | 中 | 当前阶段 |
| HttpOnly Cookie + SameSite=Strict | 🟢 高 (JS 不可读) | 高 (需同域) | 生产目标 |

**当前阶段实现** (内存 + 加密 localStorage):
```typescript
// stores/auth.ts — 增强 Token 存储
import { encryptToken, decryptToken } from '@/utils/crypto';

// 写入: 用 Web Crypto API 加密后写入 localStorage
function persistTokens(accessToken: string, refreshToken: string) {
  const encrypted = encryptToken(JSON.stringify({ accessToken, refreshToken }));
  localStorage.setItem('auth_tokens', encrypted);
}

// 读取: 从 localStorage 解密，Token 本身仅存在于 Pinia Store 内存
// refreshToken 保留在 localStorage（加密），accessToken 仅内存
```

**生产目标**: 推动后端将 JWT 写入 `HttpOnly; SameSite=Strict; Secure` Cookie，前端无需管理 Token。

### 3.5.2 markdown-it 安全配置（强制约束）

```typescript
// composables/useMarkdown.ts
import MarkdownIt from 'markdown-it';

const md = new MarkdownIt({
  html: false,          // 🔴 强制: 禁止原始 HTML，防 XSS
  linkify: true,        // 🟡 允许: 自动链接，但需白名单过滤
  breaks: true,         // 允许换行转 <br>
  typographer: false,   // 禁用排版替换（避免意外字符）
});

// 链接白名单: 仅允许 http/https，禁止 javascript: / data:
md.validateLink = (url: string) => /^https?:\/\//.test(url);
```

**使用约束**: 所有渲染 LLM 返回的 Markdown 内容时，**必须**使用此配置的 `md` 实例，禁止使用 `v-html` 直接渲染未经处理的 Markdown 字符串。

### 3.5.3 内容安全策略 (CSP) 部署要求

建议运维侧在 Nginx / Kestrel 层配置以下 CSP Header:
```
Content-Security-Policy:
  default-src 'self';
  script-src 'self' 'unsafe-inline';
  style-src 'self' 'unsafe-inline';
  img-src 'self' data: blob:;
  connect-src 'self' http://localhost:5000;
  font-src 'self';
```

> 前端在 `index.html` 中通过 `<meta http-equiv="Content-Security-Policy">` 提供默认值，生产环境由网关覆盖。

### 3.5.4 敏感信息保护

- **禁止 console.log 泄露**: 生产构建通过 `vite.config.ts` 的 `esbuild.drop: ['console', 'debugger']` 移除所有 console
- **脱敏日志**: 前端上报的错误日志中，URL 参数和 Token 需脱敏后上报
- **合规查询历史**: 存储在 `localStorage` 时使用 AES-GCM 加密（通过 `pinia-plugin-persistedstate` 的 `serializer` 配置）

---

## 3.6 状态持久化与缓存策略细化

### 3.6.1 pinia-plugin-persistedstate 统一管理

```typescript
// stores/plugins.ts
import { createPersistedState } from 'pinia-plugin-persistedstate';

// 全局配置
const persistedState = createPersistedState({
  storage: localStorage,
  // 序列化时加密敏感字段
  serializer: {
    serialize: (value) => {
      // 敏感 Store (如 auth) 使用 AES-GCM 加密
      return JSON.stringify(value);
    },
    deserialize: (value) => {
      return JSON.parse(value);
    },
  },
});
```

**各 Store 持久化策略**:

| Store | 持久化 | 内容 | TTL | 加密 |
|-------|--------|------|-----|------|
| `authStore` | ✅ | `refreshToken` (加密), `username`, `role` | — | ✅ AES-GCM |
| `complianceStore` | ✅ | `history` (最近 20 条查询) | 7 天 | ✅ AES-GCM |
| `inspectionStore` | ❌ | — | — | — |
| `ticketStore` | ❌ | — | — | — |
| `assetStore` | ❌ | — | — | — |
| `dashboardStore` | ❌ | — | — | — |
| `auditStore` | ❌ | — | — | — |
| `systemStore` | ❌ | — | — | — |

### 3.6.2 Vue Query 缓存配置规范

```typescript
// Vue Query 全局默认配置 (在 main.ts 中初始化)
import { VueQueryPlugin } from '@tanstack/vue-query';

app.use(VueQueryPlugin, {
  queryClientConfig: {
    defaultOptions: {
      queries: {
        staleTime: 30_000,           // 30s 内视为新鲜
        gcTime: 5 * 60_000,           // 5 分钟垃圾回收
        retry: 2,                     // 失败重试 2 次
        refetchOnWindowFocus: false,  // 窗口聚焦不重新请求
      },
      mutations: {
        retry: 1,
      },
    },
  },
});
```

**各模块缓存策略**:

| 查询 | staleTime | gcTime | 手动失效触发 |
|------|-----------|--------|------------|
| `['compliance', 'summary']` | 60s | 5min | 自动扫描完成后 |
| `['tickets', 'list']` | 10s | 3min | 状态流转成功后 `invalidateQueries({ queryKey: ['tickets'] })` |
| `['inspection', 'plans']` | 30s | 5min | 创建/删除计划后 |
| `['assets', 'list']` | 60s | 5min | 扫描完成后 |
| `['health']` | 10s | 1min | 手动刷新 |

**Query Key 命名约定**: `[domain, resource, ...params]`
```typescript
// 示例
useQuery({ queryKey: ['tickets', 'list'], queryFn: fetchTickets });
useQuery({ queryKey: ['tickets', 'detail', id], queryFn: () => fetchTicketDetail(id) });
useQuery({ queryKey: ['inspection', 'plans', planId], queryFn: () => fetchPlan(planId) });
```

---

## 3.7 UI 交互细节补充

### 3.7.1 骨架屏（Skeleton）

替代生硬的 `v-loading` 转圈，为核心数据区域提供骨架屏：

```
仪表盘骨架屏:
┌─────────────────────────────────────────────────────────┐
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐   │
│  │ ████████ │ │ ████████ │ │ ████████ │ │ ████████ │   │
│  │ ████     │ │ ████     │ │ ████     │ │ ████     │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘   │
│  ┌──────────────────┐ ┌──────────────────┐             │
│  │ ████████████████ │ │ ████████████████ │             │
│  │ ████████████████ │ │ ████████████████ │             │
│  └──────────────────┘ └──────────────────┘             │
└─────────────────────────────────────────────────────────┘

工单列表骨架屏:
┌────┬──────────────────┬────────┬────────┬────────┬────────┐
│ ██ │ ████████████████ │ ██████ │ ██████ │ ██████ │ ██████ │
│ ██ │ ████████████████ │ ██████ │ ██████ │ ██████ │ ██████ │
│ ██ │ ████████████████ │ ██████ │ ██████ │ ██████ │ ██████ │
└────┴──────────────────┴────────┴────────┴────────┴────────┘
```

**实现**: 使用 Element Plus 的 `el-skeleton` 组件封装 `components/common/SkeletonCard.vue` 和 `components/common/SkeletonTable.vue`。

### 3.7.2 空状态与首次引导

| 场景 | 设计 |
|------|------|
| 合规自查页（首次） | 展示引导性文字 + 3 个示例查询按钮（点击即填入输入框）: "硫酸和硝酸能同库储存吗？" / "甲醇属于什么危险类别？" / "甲类仓库安全距离要求" |
| 工单列表（空） | 插画 + "暂无整改工单，合规状态良好 🎉" + 自动扫描快捷入口 |
| 巡检计划（空） | 插画 + "还没有巡检计划" + [创建第一个计划] 按钮 |
| 审计日志（空） | "暂无操作记录" + 筛选条件提示 |

**实现**: 封装 `components/common/EmptyState.vue`，Props: `image`, `title`, `description`, `actionText`, `actionRoute`

### 3.7.3 通知中心 (`components/common/NotificationCenter.vue`)

```
┌─────────────────────────────────┐
│  🔔 通知中心              [清空]│
├─────────────────────────────────┤
│  ⚠️ 自动扫描完成               │
│     发现 2 项不合规，已生成工单 │
│     2 分钟前                    │
├─────────────────────────────────┤
│  🎫 工单 #3 已分配给王五        │
│     消防通道标识不清            │
│     15 分钟前                   │
├─────────────────────────────────┤
│  ✅ 甲类仓库周检执行完成        │
│     合规率: 80%                 │
│     1 小时前                    │
└─────────────────────────────────┘
```

**触发时机**: 自动扫描完成、工单指派、巡检执行完成、消息推送
**存储**: Pinia `useNotificationStore`，最多保留 50 条，7 天 TTL

### 3.7.4 面包屑导航

```typescript
// router/index.ts — 路由 meta 扩展
const routes = [
  {
    path: '/compliance',
    name: 'Compliance',
    meta: { title: '合规自查', breadcrumb: ['首页', '合规自查'] },
  },
  {
    path: '/tickets/:id',
    name: 'TicketDetail',
    meta: { title: '工单详情', breadcrumb: ['首页', '工单管理', '工单详情'] },
  },
  // ...
];
```

**组件**: `components/common/Breadcrumb.vue`，使用 `useRouter().currentRoute.value.matched` 自动推导面包屑路径。面包屑末级为纯文本（不可点击），前级为 `<router-link>`。

### 3.7.5 侧边栏响应式

```
宽度 ≥ 1024px:
┌──────┬──────────────────────────────┐
│      │                              │
│ 完整 │        主内容区               │
│ 侧栏 │                              │
│      │                              │
└──────┴──────────────────────────────┘

宽度 < 1024px:
┌──┬─────────────────────────────────┐
│📋│  📊 总览仪表盘                  │
│🔍│  ┌──────────┐ ┌──────────┐     │
│📋│  │  总资产  │ │  合规率  │     │
│🎫│  └──────────┘ └──────────┘     │
└──┴─────────────────────────────────┘
  仅图标      主内容区自适应
  悬停展开
```

**实现**: 使用 `el-menu` 的 `collapse` 属性，监听 `window.matchMedia('(max-width: 1024px)')` 自动折叠。折叠模式下仅显示图标，hover 时弹出子菜单文字。

---

## 补充清单：Sprint 拆分建议

| Sprint | 内容 | 交付物 |
|--------|------|--------|
| **Sprint 0** (基础设施) | Axios 拦截器增强、ErrorBoundary、EmptyState、Skeleton 组件 | `lib/axios.ts` 增强, `ErrorBoundary.vue`, `EmptyState.vue`, `SkeletonCard.vue` |
| **Sprint 1** (核心页面) | 登录页、仪表盘、合规自查页、工单列表/详情 (基于原文档 2.3) | 6 个页面组件 + 关联业务组件 |
| **Sprint 2** (测试 + 错误韧性) | L1 单元测试 (Stores)、L2 组件测试、重试策略 `useRetry`、乐观更新 | `__tests__/` + `useRetry.ts` |
| **Sprint 3** (巡检 + 资产) | 巡检计划 CRUD、巡检执行页、资产台账、自动扫描 | 剩余页面组件 |
| **Sprint 4** (E2E + DX) | Playwright E2E (5 条路径)、Storybook (6 个组件)、API 类型自动生成 | `e2e/` + `.storybook/` + `scripts/generate-api.ts` |
| **Sprint 5** (加固) | 国际化全覆盖、A11y 审查、虚拟滚动、安全强化 (CSP/markdown-it) | `locales/zh-CN.ts` 完整、安全配置文档 |
| **Sprint 6** (审计 + 系统) | 审计日志页、系统状态页、面包屑、通知中心、响应式适配 | 最后 2 个页面 + 全局组件 |

---

## 补充：关键文件创建清单

| 文件 | 所属 Sprint | 说明 |
|------|-----------|------|
| `src/components/common/ErrorBoundary.vue` | S0 | 全局错误边界 |
| `src/components/common/EmptyState.vue` | S0 | 空状态占位 |
| `src/components/common/SkeletonCard.vue` | S0 | 卡片骨架屏 |
| `src/components/common/SkeletonTable.vue` | S0 | 表格骨架屏 |
| `src/components/common/GlobalLoadingBar.vue` | S0 | 全局顶部进度条 |
| `src/components/common/Breadcrumb.vue` | S6 | 面包屑导航 |
| `src/components/common/NotificationCenter.vue` | S6 | 通知中心 |
| `src/composables/useRetry.ts` | S2 | 指数退避重试 |
| `src/composables/useMarkdown.ts` | S1 | 安全 Markdown 渲染 |
| `src/stores/plugins.ts` | S0 | 持久化插件配置 |
| `src/i18n/index.ts` | S5 | 国际化初始化 |
| `src/locales/zh-CN.ts` | S5 | 国际化字符串（贯穿全部 Sprint） |
| `src/utils/crypto.ts` | S0 | Web Crypto API 加密工具 |
| `scripts/generate-api.ts` | S4 | API 类型自动生成 |
| `.storybook/main.ts` | S4 | Storybook 配置 |
| `.lintstagedrc.js` | S0 | lint-staged 配置 |
| `vitest.config.ts` | S2 | Vitest 配置 |
| `playwright.config.ts` | S4 | Playwright 配置 |

---

> **文档结束** — 本文档定义了 Agent1 前端开发的完整架构（含生产加固），所有页面、组件、Store、路由均以此为依据进行拆分和实现。实现时应遵循 2.6 节的编码规范和技术约束，并按补充清单的 Sprint 节奏分阶段交付。

---

# 修正记录

## v1.0.1（2026-07-10）

**来源**：[前后端高风险点对齐审查报告](./前后端高风险点对齐审查报告.md)

### 修正 1：工单状态枚举对齐

**问题**：前端工单相关组件、Mock 数据、常量中使用的状态值来自 `FindingStatus` 枚举（`Confirmed`、`Remediated`、`VerifiedClosed`、`FalsePositive`），与后端 `TicketFollowupModule.TicketStatus` 枚举（`Accepted`、`Completed`、`Verified`、`Rejected`）不一致。

**修改**：
- `src/types/api.ts` — 新增 `TicketStatus` 联合类型（`'New' | 'Accepted' | 'InProgress' | 'Completed' | 'Verified' | 'Closed' | 'Rejected'`）
- `src/utils/constants.ts` — 新建，包含 `TICKET_ACTIONS_BY_STATUS`、`TICKET_STATUS_COLOR_MAP`、`TICKET_STATUS_LABEL_MAP`、`ACTION_TO_STATUS`、`isTerminalStatus()`
- `src/mocks/data/tickets.ts` — Mock 数据 `Confirmed` → `Accepted`，`stateTransitions` 和 `actionToStatus` 全部更新
- `src/components/ticket/TicketStatusBadge.vue` — 新建，Props 类型使用 `TicketStatus`
- `src/components/ticket/TicketActionButtons.vue` — 新建，按钮逻辑对齐新状态机
- `src/mocks/README.md` — 文档中的状态链表述更新

**状态映射**：
| 旧值 (FindingStatus) | 新值 (TicketStatus) |
|----------------------|---------------------|
| `Confirmed` | `Accepted` |
| `Remediated` | `Completed` |
| `VerifiedClosed` | `Verified` |
| `FalsePositive` | `Rejected` |

> **注意**：仪表盘合规总览（`compliance.ts` 中的 `findingsByStatus`）使用 `FindingStatus` 枚举，这是合规发现的状态体系，与工单（Ticket）是两套独立体系，本次修正**不涉及**。

### 修正 2：viewer 权限收缩

**问题**：前端设计文档授予 viewer 角色 6 项业务模块只读权限，但后端所有业务 Controller 均使用 `[Authorize(Policy = "Auditor")]`，viewer 调用任何业务 API 都会收到 401/403。

**修改**：
- `src/types/api.ts` — 新增 `UserRole` 类型
- `src/stores/auth.ts` — 新建，`hasPermission()` viewer 对所有业务角色检查返回 `false`
- `src/router/index.ts` — 所有业务路由 `meta.roles` 限定为 `['admin', 'auditor']`，`beforeEach` 守卫拦截 viewer 跳转至 `/403`
- `src/components/layout/AppSidebar.vue` — 新建，viewer 角色不显示业务菜单
- `src/App.vue` — 更新为侧边栏布局，Login/403 路由无侧边栏
- `src/pages/ForbiddenPage.vue` — 新建 403 页面
- `docs/frontend/Agent1前端架构设计方案.md` — 2.1 节权限矩阵 viewer 列全部改为 ❌
- `docs/frontend/前后端高风险点对齐审查报告.md` — 方案 A 标记为"已执行"

**viewer 当前体验**：仅可访问 `/login` 和 `/403`，登录后所有业务路由均被守卫拦截。待后端为 GET 端点增加 `[Authorize(Policy = "Viewer")]` 后，取消 `auth.ts` 中 viewer 的权限注释即可恢复只读访问。
