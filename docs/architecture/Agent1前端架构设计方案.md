# Agent1 — 化工合规 AI Agent 前端架构设计方案

> **文档版本**：v2.0
> **创建日期**：2026-06-24
> **更新日期**：2026-06-24（v2.0：融合 DeepSeek 设计评审，新增 Design Token、页面交互规范、新页面、Docker 部署、增强测试）
> **适用范围**：Agent1 化工合规 AI Agent 前端系统（Web SPA）
> **后端版本依赖**：Agent1.Api v2.5.0+

---

## 目录

- [一、系统架构总览](#一系统架构总览)
- [二、UI 设计方案](#二ui-设计方案)
  - [2.1 设计原则](#21-设计原则)
  - [2.2 页面地图](#22-页面地图)
  - [2.3 页面详细设计](#23-页面详细设计)
  - [2.4 视觉风格指南](#24-视觉风格指南)
  - [2.5 页面交互规范](#25-页面交互规范)
- [三、前端技术栈选型](#三前端技术栈选型)
- [四、前端项目结构与模块划分](#四前端项目结构与模块划分)
- [五、API 接口契约定义](#五api-接口契约定义)
- [六、前后端并行开发协作策略](#六前后端并行开发协作策略)
  - [6.9 前端 Docker + Nginx 生产部署](#69-前端-docker--nginx-生产部署)
  - [6.10 增强测试策略](#610-增强测试策略)

---

## 一、系统架构总览

### 1.1 全栈交互全景图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          用户终端层                                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────────────┐   │
│  │ 安监部门   │  │ 园区管理员 │  │ 企业安全员 │  │ 外部系统 (ERP/WMS/EHS)│   │
│  │ (auditor) │  │ (admin)   │  │ (viewer)  │  │ (API 集成)           │   │
│  └─────┬─────┘  └─────┬─────┘  └─────┬─────┘  └──────────┬───────────┘   │
│        │              │              │                    │               │
└────────┼──────────────┼──────────────┼────────────────────┼───────────────┘
         │              │              │                    │
         ▼              ▼              ▼                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        前端 SPA (React 18 + TypeScript)                  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                       路由层 (React Router v6)                    │  │
│  │  /login  /dashboard  /compliance  /inspection  /tickets  /admin  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      状态管理层                                   │  │
│  │  Auth Store (Zustand)  │  API Cache (TanStack Query)             │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      HTTP 客户端 (Axios)                          │  │
│  │  JWT 拦截器 │ 自动刷新 │ 错误处理 │ 请求/响应转换                  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
         │                                                    ▲
         │  HTTPS (JWT Bearer Token)                          │ SSE
         ▼                                                    │
┌─────────────────────────────────────────────────────────────────────────┐
│                     Agent1.Api (.NET 8 Web API :5000)                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │   Auth   │ │Compliance│ │Inspection│ │ Tickets  │ │  Health  │    │
│  │Controller│ │Controller│ │Controller│ │Controller│ │  /metrics│    │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘ └──────────┘    │
│       │            │            │            │                         │
│  ┌────┴────────────┴────────────┴────────────┴────────────────────┐   │
│  │                   中间件管线                                     │   │
│  │  GlobalException → RequestId → RateLimiting → CORS → JWT Auth  │   │
│  │  → TokenBlacklist → Authorization → Controllers               │   │
│  └────────────────────────────────────────────────────────────────┘   │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │                    服务层 (Singleton DI)                        │   │
│  │  AgentDialog │ ChemicalComplianceTools │ LlmService            │   │
│  │  HybridKnowledgeBaseService │ InspectionOrchestrator           │   │
│  │  ComplianceRuleEngine │ AuditService │ MemoryCoordinator       │   │
│  └────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
         │              │              │
         ▼              ▼              ▼
┌────────────┐  ┌────────────┐  ┌──────────────┐
│ PostgreSQL │  │ llama.cpp  │  │ Prometheus + │
│ + pgvector │  │ (GGUF)     │  │ Grafana      │
└────────────┘  └────────────┘  └──────────────┘
```

### 1.2 前后端通信协议

| 层级 | 技术选择 | 说明 |
|------|---------|------|
| 传输协议 | HTTPS + JSON | RESTful API，非流式场景 |
| 认证方式 | JWT Bearer Token | Access Token 1h + Refresh Token 7d，Token Rotation |
| 实时通信 | SSE (Server-Sent Events) | LLM 流式输出场景（后续 Sprint） |
| 跨域策略 | CORS `http://localhost:5173` | 开发环境 Vite 默认端口 |
| 速率限制 | 100 req/min/IP+endpoint | 由后端 RateLimitingMiddleware 保证 |
| 文件上传 | multipart/form-data | GHS 标签图片识别场景 |
| 日志追踪 | X-Request-Id 响应头 | 关联前后端全链路日志 |

### 1.3 已就绪的后端 API 清单（基线）

根据对后端代码的完整分析，现有 API 端点如下：

| 控制器 | 方法 | 路径 | 认证 | 说明 |
|--------|------|------|------|------|
| Auth | POST | `/api/Auth/login` | Anonymous | 登录获取 JWT |
| Auth | POST | `/api/Auth/refresh` | Anonymous | 刷新 Token |
| Auth | POST | `/api/Auth/logout` | Authorized | 登出 + 黑名单 |
| Compliance | GET | `/api/Compliance/summary` | Auditor | 合规总览仪表盘 |
| Compliance | POST | `/api/Compliance/check` | Auditor | 提交合规审核 |
| Compliance | POST | `/api/Compliance/hazard/query` | Auditor | 危化品类别查询 |
| Compliance | POST | `/api/Compliance/storage/compatibility` | Auditor | 储存兼容性检查 |
| Inspection | GET/POST | `/api/Inspection/plans` | Auditor | 巡检计划 CRUD |
| Inspection | GET | `/api/Inspection/plans/{id}` | Auditor | 计划详情 |
| Inspection | POST | `/api/Inspection/plans/{id}/execute` | Auditor | 执行巡检 |
| Inspection | GET | `/api/Inspection/rounds/{id}` | Auditor | 巡检轮次结果 |
| Inspection | GET | `/api/Inspection/reports/{id}` | Auditor | 巡检报告 |
| Inspection | GET | `/api/Inspection/reports/{id}/export` | Auditor | 导出报告 JSON |
| Inspection | GET | `/api/Inspection/assets` | Auditor | 资产台账列表 |
| Inspection | POST | `/api/Inspection/scan` | Auditor | 自动扫描 |
| Inspection | POST | `/api/Inspection/quick-check` | Auditor | 快速检查 |
| Tickets | GET | `/api/Tickets` | Auditor | 工单列表 |
| Tickets | PUT | `/api/Tickets/{id}/status` | Auditor | 工单状态流转 |
| Health | GET | `/health` | Anonymous | 健康检查 |
| Health | GET | `/metrics` | Anonymous | Prometheus 指标 |
| Cache | GET | `/cache/stats` | Anonymous | 缓存统计 |
| Cache | POST | `/cache/clear` | Anonymous | 清除缓存 |
| KB | POST | `/knowledgebase/incremental-update` | Anonymous | 知识库增量更新 |
| Memory | GET | `/memory/stats` | Anonymous | 记忆统计 |
| Memory | GET | `/memory/long-term/search` | Anonymous | 长期记忆搜索 |

---

## 二、UI 设计方案

### 2.1 设计原则

基于项目设计文档 V1.0 的核心理念，前端 UI 遵循以下原则：

| 原则 | 说明 | 对应用户体验 |
|------|------|-------------|
| 🎯 **简洁优先** | 默认展示核心结论，详细过程按需展开 | 安监人员快速获取合规结论 |
| 🧠 **智能记忆** | 记住用户身份、偏好设置、最近查询 | 减少重复输入 |
| ⚡ **即时反馈** | 长时间 LLM 推理显示进度指示器 | 避免用户焦虑等待 |
| 🔴 **风险可视化** | 不合规项红色高亮，合规率仪表盘 | 一目了然的风险态势 |
| 📋 **报告导出** | 一键生成 PDF/Markdown 巡检报告 | 对接安监存档要求 |

### 2.2 页面地图 (Site Map)

```
/login                          # 登录页
/dashboard                      # 合规态势总览仪表盘
/compliance                     # 合规审核
  /compliance/check             #   提交合规查询
  /compliance/hazard            #   危化品类别查询
  /compliance/storage           #   储存兼容性检查
/inspection                     # 巡检管理
  /inspection/plans             #   巡检计划列表
  /inspection/plans/:id         #   计划详情
  /inspection/plans/create      #   创建巡检计划
  /inspection/rounds/:id        #   巡检轮次结果
  /inspection/reports/:id       #   巡检报告
  /inspection/assets            #   资产台账
/knowledge-graph                # 知识图谱大屏
/emergency                      # 应急响应
/gpu-monitor                    # GPU 推理监控（admin）
/tickets                        # 整改工单
  /tickets/:id                  #   工单详情
/admin                          # 系统管理（admin 角色）
  /admin/knowledge-base         #   知识库管理
  /admin/cache                  #   缓存管理
  /admin/users                  #   用户管理（预留）
```

### 2.3 页面详细设计

#### 2.3.1 登录页 (`/login`)

```
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│                    🔬 Agent1                                  │
│              化工园区危化品合规审核系统                          │
│                                                              │
│     ┌──────────────────────────────────────┐                │
│     │  用户名                               │                │
│     │  ┌──────────────────────────────────┐│                │
│     │  │ admin                          ││                │
│     │  └──────────────────────────────────┘│                │
│     │  密码                                 │                │
│     │  ┌──────────────────────────────────┐│                │
│     │  │ ●●●●●●●●                      ││                │
│     │  └──────────────────────────────────┘│                │
│     │                                      │                │
│     │  ┌──────────────────────────────────┐│                │
│     │  │          登  录                  ││                │
│     │  └──────────────────────────────────┘│                │
│     └──────────────────────────────────────┘                │
│                                                              │
│      等保三级 · SHA256 审计 · 全链路可观测                       │
└──────────────────────────────────────────────────────────────┘
```

**交互要点**：
- 登录成功后存储 JWT 到 `localStorage`，跳转 `/dashboard`
- 失败显示 `用户名或密码错误`（不区分是用户名错还是密码错，安全）
- Token 过期自动触发刷新，刷新失败跳回登录页
- 按角色显示不同菜单：admin 可见 `/admin`，viewer 不可操作

#### 2.3.2 合规态势总览仪表盘 (`/dashboard`)

```
┌──────────────────────────────────────────────────────────────┐
│  🏭 Agent1 合规态势总览                    admin ▼  🔔  ⚙️   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ 资产总数  │ │ 合规率    │ │ 未关闭   │ │ 整改率    │       │
│  │   8      │ │  75.0%   │ │ 发现 4   │ │  60.0%   │       │
│  │ 园区化学品 │ │  ▲ 12%   │ │ ⚠ 待处理 │ │  ▲ 8%    │       │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘       │
│                                                              │
│  ┌─────────────────────────┐ ┌─────────────────────────┐    │
│  │     风险分布饼图         │ │   按严重级别分布柱状图    │    │
│  │   (ECharts 环形图)      │ │   (ECharts 柱状图)       │    │
│  │   合规 · 未知 · 高风险   │ │   Critical ████ 2       │    │
│  │    · 严重               │ │   High     ██ 1         │    │
│  │                         │ │   Medium   █ 1          │    │
│  └─────────────────────────┘ └─────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  最近不合规发现                    [查看全部 →]       │   │
│  │  ┌─────────────────────────────────────────────────┐ │   │
│  │  │ 🔴 苯 (甲类仓库A区) — 与丙酮同库储存违规       │ │   │
│  │  │    GB 15603-2022 §4.2.2 | 张三 | 未分配         │ │   │
│  │  ├─────────────────────────────────────────────────┤ │   │
│  │  │ 🟠 甲醇 (甲类仓库B区) — 超重大危险源临界量      │ │   │
│  │  │    GB 18218-2018 | 李四 | 确认中                │ │   │
│  │  └─────────────────────────────────────────────────┘ │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**数据来源**：`GET /api/Compliance/summary` + `GET /api/Inspection/assets`

#### 2.3.3 合规审核页 (`/compliance/check`)

这是核心交互页面，用户输入化学品相关问题，系统返回 AI 合规判断：

```
┌──────────────────────────────────────────────────────────────┐
│  ← 合规审核                                                   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  💬 输入你想查询的合规问题...                          │   │
│  │                                                      │   │
│  │  苯和丙酮能放在同一个仓库吗？                          │   │
│  │                                                      │   │
│  └──────────────────────────────────────────────────────┘   │
│                                              [提交审核]      │
│                                                              │
│  ┌─ 快捷查询 ──────────────────────────────────────────┐    │
│  │ [苯的危险类别] [甲醇重大危险源] [甲类仓库安全距离]     │    │
│  │ [硝酸储存条件]  [苯+丙酮兼容性]                       │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌─ AI 分析结果 ───────────────────────────────────────┐    │
│  │                                                     │    │
│  │  ⏳ 正在分析中... (预计 10-30 秒)                    │    │
│  │  ▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░ 40%                         │    │
│  │  已调用工具: CheckStorageCompatibility               │    │
│  │                                                     │    │
│  │  ── 完成后显示 ──                                   │    │
│  │                                                     │    │
│  │  【合规判断】否                                      │    │
│  │  【法规依据】GB 15603-2022 §4.2.2                    │    │
│  │  【违规点】苯(易燃液体)与丙酮(易燃液体)为禁忌配伍     │    │
│  │  【整改建议】立即分库储存，苯移至甲类仓库A区2号位     │    │
│  │                                                     │    │
│  │  已验证法规: GB 15603-2022, GB 30000.7-2013          │    │
│  │  使用工具: CheckStorageCompatibility, CheckHazard...  │    │
│  │  ⚠ 警告: 丙酮存量超危险源临界量80%                   │    │
│  │                                                     │    │
│  │               [📋 查看详细推理过程]                   │    │
│  │               [📄 导出为报告]                         │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌─ 历史记录 ──────────────────────────────────────────┐    │
│  │  🕐 06-24 15:30  苯+丙酮储存 → ❌ 不合规             │    │
│  │  🕐 06-24 14:10  甲醇危险类别 → 易燃液体 GB30000.7   │    │
│  │  🕐 06-24 11:22  甲类仓库消防通道 → 15米 GB50160     │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**交互要点**：
- **请求阶段**：显示进度条 + 工具调用实时反馈（目前后端是同步返回，后续可用 SSE 流式）
- **结果展示**：突出合规判断（红/绿色标签），法规引用可点击跳转到知识库原文
- **详细过程**：折叠面板展示工具调用参数、RAG 检索结果、反思纠错过程
- **长耗时处理**：LLM 推理可能需要 10-60 秒，显示预估等待时间，超时给出提示
- **缓存机制**：相同查询 5 分钟内命中 `ResponseCacheService` 直接返回，毫秒级响应

#### 2.3.4 巡检管理页 (`/inspection/plans`)

```
┌──────────────────────────────────────────────────────────────┐
│  ← 巡检计划管理                            [+ 创建巡检计划]    │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ 筛选 ──────────────────────────────────────────────┐    │
│  │  状态: [全部 ▾]  类型: [全部 ▾]  区域: [全部 ▾]     │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  计划名称          │ 区域    │ 状态   │ 项数 │ 创建时间│   │
│  ├──────────────────────────────────────────────────────┤   │
│  │ 甲类仓库周检       │甲类仓库A│已完成  │ 5   │06-23   │   │
│  │ 罐区月度安全检查   │储罐区   │执行中  │ 4   │06-22   │   │
│  │ 节前安全大检查     │全园区   │草稿    │ 8   │06-21   │   │
│  │ 消防设施月度检查   │全园区   │已归档  │ 4   │06-15   │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  ┌─ 快速操作 ──────────────────────────────────────────┐    │
│  │  [快速检查] 输入查询内容 → 即时合规判定               │    │
│  │  [自动扫描] 对所有资产执行规则引擎扫描                 │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

#### 2.3.5 整改工单页 (`/tickets`)

```
┌──────────────────────────────────────────────────────────────┐
│  ← 整改工单                                                  │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ 统计卡片 ──────────────────────────────────────────┐    │
│  │ 总工单 12  │ 待处理 4  │ 整改中 3  │ 已完成 5      │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ID │ 问题摘要         │ 优先级 │ 责任人 │ 状态   │操作│   │
│  ├──────────────────────────────────────────────────────┤   │
│  │ #01 │苯与丙酮同库储存  │ 🔴严重 │ 张三  │新发现  │→  │   │
│  │ #02 │甲醇超临界量80%  │ 🟠高   │ 李四  │确认中  │→  │   │
│  │ #03 │消防通道标识不清  │ 🟡中   │ 王五  │整改中  │→  │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  工单状态流转:                                                │
│  New → Confirmed → InProgress → Remediated → Verified        │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

#### 2.3.6 资产台账页 (`/inspection/assets`)

```
┌──────────────────────────────────────────────────────────────┐
│  ← 化学品资产台账                                           │
├──────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────┐   │
│  │ 名称  │ CAS      │ 位置         │ 存量 │责任人│合规│   │
│  ├──────────────────────────────────────────────────────┤   │
│  │ 苯    │ 71-43-2  │ 甲类A区1号位 │ 15t  │张三  │❌  │   │
│  │ 丙酮  │ 67-64-1  │ 甲类A区2号位 │ 8t   │张三  │✅  │   │
│  │ 甲醇  │ 67-56-1  │ 甲类B区1号位 │ 20t  │李四  │⚠️  │   │
│  │ 硝酸  │ 7697-37-2│ 乙类C区3号位 │ 5t   │王五  │✅  │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  合规图例: ✅ 合规  ❌ 不合规  ⚠️ 未检查                      │
└──────────────────────────────────────────────────────────────┘
```

#### 2.3.7 知识图谱大屏 (`/knowledge-graph`)

基于 vis-network（React 封装：`react-force-graph`）展示化学品-法规-危险类别之间的关联关系：

```
┌──────────────────────────────────────────────────────────────┐
│  🧠 化工合规知识图谱                     全屏 ▢  导出 ⬇      │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ 图例 ──────────────────────┐ ┌──────────────────────┐   │
│  │ ● 化学品   ■ 法规标准        │ │ 搜索: [苯______] 🔍  │   │
│  │ ▲ 危险类别 ◆ 储存条件        │ │ 展开层级: 1 2 [3]    │   │
│  │ ─ 兼容性  ═ 禁止配伍         │ └──────────────────────┘   │
│  └────────────────────────────┘                               │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                                                      │   │
│  │              GB 18218          GB 15603              │   │
│  │                 │  ╲            ╱  │                  │   │
│  │                 │    ╲        ╱    │                  │   │
│  │            [重大危险源]    [储存通则]                 │   │
│  │                 │            │                       │   │
│  │          ┌──────┴──────┐  ┌──┴──────────┐            │   │
│  │          │    苯 ═══ 丙酮│  │  甲醇 ── 乙醇 │            │   │
│  │          │  CAS 71-43  │  │ CAS 67-56   │            │   │
│  │          │  易燃液体    │  │  易燃液体    │            │   │
│  │          └─────────────┘  └─────────────┘            │   │
│  │                                                      │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  选中节点: 苯 (CAS 71-43-2)                                   │
│  关联法规: GB 18218-2018, GB 15603-2022, GB 30000.7-2013     │
│  关联物质: 丙酮(禁止同库), 甲醇(可同库)                        │
└──────────────────────────────────────────────────────────────┘
```

**交互要点**：
- 节点拖拽 + 缩放（vis-network 力导向布局）
- 点击化学品节点显示属性面板（CAS号、危险类别、储存条件、关联法规）
- 点击法规节点展开关联的所有化学品
- 连线颜色区分关系类型：绿色=兼容，红色=禁止配伍，蓝色=法规引用
- 支持按化学品名称搜索并高亮关联子图
- 展开层级控制：1=直接关联，2=二度关联，3=全图
- `react-force-graph` 3D 模式可选（大屏展示场景）

#### 2.3.8 应急响应页 (`/emergency`)

当检测到严重违规或安全事故时，快速查询应急处置方案：

```
┌──────────────────────────────────────────────────────────────┐
│  🚨 应急响应                            [演练模式] [实战模式] │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ 事故类型选择 ──────────────────────────────────────┐    │
│  │ [🔥 火灾] [💥 爆炸] [☠️ 泄漏] [⚡ 触电] [🧪 腐蚀]   │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  涉及化学品: [苯______] [丙酮____]  [+添加]         │   │
│  │  事故规模:    [小型泄漏 ▾]                           │   │
│  │  发生区域:    [甲类仓库A区 ▾]                        │   │
│  └──────────────────────────────────────────────────────┘   │
│                                              [生成应急方案]   │
│                                                              │
│  ┌─ AI 应急方案 ───────────────────────────────────────┐    │
│  │                                                     │    │
│  │  【事故等级】Ⅲ级 (园区级响应)                        │    │
│  │  【疏散半径】500 米                                  │    │
│  │  【防护装备】正压式呼吸器 + A级防护服                 │    │
│  │  【灭火介质】抗溶性泡沫、干粉、CO₂                    │    │
│  │  【处置要点】                                         │    │
│  │    1. 立即切断火源、电源                              │    │
│  │    2. 使用干粉灭火器覆盖燃烧面                        │    │
│  │    3. 对周边储罐喷水冷却                              │    │
│  │  【紧急联系人】园区应急办: xxx / 消防: 119             │    │
│  │  【法规依据】AQ 3013-2008《危险化学品从业单位...》     │    │
│  │                                                     │    │
│  │               [📋 生成应急处置卡]  [📞 一键通知]      │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**交互要点**：
- 演练模式：蓝色主题，操作留痕供事后复盘
- 实战模式：红色告警主题，全屏模式，最大化可读性
- 自动调用 `GetSafetyDistance` 工具计算疏散半径
- 一键生成 A4 应急处置卡片（打印友好）
- 紧急联系人信息硬编码本地（确保离线可用）

#### 2.3.9 GPU 推理监控页 (`/gpu-monitor`)

仅 admin 角色可见，监控 llama.cpp 推理服务运行状态：

```
┌──────────────────────────────────────────────────────────────┐
│  ⚡ GPU 推理监控                        🔄 自动刷新 10s       │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ GPU 状态  │ │ 显存使用  │ │ 模型加载  │ │ 推理速度  │       │
│  │ 🟢 运行中 │ │ 6.2/10GB │ │ ✅ 已加载 │ │ 28.5 t/s │       │
│  │ RTX 3090 │ │ 62%      │ │ qwen3-8b │ │ (avg)    │       │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘       │
│                                                              │
│  ┌─────────────────────────┐ ┌─────────────────────────┐    │
│  │   推理吞吐 (tokens/s)    │ │   请求队列 (实时)       │    │
│  │   📈 ECharts 时序图     │ │   当前排队: 2          │    │
│  │   30/25/28/32/27/29    │ │   最大并发: 2          │    │
│  │                         │ │   Semaphore 等待: 1    │    │
│  └─────────────────────────┘ └─────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  最近推理请求                           [查看全部 →]  │   │
│  │  时间         │ 模型       │ tokens │ 耗时   │ 状态  │   │
│  │  15:30:22    │ qwen3-8b   │ 512    │ 18.2s  │ ✅   │   │
│  │  15:30:05    │ qwen3-8b   │ 1024   │ 38.5s  │ ✅   │   │
│  │  15:29:40    │ qwen3-8b   │ 256    │ 12.3s  │ ⚠️   │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**数据来源**：后端 Prometheus `/metrics` 端点暴露的 `llama_inference_tokens_total`、`llama_inference_duration_seconds`、`llama_server_requests_in_flight` 等指标，前端通过 Grafana iframe 嵌入或直接 fetch `/metrics` 解析。

### 2.4 视觉风格指南

| 维度 | 选择 | 说明 |
|------|------|------|
| **设计语言** | 工业科技风 + 企业功能型 | 化工行业稳重感，兼顾数据可视化科技感 |
| **主色调** | 深蓝灰 #1e2a3a → 天蓝 #409EFF | 化工安全行业专业调性，沉稳不沉闷 |
| **功能色** | 合规绿 #67C23A / 告警红 #F56C6C / 警告橙 #E6A23C | 一目了然的状态标识 |
| **字体** | 系统默认（中文 PingFang SC / Microsoft YaHei） | 不做额外引入，减少加载体积 |
| **间距** | 8px 基准栅格（Ant Design 默认） | 统一视觉节奏 |
| **圆角** | 6px（卡片）/ 4px（按钮/输入框） | 适度柔和，不过度圆润 |
| **暗色模式** | 完整支持（Tailwind `dark:` 前缀 + CSS 变量） | 夜间巡检/大屏展示场景 |
| **响应式** | 桌面优先（1280px+），平板适配（768px+） | 化工园区以桌面操作为主 |

#### 2.4.1 完整 Design Token 色彩体系

```css
/* ── src/assets/styles/tokens.css ── */

:root {
  /* ═══ 主色系 ═══ */
  --color-primary-50: #ecf5ff;
  --color-primary-100: #d9ecff;
  --color-primary-200: #b3d8ff;
  --color-primary-300: #80c4ff;
  --color-primary-400: #4dadff;
  --color-primary-500: #2b8de0;  /* 主色 */
  --color-primary-600: #1e6eb5;
  --color-primary-700: #15508a;
  --color-primary-800: #0d3460;
  --color-primary-900: #061d3a;

  /* ═══ 中性色 ═══ */
  --color-gray-50: #fafbfc;
  --color-gray-100: #f4f6f8;
  --color-gray-200: #e4e8ed;
  --color-gray-300: #c8cfd8;
  --color-gray-400: #99a4b3;
  --color-gray-500: #6b788e;
  --color-gray-600: #4a5568;
  --color-gray-700: #2d3748;
  --color-gray-800: #1e2a3a;  /* 深色背景主色 */
  --color-gray-900: #0f1923;

  /* ═══ 语义色 ═══ */
  --color-success: #67C23A;
  --color-success-light: #e1f3d8;
  --color-success-dark: #529b2e;

  --color-warning: #E6A23C;
  --color-warning-light: #faecd8;
  --color-warning-dark: #b88230;

  --color-danger: #F56C6C;
  --color-danger-light: #fde2e2;
  --color-danger-dark: #c45656;

  --color-info: #909399;
  --color-info-light: #e9e9eb;
  --color-info-dark: #73767a;

  /* ═══ 合规专用语义色 ═══ */
  --color-compliant: #67C23A;          /* ✅ 合规 */
  --color-compliant-bg: #f0f9eb;
  --color-noncompliant: #F56C6C;       /* ❌ 不合规 */
  --color-noncompliant-bg: #fef0f0;
  --color-unknown: #E6A23C;            /* ⚠️ 未检查/未知 */
  --color-unknown-bg: #fdf6ec;
  --color-critical: #FF0000;           /* 🔴 严重违规 */
  --color-critical-bg: #fff0f0;

  /* ═══ 危化品类别色 ═══ */
  --color-explosive: #FF6B35;          /* 爆炸品 */
  --color-flammable: #F7931E;          /* 易燃液体 */
  --color-toxic: #8B572A;              /* 毒性物质 */
  --color-corrosive: #FFD700;          /* 腐蚀品 */
  --color-oxidizer: #FFD700;           /* 氧化剂 */
  --color-gas: #00BFFF;                /* 压缩气体 */

  /* ═══ 图表色板 ═══ */
  --chart-color-1: #409EFF;
  --chart-color-2: #67C23A;
  --chart-color-3: #E6A23C;
  --chart-color-4: #F56C6C;
  --chart-color-5: #909399;
  --chart-color-6: #00D4FF;

  /* ═══ 背景/表面 ═══ */
  --bg-page: #f0f2f5;
  --bg-card: #ffffff;
  --bg-sidebar: #1e2a3a;
  --bg-header: #ffffff;

  /* ═══ 阴影 ═══ */
  --shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.06);
  --shadow-md: 0 4px 6px rgba(0, 0, 0, 0.08);
  --shadow-lg: 0 10px 30px rgba(0, 0, 0, 0.10);
}

/* ═══ 暗黑模式覆盖 ═══ */
[data-theme='dark'],
.dark {
  --color-primary-500: #4dadff;
  --color-primary-700: #b3d8ff;
  --color-gray-50: #1a2332;
  --color-gray-100: #1e2a3a;
  --color-gray-700: #c8cfd8;
  --color-gray-800: #e4e8ed;
  --color-gray-900: #f4f6f8;

  --bg-page: #0f1923;
  --bg-card: #1a2332;
  --bg-sidebar: #0d1520;
  --bg-header: #1a2332;

  --shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.30);
  --shadow-md: 0 4px 6px rgba(0, 0, 0, 0.35);
  --shadow-lg: 0 10px 30px rgba(0, 0, 0, 0.40);
}
```

#### 2.4.2 Tailwind 配置对齐

```typescript
// ─── tailwind.config.ts ───

export default {
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        primary: {
          50: '#ecf5ff', 500: '#2b8de0', 700: '#15508a', 900: '#061d3a',
        },
        compliant: '#67C23A',
        'non-compliant': '#F56C6C',
        unknown: '#E6A23C',
        critical: '#FF0000',
        sidebar: '#1e2a3a',
      },
    },
  },
  // ...
};
```

### 2.5 页面交互规范

#### 2.5.1 键盘快捷键

| 快捷键 | 作用域 | 行为 |
|--------|--------|------|
| `Ctrl + Enter` | 合规审核输入框 | 提交查询 |
| `Ctrl + K` | 全局 | 打开命令面板（快速跳转页面/功能） |
| `Ctrl + /` | 全局 | 显示/隐藏快捷键帮助面板 |
| `Esc` | 弹窗/抽屉 | 关闭当前弹窗 |
| `Ctrl + S` | 巡检/工单编辑页 | 保存草稿 |
| `Tab` | 表单内 | 切换聚焦到下一字段 |

```typescript
// ─── hooks/useKeyboardShortcuts.ts ───

import { useEffect } from 'react';

export function useKeyboardShortcuts(shortcuts: Record<string, () => void>) {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const key = `${e.ctrlKey ? 'Ctrl+' : ''}${e.key}`;
      if (shortcuts[key]) {
        e.preventDefault();
        shortcuts[key]();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [shortcuts]);
}
```

#### 2.5.2 加载状态模式

| 场景 | 模式 | 说明 |
|------|------|------|
| **页面首次加载** | Ant Design `<Skeleton>` 骨架屏 | 卡片/表格/图表占位，减少感知等待 |
| **LLM 推理中 (10-60s)** | 进度条 + 工具调用实时列表 | 参考下方 StreamOutput 组件设计 |
| **列表刷新** | 顶部细线 LoadingBar（nprogress 风格） | 不阻断操作 |
| **表单提交** | 按钮 Loading 态 + 禁用重复点击 | Loading 文字提示当前操作 |
| **数据为空** | `<EmptyState>` 全局统一占位 | 提示文案 + 引导操作按钮 |
| **网络错误** | `<ErrorFallback>` + 重试按钮 | 不白屏，保留导航可用 |

#### 2.5.3 流式打字机效果（LLM 输出）

当后端支持 SSE 流式输出后（后续 Sprint），前端使用 `fetch + ReadableStream` 实现逐字打印效果：

```typescript
// ─── hooks/useStreamOutput.ts ───

import { useState, useRef, useCallback } from 'react';

export function useStreamOutput() {
  const [text, setText] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  const startStream = useCallback(async (url: string, body: unknown) => {
    const controller = new AbortController();
    abortRef.current = controller;
    setIsStreaming(true);
    setText('');

    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        signal: controller.signal,
      });

      const reader = res.body!.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        // SSE 格式: data: {...}\n\n
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';
        for (const line of lines) {
          if (line.startsWith('data: ')) {
            const chunk = JSON.parse(line.slice(6));
            setText((prev) => prev + chunk.content);
          }
        }
      }
    } catch (err: any) {
      if (err.name !== 'AbortError') throw err;
    } finally {
      setIsStreaming(false);
    }
  }, []);

  const abort = useCallback(() => abortRef.current?.abort(), []);

  return { text, isStreaming, startStream, abort };
}
```

#### 2.5.4 新手指引（Driver.js）

首次登录后展示分步引导，降低学习成本：

```typescript
// ─── hooks/useOnboarding.ts ───

import { driver } from 'driver.js';
import 'driver.js/dist/driver.css';

export function startOnboarding() {
  const driverObj = driver({
    showProgress: true,
    steps: [
      { element: '#sidebar-dashboard', popover: { title: '合规态势总览', description: '查看园区合规率、风险分布等核心指标' } },
      { element: '#sidebar-compliance', popover: { title: '合规审核', description: '输入化学品查询问题，AI 自动判断合规性并引用法规' } },
      { element: '#sidebar-inspection', popover: { title: '巡检管理', description: '创建并执行巡检计划，自动生成合规报告' } },
      { element: '#shortcut-hint', popover: { title: '快捷操作', description: '按 Ctrl+K 打开命令面板，Ctrl+Enter 快速提交查询' } },
    ],
  });
  driverObj.drive();
}
```

#### 2.5.5 Markdown 渲染（LLM 输出格式化）

LLM 返回的合规分析结果包含 Markdown 格式（法规引用、表格、代码块），使用 `react-markdown` + `highlight.js` 渲染：

```typescript
// ─── components/business/MarkdownViewer.tsx ───

import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import hljs from 'highlight.js';
import 'highlight.js/styles/github-dark.css';

interface Props {
  content: string;
  isStreaming?: boolean; // 流式输出时启用打字机效果
}

export function MarkdownViewer({ content, isStreaming }: Props) {
  return (
    <div className={`prose prose-sm max-w-none dark:prose-invert ${isStreaming ? 'streaming-cursor' : ''}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          // 高亮法规引用
          a: ({ href, children }) => {
            const isRegulation = /^GB\s|^AQ\s|^HG\s|^SH\s/.test(children?.toString() || '');
            return (
              <a href={href} target="_blank" className={isRegulation ? 'regulation-ref' : ''}>
                {children}
              </a>
            );
          },
          code: ({ className, children }) => {
            const language = className?.replace('language-', '');
            return language ? (
              <pre><code className={className} dangerouslySetInnerHTML={{
                __html: hljs.highlight(String(children), { language }).value,
              }} /></pre>
            ) : (
              <code className="inline-code">{children}</code>
            );
          },
        }}
      />
    </div>
  );
}
```

---

## 三、前端技术栈选型

### 3.1 核心选型

| 层级 | 技术 | 版本 | 理由 |
|------|------|------|------|
| **框架** | React | 18.x | 最大的生态系统，TypeScript 支持最成熟，企业级项目首选 |
| **语言** | TypeScript | 5.x | 编译期类型安全，与后端 C# 强类型体系匹配 |
| **构建** | Vite | 5.x | 秒级 HMR，原生 ESM，比 CRA/webpack 快 10x |
| **UI 组件** | Ant Design | 5.x | 最成熟的中文企业级组件库，Table/Form/Modal 开箱即用 |
| **CSS** | Tailwind CSS | 3.x | 原子化 CSS，与 Ant Design 互补做自定义布局 |
| **路由** | React Router | 6.x | 标准方案，嵌套路由 + 懒加载 |
| **服务端状态** | TanStack Query | 5.x | 自动缓存/重试/失效，完美处理 LLM 长耗时请求 |
| **客户端状态** | Zustand | 4.x | 轻量（<1KB），无 Boilerplate，替代 Redux |
| **HTTP** | Axios | 1.x | 拦截器机制天然适合 JWT 自动刷新 |
| **图表** | ECharts (echarts-for-react) | 5.x | 国产标杆，化学工业可视化场景（散点图/仪表盘/热力图） |
| **表单** | React Hook Form + Zod | 7.x + 3.x | 性能最优，Zod schema 可与后端 C# record 对齐 |
| **图标** | @ant-design/icons | 5.x | 与 Ant Design 统一风格 |
| **图谱** | react-force-graph | 1.x | 替代 vis-network，原生 React 组件，支持 2D/3D 力导向图 |
| **Markdown** | react-markdown + remark-gfm | 9.x | LLM 输出渲染，GFM 表格/任务列表支持 |
| **代码高亮** | highlight.js | 11.x | Markdown 代码块语法高亮 |
| **新手引导** | driver.js | 1.x | 分步指引，降低首次使用门槛 |
| **测试** | Vitest + Testing Library | 1.x | 与 Vite 深度集成，速度快 |
| **E2E 测试** | Playwright | 1.x | 跨浏览器端到端测试，支持视觉回归截图 |
| **代码规范** | ESLint + Prettier | 8.x + 3.x | 强制统一风格 |

### 3.2 为什么不选其他方案

| 方案 | 不选的理由 |
|------|-----------|
| **Vue 3** | Ant Design Vue 功能滞后 React 版 6-12 个月；社区 React 的 LLM/Chat UI 组件更丰富 |
| **Angular** | 学习曲线陡峭，不适合小型团队快速交付 |
| **Next.js** | SSR 对 BFF（Backend For Frontend）架构有优势，但本项目 API 已独立部署，纯 SPA 更简单 |
| **shadcn/ui** | 组件需要从零组合，企业表单/表格场景不如 Ant Design 开箱即用 |
| **Redux Toolkit** | 本项目状态管理简单（主要是 Auth + 少量全局配置），Zustand 足够 |

### 3.3 关键依赖版本锁定

```json
{
  "dependencies": {
    "react": "^18.3.0",
    "react-dom": "^18.3.0",
    "react-router-dom": "^6.23.0",
    "antd": "^5.17.0",
    "@ant-design/icons": "^5.3.0",
    "axios": "^1.7.0",
    "@tanstack/react-query": "^5.40.0",
    "zustand": "^4.5.0",
    "echarts": "^5.5.0",
    "echarts-for-react": "^3.0.0",
    "react-hook-form": "^7.51.0",
    "@hookform/resolvers": "^3.6.0",
    "zod": "^3.23.0",
    "dayjs": "^1.11.0",
    "react-markdown": "^9.0.0",
    "remark-gfm": "^4.0.0",
    "highlight.js": "^11.9.0",
    "react-force-graph": "^1.45.0",
    "driver.js": "^1.3.0"
  },
  "devDependencies": {
    "typescript": "^5.4.0",
    "vite": "^5.2.0",
    "@vitejs/plugin-react": "^4.2.0",
    "tailwindcss": "^3.4.0",
    "autoprefixer": "^10.4.0",
    "vitest": "^1.6.0",
    "@testing-library/react": "^15.0.0",
    "@playwright/test": "^1.44.0",
    "eslint": "^8.57.0",
    "prettier": "^3.2.0",
    "msw": "^2.3.0"
  }
}
```

---

## 四、前端项目结构与模块划分

### 4.1 目录结构

```
agent1-web/
├── public/
│   └── favicon.svg                    # 化工安全标识图标
├── src/
│   ├── main.tsx                       # 入口：ReactDOM.createRoot
│   ├── App.tsx                        # 根组件：ConfigProvider + Router
│   ├── vite-env.d.ts
│   │
│   ├── assets/                        # 静态资源
│   │   ├── styles/
│   │   │   ├── index.css              # Tailwind 指令 + 全局样式
│   │   │   └── antd-theme.ts          # Ant Design 主题 Token 定制
│   │   └── images/                    # 图标/插图
│   │
│   ├── config/                        # 配置常量
│   │   ├── api.ts                     # API_BASE_URL, TIMEOUT 等
│   │   └── constants.ts               # 业务常量（角色、状态枚举等）
│   │
│   ├── lib/                           # 基础设施库
│   │   ├── axios.ts                   # Axios 实例 + JWT 拦截器
│   │   ├── query-client.ts            # TanStack Query 全局配置
│   │   └── utils.ts                   # 工具函数（脱敏、格式化等）
│   │
│   ├── stores/                        # Zustand 全局状态
│   │   ├── auth-store.ts              # Token + User + Role
│   │   └── app-store.ts              # 侧边栏折叠、暗色模式等
│   │
│   ├── hooks/                         # 自定义 Hooks
│   │   ├── useAuth.ts                 # 登录/登出/刷新
│   │   ├── useComplianceCheck.ts      # 合规审核 mutation
│   │   └── useInspection.ts           # 巡检相关 queries
│   │
│   ├── types/                         # TypeScript 类型定义
│   │   ├── api.ts                     # API 请求/响应类型（与后端 C# record 对齐）
│   │   ├── models.ts                  # 业务模型（ChemicalAsset, Finding 等）
│   │   └── enums.ts                   # 枚举（FindingSeverity, InspectionType 等）
│   │
│   ├── pages/                         # 页面组件（按路由分组）
│   │   ├── login/
│   │   │   └── LoginPage.tsx
│   │   ├── dashboard/
│   │   │   ├── DashboardPage.tsx
│   │   │   └── components/            # 仪表盘专属组件
│   │   │       ├── StatCard.tsx
│   │   │       ├── RiskPieChart.tsx
│   │   │       ├── SeverityBarChart.tsx
│   │   │       └── RecentFindings.tsx
│   │   ├── compliance/
│   │   │   ├── ComplianceCheckPage.tsx
│   │   │   ├── HazardQueryPage.tsx
│   │   │   └── StorageCheckPage.tsx
│   │   ├── inspection/
│   │   │   ├── PlanListPage.tsx
│   │   │   ├── PlanDetailPage.tsx
│   │   │   ├── CreatePlanPage.tsx
│   │   │   ├── RoundDetailPage.tsx
│   │   │   ├── ReportPage.tsx
│   │   │   └── AssetsPage.tsx
│   │   ├── tickets/
│   │   │   ├── TicketListPage.tsx
│   │   │   └── TicketDetailPage.tsx
│   │   ├── knowledge-graph/
│   │   │   └── KnowledgeGraphPage.tsx
│   │   ├── emergency/
│   │   │   └── EmergencyResponsePage.tsx
│   │   └── admin/
│   │       ├── KnowledgeBasePage.tsx
│   │       ├── CacheManagePage.tsx
│   │       └── GpuMonitorPage.tsx
│   │
│   ├── components/                    # 共享组件
│   │   ├── layout/
│   │   │   ├── AppLayout.tsx           # 主布局（Sider + Header + Content）
│   │   │   ├── SideMenu.tsx            # 侧边菜单（按角色过滤）
│   │   │   └── UserMenu.tsx            # 顶部用户下拉菜单
│   │   ├── common/
│   │   │   ├── LoadingOverlay.tsx      # LLM 推理等待遮罩
│   │   │   ├── ComplianceBadge.tsx     # 合规/不合规标签
│   │   │   ├── SeverityTag.tsx         # 严重级别标签
│   │   │   ├── StatusBadge.tsx         # 工单状态标签
│   │   │   ├── EmptyState.tsx          # 空状态占位
│   │   │   └── ErrorFallback.tsx       # 错误边界
│   │   └── business/
│   │       ├── ComplianceResult.tsx    # 合规审核结果卡片
│   │       ├── ToolCallTimeline.tsx    # 工具调用时间线
│   │       ├── RegulationRef.tsx       # 法规引用链接
│   │       ├── MarkdownViewer.tsx      # LLM 输出 Markdown 渲染
│   │       ├── StreamOutput.tsx        # 流式打字机效果输出
│   │       └── QuickCheckInput.tsx     # 快速检查输入框
│   │
│   ├── routes/                        # 路由配置
│   │   ├── index.tsx                  # 路由表定义
│   │   ├── ProtectedRoute.tsx         # 鉴权守卫
│   │   └── role-guard.tsx             # 角色守卫
│   │
│   └── mocks/                         # MSW Mock 数据（仅开发环境）
│       ├── handlers.ts                # Mock API handlers
│       ├── data/
│       │   ├── compliance.ts           # 合规审核 Mock 数据
│       │   ├── inspection.ts           # 巡检 Mock 数据
│       │   └── tickets.ts             # 工单 Mock 数据
│       └── server.ts                  # MSW Server 启动入口
│
├── .env                               # 环境变量（VITE_API_BASE_URL）
├── .env.mock                           # Mock 模式环境变量
├── index.html
├── package.json
├── tsconfig.json
├── vite.config.ts
├── tailwind.config.ts
├── postcss.config.js
└── .eslintrc.cjs
```

### 4.2 模块职责说明

| 模块 | 职责 | 依赖 |
|------|------|------|
| `lib/axios.ts` | 封装 Axios 实例，自动附加 JWT、401 刷新、请求重试 | `stores/auth-store` |
| `lib/query-client.ts` | TanStack Query 全局配置（staleTime: 30s, retry: 2） | — |
| `stores/auth-store` | 存储 token/user/role，提供 login/logout/refreshToken 方法 | `lib/axios` |
| `hooks/use*` | 封装 TanStack Query 的 useQuery/useMutation，返回类型安全数据 | `types/api` |
| `pages/*` | 页面级组件：组合布局 + 业务组件 + 数据获取 | `hooks`, `components` |
| `components/layout` | 全局布局框架（侧边栏 + 顶栏 + 内容区） | `stores/auth-store` |
| `components/business` | 可复用的业务组件（合规结果卡片、工具调用时间线等） | `types/models` |
| `routes/` | 路由配置 + 权限守卫 | `stores/auth-store` |
| `mocks/` | MSW 拦截 API 请求返回 Mock 数据（开发期独立前端时使用） | — |
| `types/` | 与后端 C# record/class 对齐的 TypeScript 类型定义 | — |

---

## 五、API 接口契约定义

### 5.1 契约管理原则

> **契约先行 (Contract-First)**：前端和后端共享一份 OpenAPI 规范文件，双方各自基于契约并行开发，通过 Mock 和契约测试保证一致性。

### 5.2 请求/响应类型定义（TypeScript 对齐 C#）

```typescript
// ─── types/api.ts ───

// ============================================================
// Auth 认证
// ============================================================
export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  username: string;
  role: 'admin' | 'auditor' | 'viewer';
  expiresAt: string; // ISO 8601
}

export interface RefreshRequest {
  refreshToken: string;
}

// ============================================================
// Compliance 合规审核
// ============================================================
export interface ComplianceRequest {
  query: string;
}

export interface ComplianceResponse {
  query: string;
  response: string | null;
  toolsUsed: string[];
  verifiedRegulations: string[];
  hallucinatedRegulations: string[];
  warnings: string[];
}

export interface HazardQueryRequest {
  substanceName: string;
}

export interface HazardQueryResponse {
  substanceName: string;
  response: string | null;
  toolsUsed: string[];
}

export interface StorageCompatibilityRequest {
  substanceA: string;
  substanceB: string;
}

export interface StorageCompatibilityResponse {
  substanceA: string;
  substanceB: string;
  response: string | null;
  toolsUsed: string[];
}

// 合规总览
export interface ComplianceSummary {
  totalAssets: number;
  checkedAssets: number;
  compliantAssets: number;
  nonCompliantAssets: number;
  complianceRate: number;
  totalFindings: number;
  openFindings: number;
  remediationRate: number;
  lastAutoScanAt: string | null;
  findingsBySeverity: Record<string, number>;
  findingsByStatus: Record<string, number>;
  riskDistribution: {
    low: number;
    unknown: number;
    high: number;
    critical: number;
  };
}

// ============================================================
// Inspection 巡检
// ============================================================
export interface CreatePlanRequest {
  name: string;
  type?: string;       // DailyWeekly | Monthly | PreHoliday | Regulatory
  area?: string;
  items: InspectionItemRequest[];
  notes?: string;
}

export interface InspectionItemRequest {
  query: string;
  capability?: string; // storage-compliance | safety-distance | regulatory-audit | ghs-label-check
}

export interface InspectionPlan {
  planId: string;
  name: string;
  area: string;
  type: string;
  inspector: string;
  status: 'Draft' | 'InProgress' | 'Completed' | 'Archived';
  scheduledDate: string;
  createdAt: string;
  notes: string;
  items: InspectionItem[];
}

export interface InspectionItem {
  itemId: number;
  query: string;
  capabilityName: string;
  expectedRegulation?: string;
}

export interface InspectionRound {
  roundId: string;
  planId: string;
  complianceRate: number;
  compliantCount: number;
  nonCompliantCount: number;
  warningCount: number;
  ticketCount: number;
  totalElapsedMs: number;
  executedBy: string;
  startedAt: string;
  completedAt: string | null;
  results: InspectionItemResult[];
}

export interface InspectionItemResult {
  itemId: number;
  isCompliant: boolean | null;
  regulationRef: string;
  conclusion: string;
  warnings: string[];
  tools: string[];
  traceId: string;
  elapsedMs: number;
}

export interface InspectionReport {
  reportId: string;
  roundId: string;
  complianceRate: number;
  summary: string;
  criticalFindings: string[];
  auditHash: string;
  generatedAt: string;
  generatedBy: string;
  markdown: string;
  plan: { planId: string; name: string; area: string };
}

export interface ChemicalAsset {
  assetId: string;
  name: string;
  casNumber: string;
  location: string;
  quantityTons: number;
  storageCondition: string;
  responsiblePerson: string;
  isMajorHazardSource: boolean;
  lastCheckResult: boolean | null;
  lastCheckedAt: string | null;
}

export interface ScanResult {
  scannedAt: string;
  totalAssets: number;
  checkedAssets: number;
  totalFindings: number;
  newFindings: number;
  findings: Finding[];
}

export interface Finding {
  findingId: string;
  assetId: string;
  ruleId: string;
  regulationRef: string;
  description: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low' | 'Info';
  status: string;
}

export interface QuickCheckRequest {
  query: string;
}

export interface QuickCheckResult {
  isCompliant: boolean;
  conclusion: string;
  regulationRef: string;
  warnings: string[];
  tools: string[];
  traceId: string;
  elapsedMs: number;
}

// ============================================================
// Tickets 工单
// ============================================================
export interface TicketItem {
  id: number;
  issue: string;
  action: string;
  priority: string;
  status: string;
  assignee: string;
  regulationRef: string;
  suggestedDeadline: string;
  isOpen: boolean;
  logCount: number;
}

export interface TicketListResponse {
  total: number;
  open: number;
  tickets: TicketItem[];
}

export interface TicketStatusUpdateRequest {
  action: 'accept' | 'start' | 'complete' | 'verify' | 'close' | 'reject';
  assignee?: string;
  reason?: string;
}

// ============================================================
// 通用错误响应
// ============================================================
export interface ApiError {
  error: string;
  retryAfter?: number; // 503 时返回
}

// ============================================================
// 健康检查
// ============================================================
export interface HealthStatus {
  status: 'healthy' | 'degraded';
  timestamp: string;
  version: string;
  checks: {
    database: string;
    ollama: string;
    knowledge_base_docs: number;
    llm_calls: number;
    llm_error_rate: string;
  };
}
```

### 5.3 API 客户端封装

```typescript
// ─── lib/axios.ts ───

import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '../stores/auth-store';

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  timeout: 120_000, // LLM 推理最长 2 分钟
  headers: { 'Content-Type': 'application/json' },
});

// 请求拦截器：自动附加 JWT
apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = useAuthStore.getState().token;
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// 响应拦截器：401 自动刷新 / 503 自动重试 / TraceId 错误追踪
let isRefreshing = false;
let failedQueue: Array<{ resolve: Function; reject: Function }> = [];

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiError>) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    // 503 服务繁忙 → 自动重试（最多 2 次）
    if (error.response?.status === 503 && !originalRequest._retry) {
      originalRequest._retry = true;
      const retryAfter = (error.response.data as ApiError)?.retryAfter ?? 5;
      await new Promise((r) => setTimeout(r, retryAfter * 1000));
      return apiClient(originalRequest);
    }

    // 401 → 尝试刷新 Token
    if (error.response?.status === 401 && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return apiClient(originalRequest);
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const refreshToken = useAuthStore.getState().refreshToken;
        const { data } = await axios.post<LoginResponse>(
          `${apiClient.defaults.baseURL}/api/Auth/refresh`,
          { refreshToken }
        );
        useAuthStore.getState().setTokens(data.token, data.refreshToken);
        failedQueue.forEach((p) => p.resolve(data.token));
        failedQueue = [];
        originalRequest.headers.Authorization = `Bearer ${data.token}`;
        return apiClient(originalRequest);
      } catch {
        failedQueue.forEach((p) => p.reject(error));
        failedQueue = [];
        useAuthStore.getState().logout();
        window.location.href = '/login';
        return Promise.reject(error);
      } finally {
        isRefreshing = false;
      }
    }

    // TraceId 错误追踪：提取后端 X-Request-Id 用于全链路排查
    const traceId = error.response?.headers['x-request-id'] || 'N/A';
    console.error(`[API Error] ${error.config?.method?.toUpperCase()} ${error.config?.url} | TraceId: ${traceId} | Status: ${error.response?.status}`);

    return Promise.reject(error);
  }
);

export default apiClient;
```

### 5.4 TanStack Query Hooks 示例

```typescript
// ─── hooks/useComplianceCheck.ts ───

import { useMutation, useQuery } from '@tanstack/react-query';
import apiClient from '../lib/axios';
import type {
  ComplianceRequest, ComplianceResponse,
  ComplianceSummary,
  HazardQueryRequest, HazardQueryResponse,
  StorageCompatibilityRequest, StorageCompatibilityResponse,
} from '../types/api';

// 合规总览
export function useComplianceSummary() {
  return useQuery<ComplianceSummary>({
    queryKey: ['compliance', 'summary'],
    queryFn: () => apiClient.get('/api/Compliance/summary').then((r) => r.data),
    refetchInterval: 60_000, // 每分钟自动刷新
  });
}

// 合规审核（Mutation — 长时间 LLM 调用）
export function useComplianceCheck() {
  return useMutation<ComplianceResponse, Error, ComplianceRequest>({
    mutationFn: (req) =>
      apiClient.post('/api/Compliance/check', req).then((r) => r.data),
    onError: (error) => {
      console.error('合规审核失败:', error.message);
    },
  });
}

// 危化品查询
export function useHazardQuery() {
  return useMutation<HazardQueryResponse, Error, HazardQueryRequest>({
    mutationFn: (req) =>
      apiClient.post('/api/Compliance/hazard/query', req).then((r) => r.data),
  });
}

// 储存兼容性
export function useStorageCompatibility() {
  return useMutation<StorageCompatibilityResponse, Error, StorageCompatibilityRequest>({
    mutationFn: (req) =>
      apiClient.post('/api/Compliance/storage/compatibility', req).then((r) => r.data),
  });
}
```

---

## 六、前后端并行开发协作策略

### 6.1 核心原则

```
┌─────────────────────────────────────────────────────────────┐
│              前后端并行开发 ≠ 互相等待                         │
│                                                             │
│  后端正在修复 Bug (RAG 链路 / 工具调用 / ...)                │
│       ↓                                                     │
│  前端基于「契约 + Mock」独立开发                              │
│       ↓                                                     │
│  契约变更 → 双方评审 → 同步更新 Mock + 类型                   │
│       ↓                                                     │
│  后端 Bug 修复完成 → 集成测试 → 端到端验证                    │
└─────────────────────────────────────────────────────────────┘
```

### 6.2 三层隔离策略

| 隔离层 | 机制 | 工具 | 适用场景 |
|--------|------|------|---------|
| **L1: 契约层** | OpenAPI 规范 + TypeScript 类型 | `types/api.ts` + 契约评审 | API 设计阶段 |
| **L2: Mock 层** | MSW (Mock Service Worker) 拦截 | `mocks/handlers.ts` | 前端独立开发 |
| **L3: 集成层** | 真实后端 + 契约测试 | Vitest + 端到端测试 | 集成验证阶段 |

### 6.3 MSW Mock 策略

当前后端正在持续修复 Bug（RAG 工程 Bug 修复笔记中记录的 30 个 Bug 已全部修复，但可能还有增量修改），前端需要完全独立运行：

```typescript
// ─── mocks/handlers.ts ───

import { http, HttpResponse, delay } from 'msw';

export const handlers = [
  // 登录
  http.post('/api/Auth/login', async ({ request }) => {
    const body = await request.json() as { username: string };
    await delay(300);
    return HttpResponse.json({
      token: 'mock-jwt-token-' + Date.now(),
      refreshToken: 'mock-refresh-token',
      username: body.username,
      role: 'admin',
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  // 合规审核（模拟 LLM 延迟 2-5 秒）
  http.post('/api/Compliance/check', async ({ request }) => {
    const body = await request.json() as { query: string };
    await delay(2000 + Math.random() * 3000); // 模拟 LLM 推理延迟
    return HttpResponse.json({
      query: body.query,
      response: `【合规判断】否\n【法规依据】GB 15603-2022 §4.2.2\n【违规点】${body.query}存在禁忌配伍\n【整改建议】立即分库储存`,
      toolsUsed: ['CheckStorageCompatibility', 'CheckHazardCategory'],
      verifiedRegulations: ['GB 15603-2022', 'GB 30000.7-2013'],
      hallucinatedRegulations: [],
      warnings: ['丙酮存量超危险源临界量80%'],
    });
  }),

  // 合规总览
  http.get('/api/Compliance/summary', async () => {
    await delay(200);
    return HttpResponse.json({
      totalAssets: 8,
      checkedAssets: 6,
      compliantAssets: 4,
      nonCompliantAssets: 2,
      complianceRate: 0.75,
      totalFindings: 5,
      openFindings: 3,
      remediationRate: 0.6,
      lastAutoScanAt: new Date().toISOString(),
      findingsBySeverity: { Critical: 2, High: 1, Medium: 1, Low: 0, Info: 1 },
      findingsByStatus: { New: 2, Confirmed: 1, InProgress: 1, Remediated: 0, VerifiedClosed: 1, Closed: 2, FalsePositive: 0 },
      riskDistribution: { low: 4, unknown: 2, high: 1, critical: 1 },
    });
  }),

  // 巡检计划列表
  http.get('/api/Inspection/plans', async () => {
    await delay(200);
    return HttpResponse.json([
      { planId: 'p1', name: '甲类仓库周检', area: '甲类仓库A区', inspector: '张三', status: 'Completed', items: 5, createdAt: '2026-06-23T10:00:00Z' },
      { planId: 'p2', name: '罐区月度安全检查', area: '储罐区', inspector: '李四', status: 'InProgress', items: 4, createdAt: '2026-06-22T09:00:00Z' },
      { planId: 'p3', name: '节前安全大检查', area: '全园区', inspector: '王五', status: 'Draft', items: 8, createdAt: '2026-06-21T08:00:00Z' },
    ]);
  }),

  // 资产台账
  http.get('/api/Inspection/assets', async () => {
    await delay(200);
    return HttpResponse.json([
      { assetId: 'a1', name: '苯', casNumber: '71-43-2', location: '甲类仓库A区1号位', quantityTons: 15, storageCondition: '常温常压', responsiblePerson: '张三', isMajorHazardSource: true, lastCheckResult: false, lastCheckedAt: '2026-06-23T10:30:00Z' },
      { assetId: 'a2', name: '丙酮', casNumber: '67-64-1', location: '甲类仓库A区2号位', quantityTons: 8, storageCondition: '常温常压', responsiblePerson: '张三', isMajorHazardSource: false, lastCheckResult: true, lastCheckedAt: '2026-06-23T10:30:00Z' },
      { assetId: 'a3', name: '甲醇', casNumber: '67-56-1', location: '甲类仓库B区1号位', quantityTons: 20, storageCondition: '常温常压', responsiblePerson: '李四', isMajorHazardSource: true, lastCheckResult: false, lastCheckedAt: '2026-06-22T14:00:00Z' },
    ]);
  }),

  // 工单列表
  http.get('/api/Tickets', async () => {
    await delay(200);
    return HttpResponse.json({
      total: 3,
      open: 2,
      tickets: [
        { id: 1, issue: '苯与丙酮同库储存违规', action: '立即分库储存', priority: 'Critical', status: 'New', assignee: '', regulationRef: 'GB 15603-2022 §4.2.2', suggestedDeadline: '2026-06-25T00:00:00Z', isOpen: true, logCount: 0 },
        { id: 2, issue: '甲醇存量超临界量80%', action: '降低存量至临界量以下', priority: 'High', status: 'Confirmed', assignee: '李四', regulationRef: 'GB 18218-2018', suggestedDeadline: '2026-07-01T00:00:00Z', isOpen: true, logCount: 1 },
        { id: 3, issue: '消防通道标识不清', action: '张贴标识', priority: 'Medium', status: 'InProgress', assignee: '王五', regulationRef: 'GB 50016 §7.1.8', suggestedDeadline: '2026-07-15T00:00:00Z', isOpen: false, logCount: 2 },
      ],
    });
  }),

  // 健康检查
  http.get('/health', async () => {
    return HttpResponse.json({
      status: 'healthy',
      timestamp: new Date().toISOString(),
      version: '2.5.0',
      checks: { database: 'connected', ollama: 'reachable', knowledge_base_docs: 156, llm_calls: 1234, llm_error_rate: '2.1%' },
    });
  }),
];
```

```typescript
// ─── mocks/server.ts ───

import { setupWorker } from 'msw/browser';
import { handlers } from './handlers';

// 仅在开发环境 + VITE_ENABLE_MOCK=true 时启用
export const worker = setupWorker(...handlers);

// main.tsx 中：
// if (import.meta.env.VITE_ENABLE_MOCK === 'true') {
//   const { worker } = await import('./mocks/server');
//   await worker.start({ onUnhandledRequest: 'bypass' });
// }
```

### 6.4 开发工作流（三条并行线）

```
时间线 →

后端线 (Bug 修复)        前端线 (功能开发)        契约线 (API 治理)
─────────────────      ─────────────────      ─────────────────
                        搭建项目骨架
                        Vite + React + Router
                        配置 MSW Mock
                              │
修复 RAG 管道 Bug              │                     定义 OpenAPI 契约
(不影响 API 接口)              │                     评审通过
      │                       │                          │
      │                  基于 Mock 开发登录页              │
      │                  基于 Mock 开发仪表盘              │
      │                       │                          │
修复工具调用链路               │                          │
(CallToolAsync传空参数)        │                     契约微调
      │                       │                     (参数名对齐)
      │                  开发合规审核页                     │
      │                  开发巡检管理页                     │
      │                       │                          │
      │                       ▼                          │
      │              ┌─── Mock 集成测试 ───┐              │
      │              │ 各页面流程走通 ✓   │              │
      │              └───────────────────┘              │
      │                       │                          │
      ▼                       ▼                          ▼
后端 Bug 修复完毕    切换到真实 API                最终契约冻结
      │              (改 VITE_ENABLE_MOCK=false)
      │                       │
      └───────────┬───────────┘
                  │
          ┌─── 端到端集成测试 ───┐
          │  Vitest + Playwright │
          │  全页面流程验证      │
          └─────────────────────┘
                  │
                  ▼
            🚀 联调上线
```

### 6.5 契约变更流程

当后端 API 发生变更（如新增字段、修改返回格式），执行：

```
1. 后端修改 C# record/response
       ↓
2. 更新 docs/architecture/Agent1前端架构设计方案.md 中的类型定义
       ↓
3. 前端同步更新 types/api.ts
       ↓
4. 前端同步更新 mocks/handlers.ts 对应 Mock 数据
       ↓
5. 双方各自运行测试，确认无 Breaking Change
       ↓
6. 集成验证
```

### 6.6 版本控制策略

| 策略 | 说明 |
|------|------|
| **Monorepo（推荐）** | 前端代码放在 `agent1-web/` 下，与现有后端同仓库，统一 PR 审核 |
| **分支策略** | `feat/frontend-login` / `feat/frontend-dashboard` 分功能分支开发 |
| **提交规范** | `feat(frontend): 实现合规审核页面` / `fix(frontend): 修复 Token 刷新逻辑` |
| **CI/CD** | 前端 `npm run build` + `npm run test` 在 PR 中自动运行 |

### 6.7 集成测试方案

```typescript
// ─── __tests__/integration/compliance-flow.test.tsx ───

import { describe, it, expect, beforeAll } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import ComplianceCheckPage from '../../pages/compliance/ComplianceCheckPage';

describe('合规审核流程集成测试', () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const renderPage = () =>
    render(
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <ComplianceCheckPage />
        </BrowserRouter>
      </QueryClientProvider>
    );

  it('输入化学品查询并显示结果', async () => {
    renderPage();

    // 找到输入框并输入
    const input = screen.getByPlaceholderText(/输入你想查询的合规问题/);
    await userEvent.type(input, '苯和丙酮能放在同一个仓库吗？');

    // 点击提交
    const submitBtn = screen.getByText('提交审核');
    await userEvent.click(submitBtn);

    // 等待 LLM 响应（Mock 延迟 2-5s）
    await waitFor(
      () => {
        expect(screen.getByText(/合规判断/)).toBeInTheDocument();
      },
      { timeout: 10000 }
    );

    // 验证法规引用存在
    expect(screen.getByText(/GB 15603/)).toBeInTheDocument();
  });
});
```

### 6.8 启动命令

```bash
# 开发环境（连接真实后端）
cd agent1-web
npm install
npm run dev                    # Vite dev server → http://localhost:5173

# 开发环境（纯 Mock 模式 — 后端未启动时使用）
npm run dev:mock               # VITE_ENABLE_MOCK=true npm run dev

# 构建
npm run build                  # 输出到 dist/

# 测试
npm run test                   # Vitest 单元测试
npm run test:e2e               # Playwright 端到端测试
npm run test:e2e:ui            # Playwright UI 调试模式
npm run test:visual            # Playwright 视觉回归截图对比

# 代码检查
npm run lint                   # ESLint
npm run format                 # Prettier

# Docker 构建
npm run docker:build            # 构建前端 Docker 镜像
docker run -p 80:80 agent1-web:latest  # 本地运行
```

### 6.9 前端 Docker + Nginx 生产部署

#### 6.9.1 多阶段构建 Dockerfile

```dockerfile
# ─── agent1-web/Dockerfile ───
# Stage 1: 编译阶段
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci --only=production

COPY . .
RUN npm run build

# Stage 2: Nginx 运行阶段
FROM nginx:1.25-alpine AS runtime

# 复制构建产物
COPY --from=build /app/dist /usr/share/nginx/html

# 复制 Nginx 配置
COPY nginx.conf /etc/nginx/conf.d/default.conf

# 健康检查
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://localhost:80/health || exit 1

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

#### 6.9.2 Nginx 反向代理配置

```nginx
# ─── agent1-web/nginx.conf ───

server {
    listen 80;
    server_name localhost;
    root /usr/share/nginx/html;
    index index.html;

    # Gzip 压缩
    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml;

    # 安全头
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # SPA History 路由回退
    location / {
        try_files $uri $uri/ /index.html;
    }

    # API 反向代理 → 后端 .NET 8 服务
    location /api/ {
        proxy_pass http://agent1_api:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_read_timeout 120s;  # LLM 推理最长 2 分钟
        proxy_connect_timeout 10s;
    }

    # 健康检查端点（不经过 SPA 路由）
    location /health {
        access_log off;
        return 200 "OK";
    }

    # 静态资源强缓存（带 hash 文件名）
    location /assets/ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }

    # Prometheus 指标（可选，Nginx 自身监控）
    location /nginx_status {
        stub_status;
        allow 127.0.0.1;
        deny all;
    }
}
```

#### 6.9.3 docker-compose 集成

在前端 Dockerfile 就绪后，在项目根 `docker-compose.yml` 中新增 `web` 服务：

```yaml
  # ═══════════════════════════════════════
  # Agent1 Web 前端 (Nginx SPA)
  # ═══════════════════════════════════════
  web:
    build:
      context: ./agent1-web
      dockerfile: Dockerfile
    container_name: agent1_web
    restart: unless-stopped
    ports:
      - "${WEB_PORT:-80}:80"
    depends_on:
      api:
        condition: service_healthy
    healthcheck:
      test: ["CMD-SHELL", "wget -q -O /dev/null http://localhost:80/health || exit 1"]
      interval: 30s
      timeout: 10s
      retries: 3
    networks:
      - agent1_net
```

最终镜像体积 < 80MB（Alpine Nginx + Gzip 压缩后静态文件）。

### 6.10 增强测试策略

#### 6.10.1 测试金字塔

```
        ┌──────────┐
        │ E2E      │  Playwright — 完整用户流程
        │ 5-10 条   │  (登录→合规审核→查看结果)
        ├──────────┤
        │ 集成测试  │  Vitest + MSW — API 交互
        │ 15-25 条  │  (Mock API 返回 → 验证 UI 状态)
        ├──────────┤
        │ 单元测试  │  Vitest — 纯逻辑
        │ 40-60 条  │  (Hooks / Utils / Stores)
        └──────────┘
```

#### 6.10.2 Playwright 端到端测试示例

```typescript
// ─── e2e/compliance-flow.spec.ts ───

import { test, expect } from '@playwright/test';

test.describe('合规审核流程', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('[placeholder="用户名"]', 'admin');
    await page.fill('[placeholder="密码"]', 'test123');
    await page.click('button:has-text("登 录")');
    await page.waitForURL('/dashboard');
  });

  test('完整合规审核 → 查看结果 → 导出', async ({ page }) => {
    // 导航到合规审核页
    await page.click('text=合规审核');

    // 输入查询
    await page.fill('[placeholder*="合规问题"]', '苯和丙酮能放在同一个仓库吗？');
    await page.click('text=提交审核');

    // 等待 AI 响应
    await expect(page.locator('text=合规判断')).toBeVisible({ timeout: 30000 });

    // 验证法规引用
    await expect(page.locator('text=GB 15603')).toBeVisible();

    // 截图（视觉回归基线）
    await expect(page).toHaveScreenshot('compliance-result.png');

    // 导出报告
    await page.click('text=导出为报告');
    const download = await page.waitForEvent('download');
    expect(download.suggestedFilename()).toContain('合规报告');
  });

  test('Token 过期自动刷新', async ({ page }) => {
    // 模拟 Token 过期场景...
  });
});
```

#### 6.10.3 视觉回归测试

```bash
# 首次运行 → 生成基线截图
npx playwright test --update-snapshots

# 后续运行 → 对比基线
npx playwright test

# 查看差异
npx playwright show-report
```

#### 6.10.4 CI/CD 集成

```yaml
# .github/workflows/ci.yml 中新增前端 job
frontend-test:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-node@v4
      with: { node-version: 20 }
    - run: npm ci
      working-directory: agent1-web
    - run: npm run lint
      working-directory: agent1-web
    - run: npm run test -- --coverage
      working-directory: agent1-web
    - run: npx playwright install --with-deps chromium
      working-directory: agent1-web
    - run: npm run test:e2e
      working-directory: agent1-web
    - uses: actions/upload-artifact@v4
      if: failure()
      with:
        name: playwright-report
        path: agent1-web/playwright-report/
```

### 6.9 环境变量配置

```bash
# .env（连接真实后端）
VITE_API_BASE_URL=http://localhost:5000

# .env.mock（纯 Mock 模式）
VITE_API_BASE_URL=http://localhost:5000
VITE_ENABLE_MOCK=true
```

---

## 附录：与现有后端 Bug 修复的兼容性分析

基于 [RAG工程Bug修复笔记_2026-05-26.md](../troubleshooting/RAG工程Bug修复笔记_2026-05-26.md) 中的 30 个 Bug 修复：

| Bug 类型 | 是否影响前端 | 处理方式 |
|----------|:----------:|---------|
| Key 错配 (Bug 1, 30) | ❌ 不影响 | 后端内部修复，API 契约不变 |
| 多存储不一致 (Bug 2, 9) | ❌ 不影响 | 数据完整性修复 |
| 降级副作用 (Bug 3, 6) | ❌ 不影响 | 返回空值而非错误数据 |
| 参数穿透 (Bug 17, 20, 28) | ❌ 不影响 | 工具调用参数修复，API 接口不变 |
| 量纲不一致 (Bug 8) | ❌ 不影响 | 分数归一化，返回格式不变 |
| null 防御 (Bug 10, 11, 14, 25) | ✅ 间接影响 | 前端需处理 `response: null` 字段，已在类型定义中用 `string | null` 标注 |

**结论**：当前后端 Bug 修复不涉及 API 接口变更，前端可完全独立并行开发。

---

> **文档结束**
>
> **版本**：v2.0
> **v2.0 更新摘要**（基于 DeepSeek 设计评审融合）：
> - 完整 Design Token 色彩体系（CSS 变量 + Tailwind 对齐 + 暗黑模式）
> - 页面交互规范（键盘快捷键 / 加载骨架屏 / 流式打字机效果 / 新手指引）
> - 新增 3 个页面：知识图谱大屏 / 应急响应 / GPU 推理监控
> - 新增依赖：react-markdown / react-force-graph / driver.js / Playwright
> - 前端 Docker 多阶段构建 + Nginx 反向代理 + docker-compose 集成
> - 增强测试策略：测试金字塔 + Playwright 视觉回归 + CI/CD 前端 job
> - Axios 拦截器增强：TraceId 全链路错误追踪
>
> **下一步行动**：
> 1. 执行 `npm create vite@latest agent1-web -- --template react-ts` 创建前端项目
> 2. 按 `3.3` 节安装全部依赖（包含新增的 react-markdown/react-force-graph/driver.js）
> 3. 配置 MSW Mock + Tailwind Design Token
> 4. 优先实现登录页 + 仪表盘（P0 可视化交付卡口）
> 5. 逐步实现合规审核、知识图谱、巡检管理、应急响应、工单管理页面
