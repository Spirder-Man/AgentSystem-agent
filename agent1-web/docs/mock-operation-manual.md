# Agent1-Web Mock 模式操作手册

> **用途**：不依赖任何后端服务（PostgreSQL / llama.cpp / .NET API），纯前端独立运行，用于功能验证、UI 调试和业务熟悉。
>
> **原理**：MSW (Mock Service Worker) 在浏览器 Service Worker 层拦截所有 `/api/*` 请求，返回预设模拟数据。

---

## 一、快速启动（已解决问题后）

```powershell
# 1. 进入前端目录
cd d:\桌面\agent\项目\Agent1\agent1-web

# 2. 直接启动（已修改 main.ts，开发模式自动启用 MSW）
npm run dev
```

启动后终端显示：
```
VITE v5.x.x  ready in xxx ms
➜  Local:   http://localhost:5173/
```

---

## 二、验证 Mock 是否生效

1. 浏览器打开 `http://localhost:5173`（自动跳转到 `/login`）
2. **按 F12 → Console 面板**，确认出现：
   ```
   [MSW] Mock Service Worker 已启动
   ```
3. **登录**：
   - 用户名：`admin`（admin 角色，全权限）
   - 密码：任意（Mock 模式不校验密码）
4. **Network 面板验证**：发起请求后，查看是否出现来自 MSW 的响应

---

## 三、常见问题与解决方案

### 问题 1：Console 没有 `[MSW] Mock Service Worker 已启动`

**原因**：`.env` 文件编码问题（PowerShell `echo` 默认 UTF-16，Vite 无法解析）。

**已永久修复**：修改了 `main.ts`，将 `if (import.meta.env.VITE_ENABLE_MOCK === 'true')` 改为 `if (import.meta.env.DEV)`。开发模式(`npm run dev`)自动启动 MSW，不再需要 `.env` 文件或环境变量。

```powershell
cd d:\桌面\agent\项目\Agent1\agent1-web
npm run dev
```

如果你是从旧分支拉代码，仍需要用以下方式：
```powershell
Set-Content .env.mock "VITE_ENABLE_MOCK=true"
npm run dev:mock
```

### 问题 2：PowerShell `Set-Content` 报 `DirectoryNotFoundException`

```
Set-Content : 未能找到路径“...\agent1-web\agent1-web\.env.mock”的一部分
```

**原因**：路径写重复了。当前已经在 `agent1-web` 目录，只需写 `.env.mock`：

```powershell
# 错误（当前已在 agent1-web，又加了 agent1-web/ 前缀）
Set-Content agent1-web/.env.mock "内容"

# 正确
Set-Content .env.mock "内容"
```

### 问题 3：登录后页面闪一下又回到登录页

**原因**：MSW 未启动 → 请求被 Vite 代理转发到真实后端 → 无后端服务 → 请求失败。

**排查步骤**：
1. Console 是否有 `[MSW] Mock Service Worker 已启动`
2. Network 面板查看 `/api/Auth/login` 的响应状态码
3. 如果返回 401/Network Error → MSW 没拦截到请求

**解决方案**：按第一节的快速启动步骤重新执行。

---

## 四、Mock 模式登录角色

| 用户名 | 角色 | 权限 |
|:---|:---|:---|
| `admin` | admin | 所有权限（CRUD + 系统设置） |
| 包含 `auditor` 的用户名 | auditor | 读写业务数据 |
| 其他任意用户名 | viewer | 只读 |

密码任意。

---

## 五、Network 面板各 API 端点

| Method | 端点 | 对应页面 |
|:---|:---|:---|
| POST | `/api/Auth/login` | 登录 |
| GET | `/api/Dashboard/overview` | 仪表盘 |
| GET | `/api/Dashboard/findings` | 合规发现 |
| GET | `/api/Dashboard/history` | 巡检历史 |
| GET | `/api/Dashboard/report/hazard` | 隐患报告 |
| POST | `/api/Compliance/check` | 合规检查 |
| POST | `/api/Emergency/respond` | 应急响应 |
| GET | `/api/KnowledgeBase/search` | 知识库检索 |
| GET/POST | `/api/Inspection/*` | 巡检计划 |

---

## 六、注意事项

- **不要同时开 Mock 和真实后端**：MSW 启动后拦截所有 `/api/*` 请求，真实后端的请求不会到达
- **两个终端窗口不要混用环境变量**：Mock 模式设了 `VITE_ENABLE_MOCK=true`，真实模式不能有这个变量
- **遇到问题第一步**：看浏览器 Console 有没有 `[MSW]` 日志，这是判断 Mock 是否生效的唯一标准

---

## 七、操作日志

> 以下记录使用过程中遇到的问题和解决方案，后续持续补充。

| 日期 | 问题 | 解决方案 |
|:---|:---|:---|
| 2026-07-24 | `echo >` 创建 `.env` UTF-16 编码导致 MSW 不启动 | 改用 `Set-Content` 或直接设环境变量 |
| 2026-07-24 | `Set-Content` 路径重复报 `DirectoryNotFoundException` | 当前在 `agent1-web/` 目录就用 `.env.mock` |
