# _archive/ 归档区索引（本地专属，不入 Git）

> **归档日期**：2026-07-18（仓库目录规范化批次 v4.3）
> **归档原则**：零删除——所有文件仅移动、未删除，由你亲自筛选处置
> **处置标记**：🟢 建议保留 | 🔴 建议删除 | 🟡 待定（你决策）

---

## ⚠️ 安全警告：密钥轮换清单（P0，请尽快处理）

以下密钥曾以明文形式提交进 Git 历史（本次已从当前版本移除，但**历史提交中仍可见**）。
本次采用"轮换提醒"策略而非 Git 历史重写；若需彻底清除历史，可后续单独执行 `git filter-repo`。

| 泄露项 | 出现位置（已归档） | 建议动作 |
|--------|--------------------|----------|
| AutoDL 服务器 root 密码 | secrets-scripts/Program.cs、ssh_prog.cs | **立即在 AutoDL 控制台重置实例密码**（若实例仍在使用） |
| 服务器地址+端口 | 同上 | 随密码重置一并失效 |
| JWT_KEY（2 个历史值） | secrets-scripts/rebuild_api.sh、restart_api.sh、start_services.sh、temp_rebuild.sh、targeted_test.sh | 生产 `.env` 更换为新的 ≥32 位随机串 |
| DB_PASSWORD（2 个历史值） | 同上 + fix_db.sh | 重置 PostgreSQL postgres 用户密码并更新 `.env` |
| admin/auditor 账户密码 | 同上（AUTH_ACCOUNTS_JSON） | 更新 `.env` 中 `AUTH_ACCOUNTS_JSON` 为新密码 |

---

## 1. secrets-scripts/ — 含密钥历史脚本（11 个）

| 文件 | 原位置 | 原用途 | 处置建议 |
|------|--------|--------|----------|
| Program.cs | 根目录 | SSH 装公钥一次性程序（含 root 密码） | 🔴 密钥已泄露且属一次性工具 |
| ssh_prog.cs | 根目录 | 同上（另一版本） | 🔴 同上 |
| rebuild_api.sh | 根目录 | 远程重建+重启 API（违反架构边界：进程管理+硬编码密钥） | 🟡 若远程仍需，改造为从 .env 读取后放服务器本地，不入仓库 |
| restart_api.sh | 根目录 | 远程重启 API | 🟡 同上 |
| start_services.sh | 根目录 | 远程一键启动全部服务 | 🟡 同上 |
| fix_db.sh | 根目录 | 远程重置 PG 密码+连通性检查 | 🟡 密码重置后此脚本内容即过期 |
| temp_rebuild.sh | 根目录 | 临时重建脚本 | 🔴 temp 性质 |
| test_jwt.sh | 根目录 | JWT 登录链路手工验证 | 🔴 已有 xUnit 覆盖（ApiIntegrationTests） |
| check_env.sh | 根目录 | 远程环境检查 | 🟡 可并入 scripts/zh-diag.sh |
| upload.ps1 | 根目录 | 部署上传+默认密钥检查（暴露默认密钥值） | 🟡 改造后可回 scripts/ |
| targeted_test.sh | scripts/ | 定向 CLI 测试（含 JWT/DB 密钥） | 🔴 违反测试归一规则 |

## 2. temp-files/ — 临时产物（8 个）

| 文件 | 原位置 | 说明 | 处置建议 |
|------|--------|------|----------|
| temp_audit.json / temp_emergency.json / temp_graph.json / temp_login.json | 根目录 | API 手工测试请求体样本 | 🔴 已有 Swagger/测试覆盖 |
| temp_t13_runner.b64 | 根目录 | T13 脚本 base64 传输残留 | 🔴 传输残留 |
| start-b64.txt | 根目录 | base64 启动脚本残留 | 🔴 同上 |
| test_results_0629.b64 / test_results_0629.tar.gz | 根目录 | 2026-06-29 测试结果打包 | 🟡 若已入 Bug 知识库可删 |

## 3. test-outputs/ — 测试输出快照（15 文件 + TestResults/）

| 文件 | 说明 | 处置建议 |
|------|------|----------|
| test-output.txt ~ test-output5.txt、test-output-l0.txt、l6_test_output.txt | 各轮 dotnet test 控制台输出 | 🔴 结论已沉淀至 Bug 知识库 |
| remote_arch_report.txt、remote_arch_test.txt、remote_bench.txt、remote_test_log.txt、remote_test_full.log | 远程架构/基准/回归测试输出 | 🟡 remote_test_full.log 若含未复盘内容先复盘再删 |
| tunnel-err.log | SSH 隧道错误日志 | 🔴 |
| workflow-run-67-summary.png | CI Run #67 截图 | 🟢 CI 里程碑证据，可移 docs/project/ |
| 架构收敛测试报告.txt | 架构收敛测试输出（ArchitectureTest/ 目录内有同名归档） | 🔴 重复件 |
| TestResults/ | 本地 trx/覆盖率产物目录 | 🔴 可再生成 |

## 4. legacy-test-scripts/ — 历史遗留测试脚本（16 个）

> 架构边界规则明确：此类脚本属历史遗留，逐步迁移至 xUnit。迁移完成前保留在此。

| 文件 | 原位置 | 说明 | 处置建议 |
|------|--------|------|----------|
| auto_test.sh / auto_test_v2.sh / auto_test_v2.b64 | scripts/ | 25 条 CLI 全功能自动化测试 | 🟡 迁移 xUnit 后删；README 历史记录已标注归档位置 |
| int-test-task11.sh | scripts/ | Task11 集成测试（592 行） | 🟡 同上 |
| agent_test.py / architecture_test.py / run_rag_test.py / simple_test.py / test_architecture.py / test_manual.py | scripts/ | Python 模拟 CLI 测试族 | 🔴 违反测试归一红线，功能已被 xUnit 覆盖 |
| targeted 相关：t13_b64.txt / run_t13_heartbeat.sh / test-status.sh | scripts/ | T13 心跳与状态残留 | 🟡 T13 已固化进 EvalEngine，复盘后可删 |
| start-api.sh | scripts/ | nohup 后台启动 API（红线违规） | 🔴 用 `dotnet run --project Agent1.Api` 替代 |
| load_test.ps1 / debug_chat.ps1 | 根目录（未跟踪） | 本地压测/调试脚本 | 🟡 有用逻辑可改造进 Benchmark |

## 5. side-projects/ — 侧项目（6 个）

| 目录 | 说明 | Git 状态（原） | 处置建议 |
|------|------|----------------|----------|
| ssh-runner/ | SSH 远程执行工具（SshRunner.csproj） | 曾跟踪 | 🟢 **仍被引用**：scripts/download_logs.ps1、monitor-test.ps1 已指向新路径 |
| ssh-tunnel/ | SSH 隧道工具（远程 E2E 依赖） | 曾跟踪 | 🟢 远程联调仍需；根目录残留 stderr.log 为运行中进程占用，进程结束后可并入 |
| task-email/ | 任务邮件通知子项目 | 曾跟踪 | 🟡 独立仓库管理更合适 |
| cache-monitor/ | DeepSeek Cache 监控 MCP 服务 | 部分跟踪 | 🟡 同上 |
| gravity-maze/ | 重力迷宫小游戏（与主项目无关） | 未跟踪 | 🟡 移出仓库目录 |
| wechat-group-report/ | 微信群报告工具 | 未跟踪 | 🟡 同上 |

## 6. docs-duplicates/ — 文档重复副本（31 份）

SHA256 哈希校验**完全一致**才移入，保留的正本在 `docs/` 分类目录中。
完整清单见 [moved-list.txt](docs-duplicates/moved-list.txt)。处置建议：🔴 全部为逐字节重复，确认后可整体删除。

| 重复组 | 份数 | 保留正本位置 |
|--------|------|--------------|
| docs 根散落副本（十项决策/断点地图+34图/for循环/FunctionCalling） | 5 | architecture/、technical-principles/、testing/integration/ |
| docs/testing 根副本 | 9 | testing/unit、integration、manual |
| docs/architecture/_archive 旧版副本 | 9 | architecture/、articles/ |
| docs/_archive 旧版副本 | 7 | project/、learning-notes/、technical-principles/、articles/ |
| articles 重复（K1-K9） | 1 | learning-notes/ |

## 7. chat-logs/ — 对话导出（1 个）

| 文件 | 说明 | 处置建议 |
|------|------|----------|
| chat_20260718_Agent1-2974b3fc_2e89595a.md | AI 结对编程对话导出 | 🟡 有复盘价值可留，注意其中可能含敏感上下文 |

## 8. learning/ — 代码学习笔记（1 个）

| 文件 | 原位置 | 说明 | 处置建议 |
|------|--------|------|----------|
| Microsoft.SemanticKernel.KernelFunctionAttribute.学习笔记.cs.txt | Agent1/Microsoft.SemanticKernel | SK 反编译注释学习笔记（非编译依赖，曾干扰项目目录） | 🟢 可移 docs/learning-notes/ |

## 9. dedupe-docs.ps1 — 本次去重工具脚本

一次性工具，保留在归档区备查（重跑安全：仅处理哈希一致文件）。

---

## 恢复方法

所有文件均为 `Move-Item` 平移，恢复即反向移动，例如：

```powershell
# 恢复单个文件到原位置
Move-Item "_archive\secrets-scripts\check_env.sh" ".\check_env.sh"

# 恢复整个侧项目
Move-Item "_archive\side-projects\task-email" ".\task-email"
# 注意：恢复被 .gitignore 覆盖的路径后，如需重新入库要先调整 .gitignore 再 git add
```
