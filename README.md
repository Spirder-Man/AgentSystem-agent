# Agent1 — 化工园区危化品合规审查 AI Agent

> **版本**：v4.8 | **编译**：0 错误 | **测试**：1565 用例（对基线 NEW_FAILURES=0） | **分支**：`linux原生编译模型llama.cpp`

基于 .NET 8 + Semantic Kernel + **llama.cpp 原生编译**构建的企业级化工园区危化品合规审查 AI Agent。支持 REST API、JWT 认证、PostgreSQL+pgvector 混合检索、OpenTelemetry 可观测性、等保三级审计（SHA256 哈希链），**针对 NVIDIA GPU (RTX 3090/3080 Ti) Linux 环境 RAG 全链路 GPU 加速**。

## 功能全景

```
┌──────────────────────────────────────────────────┐
│  LLM 降级体系  │  门卫+责任链+规则引擎确定性兜底    │
│  AI 推理引擎  │  SK Auto FC + 三层防御 + GPU嵌入   │
│  化工合规工具  │  8 个 [KernelFunction] + Token预算 │
│  知识库       │  BM25+Vector+RRF+增量更新          │
│  化工业务模块  │  合规自查/工单/监管/应急/图谱      │
│  基础设施     │  SHA256审计链/安全双防线/健康检查   │
│  可观测性     │  PipelineMetrics/TraceId/事件溯源  │
│  API 服务     │  JWT认证/限流/OTel/优雅关闭        │
└──────────────────────────────────────────────────┘
```

## 🏗️ 项目架构

```
Agent1/                 # .NET 8 核心类库
├── Models/             # 数据模型（CliExecutionResult/PipelineMetrics/ChemicalSubstance等）
├── Commands/           # 命令模式（14 个菜单命令）
├── Modules/            # 推理模块（CoT/ReAct/Reflection/RAG/合规检查等 12 个）
├── Services/
│   ├── AI/             # LLM 推理 + Token预算 + 反射验证 + 熔断器
│   ├── Compliance/     # 化工合规（双通道解耦架构 + 58种危化品数据库）
│   ├── Knowledge/      # 知识库（BM25+向量混合检索 + Reranker + 缓存）
│   ├── Dialog/         # 对话管理 + 意图路由
│   ├── Memory/         # 记忆系统 + 响应缓存
│   ├── Infrastructure/ # 数据库 + 审计 + 指标 + 脱敏
│   └── Eval/           # T13 无状态评测引擎
├── Config/             # 配置中心
└── Program.cs          # 控制台入口
Agent1.Api/             # Web API 层（15 个 Controller + 5 个 Middleware + ScanProgressService 异步扫描）
Agent1.Tests/           # xUnit 测试（1565 用例）
agent1-web/             # Vue 3 前端（MSW Mock 并行开发）
docs/                   # 项目文档（架构/部署/测试/排障）
scripts/                # 开发者工具箱（日志下载/远程监控）
```

## 🛠️ 技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 语言/框架 | C# 12 / .NET 8 | Semantic Kernel 1.74 |
| **推理引擎** | **llama.cpp (llama-server)** | Qwen3-8B Q4_K_M + nomic-embed F16 |
| **精排** | **bge-reranker-v2-m3** | Python sidecar（可选降级） |
| 数据库 | PostgreSQL 16 + pgvector | BM25 + 向量混合检索 |
| 认证 | JWT Bearer + BCrypt | RefreshToken 轮转 |
| 可观测性 | Serilog + Prometheus + Grafana + OTel | 结构化日志 + 分布式追踪 |
| CI/CD | GitHub Actions | 编译→测试→Docker→GHCR |
| **GPU** | **RTX 3090 24GB (sm_86)** | CUDA 编译 llama.cpp |

## ✨ 核心功能

- **双通道解耦架构** ★：法规引用归 C# 确定性代码（100% 准确），推理分析归 LLM — 四道防线防幻觉
- **LLM 降级体系 v2** ★：门卫(信号词粗筛) → 责任链(多Handler精细匹配) → 规则引擎确定性兜底。新增场景仅需一行注册
- **RAG GPU 全链路加速**：批量嵌入 + GPU 向量索引 + Cross-Encoder Reranker + 查询缓存
- **结构化化学品数据库**：58 种危化品（CAS/UN编号/闪点/爆炸极限）+ 20 组储存禁忌 + 安全距离
- **8 个 AI 工具**：危险类别查询/储存兼容性/安全距离/化学品属性/法规引用 + GHS 标签识别
- **12 个推理模块**：CoT/ReAct/Reflection/RAG/合规自查/工单跟进/监管核查/应急响应/知识图谱
- **等保三级审计**：SHA256 哈希链防篡改 + 启动自愈

## 🚀 快速开始

### 本地开发

```bash
# 前置：PostgreSQL 16 + pgvector（见 init_database.sql）
dotnet run --project Agent1      # 控制台菜单
dotnet run --project Agent1.Api  # API 服务 (localhost:52320)
```

### 前端开发

```bash
cd agent1-web && npm install
npm run dev:mock   # Mock 模式（纯前端，不需要后端）
npm run dev        # 真实 API 模式

# E2E 测试（三层契约架构）
npm run test:e2e        # MSW Mock 层 — CI 快速门禁（不依赖后端 GPU/DB）
npm run test:e2e:real   # 真实 GPU 层 — 全链路（需 SSH 隧道 + 远程 GPU）
npm run tunnel:start    # 启动 SSH 隧道（真实 E2E 前置）
```

### Linux 生产环境（GPU）

> 完整部署文档：[Docker 容器化一键部署](docs/deploy/Docker容器化一键部署.md) | [Linux 快速启动指南](docs/deploy/Linux快速启动指南.md)

核心步骤：安装 .NET 8 SDK → PostgreSQL 16 + pgvector → 编译 llama.cpp(CUDA) → 下载 GGUF 模型 → 启动 llama-server(:8080) + llama-embed(:8081) → `dotnet run`

### Docker 部署

```bash
docker compose build llama-server llama-embed api
docker compose up -d
curl http://localhost:5000/health/live
```

## 🔐 安全配置

三层优先级：`环境变量 AUTH_ACCOUNTS_JSON` > `appsettings.json` > 开发随机密码（仅非生产）

```bash
# 生产环境必须设置
export JWT_KEY=your-key-at-least-32-chars
export DB_PASSWORD=your_pg_password
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"...","Role":"admin"}]'
```

| 角色 | 权限 |
|------|------|
| `admin` | 全部功能 + GPU 监控 + 系统管理 |
| `auditor` | 仪表盘 + 合规审核 + 巡检 + 工单 |
| `viewer` | 只读：仪表盘 + 查看报告 |

## 🖥️ 前端项目

Vue 3 + TypeScript + Element Plus + Vite 5，支持 MSW Mock 前后端并行开发。页面覆盖：登录/仪表盘/合规审核/危化品查询/巡检/资产台账/知识图谱/应急响应/工单/系统管理。

**E2E 三层契约架构**（`agent1-web/e2e-real`）：

- **Layer 1 Test-ID 契约**：`src/test-ids.ts` 作为 data-testid 单一真值源，`scripts/check-test-ids.mjs` CI 自动校验注册合规性
- **Layer 2 数据契约**：`e2e-real/fixtures/data-manifest.ts` 声明式数据依赖，`global-setup.ts` / `seed-test-data.mjs` 自动检查并补种
- **Layer 3 质量基线契约**：`e2e-real/baseline.json` + `utils/llm-assertions.ts` 基于真实推理基线的可演进断言，`baseline-collector.mjs` 离线采集
- **双 E2E 分层**：`playwright.config.ts`（MSW Mock CI 门禁）+ `playwright.real.config.ts`（真实 GPU 主力，经 SSH 隧道全链路）
- **代码质量**：ESLint(flat config) + Prettier，经 husky `pre-commit` + lint-staged 在提交时自动校验格式化

## 📊 可观测性

```
/metrics       → Prometheus 指标
/health        → 全量健康检查（DB + LLM + KB）
/health/live   → 存活检查
/health/ready  → 就绪检查
```

## 🔄 CI/CD

Push → `build-and-test`（编译+单元测试+架构收敛）→ `integration-test`（PostgreSQL 集成）→ `docker`（仅 main 分支，推送 GHCR）→ `notify`（失败 QQ 邮箱告警）

## 📁 文档

```
docs/
├── architecture/         # 架构设计
├── deploy/               # 部署运维
├── technical-principles/ # 技术原理
├── testing/              # 测试方案与手册
├── troubleshooting/      # 故障排查
├── project/              # Bug知识库 + 自检清单
└── learning-notes/       # 学习笔记
```

## 📋 近期更新

### v4.8 — 十项问题分批修复 + 系统血谱方法论落地（2026-07-28）

> 源自一次远程运行日志的十项问题清单（2P1 + 5P2 + 3P3），逐项日志实锤归因后分批修复，全程零概率性手段。详见 [十项问题多维度深度分析报告](docs/analysis/2026-07-28_十项问题多维度深度分析报告.md)。

- **#1 Bug-035（P0）SQLite 兜底库静默降级**：`SplitSqlStatements` 天真 `Split(';')` 被 `GROUP_CONCAT(..., '; ')` 字面量击穿——删除拆分器改整段 `ExecuteNonQuery`（原生支持多语句）；catch 不再置 `_initialized=true`，降级可重试可观测。提炼警示模式 W24「手写解析器低估目标语法复杂度」
- **#2 幻觉法规号硬校验**：`OutputValidator` 白名单硬校验——库外 GB 编号替换为【待核实】标注（不删除、不打分）；白名单取三源并集（regulation_versions 种子 ∪ 知识图谱 ∪ 硬编码字典源）防误杀
- **#3 安全距离设施对补全**：按 GB 50016/50160 表格补全设施对（每条带条款出处），SQLite 种子/PG 迁移/测试桩三处同源拷贝同步
- **#4 Dashboard 扫描异步化**：同步阻塞 521s → `ScanProgressService` 202+scanId 后台任务 + GET 轮询 + 并发 409，前端 2s 轮询；复用既有进度回调零新增埋点
- **#5 知识库乱码闸门**：新增 `GarbledTextDetector` 三规则守门员，5 处入库循环过滤 + WRN 留痕（存量乱码块待知识库重建清除）
- **#6 告警收件人**：本地配置实为完备（调研勘误），仅补前端空值防御；远程 `.env` 补配待实例开机
- **#7 缓存预热双格式兼容**：`WarmupFromEvalSet` wrapper 对象优先 + 数组兜底，与 EvalEngine 同构
- **#8 quality-rules.json 构建分发**：根因是 csproj 缺 Content 分发项（非文件缺失），补分发后启动 WRN 消除
- **#9 CS0618 警告收敛**：`AgentDialog` 私有字段承载内部状态 + Obsolete 属性只读透出——CS0618 14→0，警告对外部调用者保留
- **#10 GetCurrentTime 取舍标注**：领域化 system prompt 下时间类工具低触发是预期副作用，拒绝概率性调 prompt，文档标注取舍理由（不改代码）
- **系统血谱方法论**：新增 [系统血谱](docs/architecture/系统血谱.md)（L0 大动脉/L1 静脉持久主图 + 同源拷贝登记表 + 病灶登记表）与 `system-blood-map` 技能——代码变更前先输出影响血管清单，改完回写主图
- **最终回归**：三项目 0 error；后端 1565 测试 NEW_FAILURES=0；前端 vitest 350/356（6 失败实锤为既有问题）+ vue-tsc 通过

### v4.7 — 架构收敛：降级路径统一为门卫+责任链+规则引擎（2026-07-27）

- **Bug-033 架构收敛**：4 条 LLM 零工具调用降级路径（E1正则猜测/FC=Required违约→BuildNoResult/熔断器打开→规则引擎/"生成失败"→规则引擎）统一收敛为单一入口 `TryFallbackToRuleEngine`
  - **DeterministicRuleEngine 重构**：新增 `IComplianceQueryHandler` 接口 + `ChemicalSignalGate` 信号词门卫（18 个化工关键词粗筛）+ 3 个 Handler 类（StorageCompatibility/HazardCategory/SafetyDistance）→ `_handlers` 责任链 → 首个命中返回
  - **AgentDialog 三调用点统一**：ExecuteChemicalComplianceAsync/ExecuteEvalInternalAsync/ExecuteEvalPerCaseAsync → 全部替换为 TryFallbackToRuleEngine
  - **LlmService 清理**：删除 `TryKeywordToolFallbackAsync` 方法（40 行 3 个正则模式，伪工程方案）
- **长期可演进策略**：门卫拦无关输入（"蜘蛛侠"不触发 handler），责任链匹配具体场景，新增合规场景（如应急响应）仅需新增一个 `IComplianceQueryHandler` 实现 + 在 `_handlers` 列表加一行注册，核心方法 `TryHandleComplianceQuery` 永不再改
- **根因方法论**：Git 历史分析发现同一开发者在 35 天内分 4 次 commit 加入互不感知的降级路径，根因是"LLM 完全宕机"和"LLM 未调用工具"的心理边界分裂。提炼出「设计理据追问法」— 看到 AI 生成的代码时问 3 个问题：为什么选这个方案？暴露了什么认知盲区？应该在哪个环节纠正？
- **Bug 知识库**：新增 Bug-033 完整记录 + W22 系统弱点 + 7 节点思维链路复盘
- 详见 [Bug知识库](docs/project/Bug知识库.md) Bug-033

### v4.6 — 工程侧7项缺陷修复：幻觉防护 + 别名归一化 + 评测准确性（2026-07-25）

- **E1 FC关键字兜底机制（v4.7 已重构为门卫+责任链架构）**：原LLM零工具调用时按关键词正则自动触发 ChemicalComplianceTools，见下方 v4.7 架构收敛
- **E2 Prompt反幻觉指令强化**：将 `[REGULATIONS:]` 标签引用改为自然语言「所见即所得」式指令，明确GB编号相似≠相同规则
- **E3 法规编号精确匹配**：`IsRegulationAllowed` 删除 Contains 模糊匹配，仅保留 Equals；`NormalizeRegNumber` 新增年份后缀剥离
- **E4 RAG检索来源去重**：重排序后增加来源去重——每源文档最多2条，至少3个不同来源，防止单一文档垄断 topK
- **E5 Level4幻觉检测漏报修复**：检测到幻觉后更新 `ConclusionReasons[0].Passed=false`，消除 41 条漏报
- **E6 白名单双路径不一致修复**：`ExtractGbNumbers` 降级为 else 兜底，仅无 `[REGULATIONS:]` 标签时使用
- **E7 化学品名称归一化重构**：删除冗余的 `SubstanceAliasMap` 硬编码字典（14条映射：13条冗余 + 1条错误「盐酸→氯化氢」），统一归一化至 `ChemicalSubstanceDatabase.Lookup`——新增别名只需在数据类 `Aliases` 字段加一行，自动注册

### v4.5 — E2E 三层契约架构落地 + 提交钩子环境修复（2026-07-21）

- **E2E 三层契约架构**：Test-ID 契约（`test-ids.ts` 单一真值源 + CI 校验）/ 数据契约（`data-manifest.ts` 声明式依赖 + 自动补种）/ 质量基线契约（`baseline.json` + `llm-assertions.ts` 可演进断言）
- **双 E2E 分层**：`playwright.config.ts` MSW Mock CI 门禁 + `playwright.real.config.ts` 真实 GPU 全链路（经 SSH 隧道）；新增 `test:e2e:real`、`tunnel:start/stop`、`health:check` 等 npm 脚本
- **契约消费重构**：14 个 e2e/e2e-real spec 改用 `getByTestId()` 精确定位，6 个 Vue 组件 data-testid 规范化，消除 strict mode 冲突
- **提交钩子修复**：补齐缺失的 ESLint(flat config)/Prettier 依赖与配置，`pre-commit` + lint-staged 恢复可用
- **`.gitignore` 收敛**：忽略 `playwright-report/`、`test-results/`、`eval_reports/` 等测试产物与含令牌的本地脚本

### v4.4 — Bug-032 v2 回马枪：防御代码位置正确性（2026-07-20）

- **Bug-032 v2**：FC=Required 违约检测从 `HasAnyToolResult` 之后提升为独立最高优先级闸门
  - v1 (7/18, `5dcf4f2`)：`toolCalls==0` 检查放在 `else` 分支内，被 `HasAnyToolResult==true` 时提前 return 绕过
  - v2 (7/20, `94e4818`)：`toolCalls==0` 提升为 `HasAnyToolResult` 之前的独立闸门，无条件拦截 FC 违约
  - 远程 3 轮扫描验证：v1 拦截 0 次，v2 拦截 10 次（Prometheus: `agent1_fc_contract_violation_total=10`）
- **编译缓存陷阱**：`dotnet run` 增量编译可能不反映源码变更 → 远程部署关键修复需 `rm -rf bin/obj && dotnet build --force`
- **Bug 知识库**：新增 N8 思维节点，记录防御代码"位置正确性"与"逻辑正确性"双维度分析方法论

### v4.3 — P0 Bug 修复批次（2026-07-18）

- **Bug-031**：审计哈希链彻底修复 — 移除 createTime 依赖 + 启动自愈
- **Bug-032**：FC=Required 违约兜底 — toolCalls==0 时丢弃 LLM 废话，走确定性拒绝模板（v2 回马枪见 v4.4）
- **IsDirty 阈值**：短文本拦截 `< 20` → `< 5`，H166 模板不再误拦截
- **IntentRouter**：新增 25 个关键词，覆盖化学品名/仓库/消防/安全术语
- **LLM 扫描预检**：`CheckLlmHealthAsync()` 3 秒快速预检，不可用时 503
- **缓存预热修复**：JSON 反序列化兼容双格式
- **API 端口对齐**：前端代理端口修正为 52320
- 详见 [Bug知识库](docs/project/Bug知识库.md) Bug-029/030/031/032

---

**文档版本**：v4.21 | **最后更新**：2026-07-28 | **许可证**：MIT
