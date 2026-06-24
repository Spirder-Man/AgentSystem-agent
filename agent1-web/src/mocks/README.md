# Agent1 前端 Mock 层 — 前后端并行开发核心机制

## 一句话原理

```
正常模式:  React → Axios → 网线 → Agent1.Api → PostgreSQL → 返回
Mock 模式: React → Axios → MSW (浏览器 Service Worker 拦截) → 返回假数据
                                ↑
                        网线根本没有发出
```

## 文件结构

```
agent1-web/src/
├── types/api.ts              ← 🏛️ 契约: 前后端唯一真相来源
├── lib/axios.ts              ← 网络层: Mock 和真实共用
├── mocks/
│   ├── handlers.ts           ← MSW 核心: 每个端点一个 handler
│   ├── server.ts             ← MSW 启动入口
│   ├── data/
│   │   ├── compliance.ts     ← 合规审核假数据 + 模板引擎
│   │   ├── inspection.ts     ← 巡检/资产假数据
│   │   └── tickets.ts        ← 工单假数据 + 状态流转引擎
│   └── README.md             ← 本文件
├── hooks/                    ← TanStack Query hooks
├── pages/                    ← 页面组件
└── stores/                   ← Zustand 状态
```

## 三层隔离

```
┌─────────────────────────────────────────────────────────────┐
│  L1 契约层: types/api.ts                                     │
│  前后端共同的"合同" — 接口格式、字段类型、状态枚举              │
│  后端改 → 契约更新 → 前端同步 → 双方各自实现                    │
├─────────────────────────────────────────────────────────────┤
│  L2 Mock 层: mocks/handlers.ts                               │
│  浏览器 Service Worker 拦截 HTTP 请求，返回模拟数据             │
│  前端不需要后端运行即可独立开发                                 │
├─────────────────────────────────────────────────────────────┤
│  L3 集成层: 真实 Agent1.Api                                   │
│  前端关闭 Mock → 请求直连后端 → 端到端验证                      │
└─────────────────────────────────────────────────────────────┘
```

## 启动方式

```bash
# 纯 Mock 模式（后端不需要启动）
cd agent1-web
echo "VITE_ENABLE_MOCK=true" > .env.mock
npm run dev:mock

# 连接真实后端
npm run dev   # VITE_ENABLE_MOCK 默认 false
```

## 关键设计决策

### 1. 为什么 MSW 不是 JSON 文件

| 方案 | 问题 |
|------|------|
| 静态 JSON 文件 | 前端代码里写 `if (MOCK) return mockData` — 侵入业务代码，切真实API 要改代码 |
| 本地 mock server (express) | 需要额外进程，端口管理，和真实API 两套地址 |
| **MSW Service Worker** ✅ | 拦截在网络层，业务代码完全无感知。关掉就是真实API，打开就是 Mock |

### 2. 为什么 handler 里有 simulateLlmDelay()

前端需要开发 LLM 推理中的「进度指示器」和「超时处理」。Mock 不加延迟 → 这些UI永远测不到。

### 3. 为什么 mock 数据有模板匹配引擎

`getComplianceResponse(query)` 根据关键词返回不同的合规结论，而不是永远返回同一条。这样前端开发时能看到不同输入对应的不同 UI 表现（合规/不合规/警告/法规引用）。

### 4. 为什么 tickets 有状态流转引擎

`applyTicketStatusUpdate()` 实现了真实的状态机（New→Confirmed→InProgress→Remediated→VerifiedClosed），前端开发工单流转功能时不依赖后端状态变更逻辑。

## Handler 对照表

| 端点 | 延迟 | 说明 |
|------|------|------|
| POST /api/Auth/login | 300ms | 模拟网络+验证 |
| POST /api/Auth/refresh | 200ms | |
| POST /api/Auth/logout | 100ms | |
| GET /api/Compliance/summary | 200ms | 仪表盘核心数据 |
| **POST /api/Compliance/check** | **2-5s** | ⚡ 模拟 LLM 推理 — 前端进度条测试关键 |
| POST /api/Compliance/hazard/query | 500ms | |
| POST /api/Compliance/storage/compatibility | 500ms | |
| GET /api/Inspection/plans | 200ms | |
| POST /api/Inspection/plans | 300ms | |
| GET /api/Inspection/plans/:id | 200ms | |
| POST /api/Inspection/plans/:id/execute | 2-5s | LLM 推理 |
| GET /api/Inspection/rounds/:id | 200ms | |
| GET /api/Inspection/reports/:id | 200ms | |
| GET /api/Inspection/reports/:id/export | 100ms | |
| GET /api/Inspection/assets | 200ms | |
| POST /api/Inspection/scan | 2-5s | LLM 推理 |
| POST /api/Inspection/quick-check | 1.5s | |
| GET /api/Tickets | 200ms | |
| PUT /api/Tickets/:id/status | 300ms | 含状态流转验证 |
| GET /health | 50ms | |
| GET /metrics | 立即 | Prometheus text format |
| GET /cache/stats | 立即 | |
| POST /cache/clear | 立即 | |
| POST /knowledgebase/incremental-update | 2s | |
| GET /memory/stats | 立即 | |

## 从 Mock 切换到真实 API

**零代码改动。** 只需要改环境变量:

```bash
# .env (真实后端)
VITE_API_BASE_URL=http://localhost:5000
# 不设置 VITE_ENABLE_MOCK 或设为 false
```

组件完全不感知 Mock 层:

```typescript
// useComplianceCheck.ts — 无论 Mock 还是真实 API，代码完全一样
export function useComplianceCheck() {
  return useMutation<ComplianceResponse, Error, ComplianceRequest>({
    mutationFn: (req) =>
      apiClient.post('/api/Compliance/check', req).then((r) => r.data),
  });
}
```

## 前后端并行开发流程

```
时间线 →

后端线 (修 Bug)          前端线 (基于 Mock)       契约线 (types/api.ts)
─────────────────      ─────────────────      ─────────────────
修复 RAG 管道            配置 Vite + React        定义类型
(API 接口不变)           + MSW Mock              评审通过
    │                        │                       │
    │                   开发登录页                     │
    │                   POST /api/Auth/login          │
    │                   → MSW 返回 Mock Token          │
    │                        │                       │
修复工具调用链路          基于 Mock 开发仪表盘           │
CallToolAsync 传参       GET /api/Compliance/summary    │
    │                   → MSW 返回假 KPI               │
    │                        │                       │
    │                   开发合规审核页                    │
    │                   POST /api/Compliance/check       │
    │                   → MSW 2-5s 延迟 + 合规结论       │
    │                        │                       │
    │                   开发巡检管理页                    │
    │                   开发工单管理页                    │
    │                        │                       │
    ▼                        ▼                       ▼
后端 Bug 修复完毕    所有页面在 Mock 下走通        契约冻结
    │                        │
    └──────────┬─────────────┘
               │
         关闭 Mock (VITE_ENABLE_MOCK=false)
         切换到真实 API
               │
         端到端集成测试
               │
            🚀 上线
```

## 新手指南

1. **启动前端项目**: `cd agent1-web && npm install && npm run dev:mock`
2. **打开浏览器**: `http://localhost:5173`
3. **登录**: 任意用户名+密码即可登入（Mock 不验证）
4. **查看仪表盘**: 数据来自 `mocks/data/compliance.ts`
5. **提交合规查询**: 试试"苯和丙酮能放在同一个仓库吗"→ 看到完整合规分析
6. **查看工单**: 点击工单管理 → 看到 5 条不同状态的工单
