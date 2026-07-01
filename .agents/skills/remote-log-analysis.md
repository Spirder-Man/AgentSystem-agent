# 远程日志分析与自动化测试操作规范 (Remote Log Analysis Skill)

## 技能描述

本技能用于规范远程 Linux 服务器上的自动化测试执行、日志收集、结构化分析与问题诊断全流程。基于 2026-06-29 远程全量测试的实践经验总结，确保每次远程测试能够按标准化流程高效执行，节省 token 消耗并快速产出分析结果。

## 项目根目录约定

> **本技能所有本地路径均使用相对于项目根目录的路径。使用时请将 `<PROJECT_ROOT>` 替换为实际项目路径。**

```powershell
# PowerShell 中自动检测项目根目录（本技能文档所在目录的上两级）
$PROJECT_ROOT = Resolve-Path "$PSScriptRoot\..\.."
# 或手动指定（示例，请替换为你的实际路径）
# $PROJECT_ROOT = "D:\你的项目路径\Agent1"
```

| 路径变量 | 相对路径 | 说明 |
|------|------|------|
| `$PROJECT_ROOT` | `.` | 项目根目录 |
| SSH 工具 | `ssh-runner/bin/Release/net8.0/SshRunner.exe` | C# SSH 客户端 |
| 测试脚本 | `scripts/auto_test_v2.sh` | 全量测试 v2 |
| 诊断脚本 | `scripts/zh-diag.sh` | 环境诊断与启动 |
| 本地日志目录 | `logs/linux/自动化脚本测试输出/...` | 测试结果存储 |

## 触发时机

- 用户要求执行远程测试或分析远程日志
- 用户要求对比不同测试批次的结果
- 用户要求诊断远程环境问题（LLM/Embedding/PG/API 异常）
- 用户要求修复服务参数后重新验证

> 🔴 **每次使用本技能前，必须先向用户确认 SSH 密码**。AutoDL 实例重启后密码会变更，文档中记录的密码（`32X+RIXP5/Vh`）仅为上次会话的快照，不可假定仍然有效。**在未获得用户确认的最新密码前，禁止执行任何 SSH 操作。**

## 适用环境

| 组件 | 地址/配置 |
|------|----------|
| SSH 入口 | **需每次向用户确认**（端口/地址/密码均为临时分配） |
| 认证 | 密码认证 (`root` / **需每次向用户确认**，禁止假定旧密码有效) |
| SSH 工具 | `ssh-runner/bin/Release/net8.0/SshRunner.exe` (C# / Renci.SshNet) |
| **SSH 企业级特性** | KeepAlive 15s 心跳防断连 + CommandTimeout 30s 快速失败 + 连接重试 3次 (指数退避 2s/4s/8s) |
| **流式模式** | **`SshRunner --stream`** — 创建伪终端，本地实时看远程输出（**跑全量测试首选**） |
| **监控看板** | **`scripts/monitor-test.ps1`** — 单次 SSH 读取 status.json，实时进度条刷新 (10s轮询) |
| **状态跟踪** | **`scripts/test-status.sh`** — 集成于 auto_test_v2.sh，每项测试后写入 JSON 状态文件 |
| LLM 服务 | `localhost:8080` (llama.cpp / Qwen3-8B-Q4_K_M) |
| Embedding 服务 | `localhost:8081` (llama.cpp / nomic-embed-text-v1.5, **必须带 `--embeddings`**) |
| PostgreSQL | 本地实例 (端口 5432) |
| 测试脚本 | **`scripts/auto_test_v2.sh`**（v2 新版菜单，适配 June 29 菜单收敛） |
| 诊断脚本 | **`scripts/zh-diag.sh`**（全身体检/一键启动/报错解释） |
| API 启动脚本 | **`scripts/start-api.sh`**（环境变量 + dotnet run） |
| 下载脚本 | **`scripts/download_logs.ps1`**（逐文件下载，需改造为 base64 模式） |
| 项目目录 | `/root/autodl-tmp/agent-system/` |
| 结果目录 | `/root/autodl-tmp/test-results/{timestamp}/` |
| 本地结果目录 | `logs/linux/自动化脚本测试输出/root/autodl-tmp/test-results/{timestamp}_{备注}/` |

---

## 阶段零：代码同步 (CODE SYNC)

> **🔴 铁律 -1：测试前必须确保远程代码是最新版本，否则所有测试结果无意义。**

### 0.1 本地代码推送

```powershell
# 本地：确认当前分支和提交
cd $PROJECT_ROOT
git branch --show-current
git log --oneline -3

# 如果有未提交的变更，先提交再推送
git add -A
git commit -m "chore: 测试前同步"
git push origin feature/partner-dev
```

### 0.2 远程代码拉取与验证

```bash
# (SSH) 拉取最新代码
cd /root/autodl-tmp/agent-system && \
git fetch origin && \
git reset --hard origin/feature/partner-dev && \
echo "Remote Commit: $(git log --oneline -1)"
```

### 0.3 脚本文件确认

```bash
# (SSH) 确认关键脚本存在且为最新
cd /root/autodl-tmp/agent-system/scripts && \
echo "=== SCRIPT CHECK ===" && \
for s in auto_test_v2.sh zh-diag.sh start-api.sh; do
  if [ -f "$s" ]; then
    echo "✅ $s ($(wc -l < $s) lines)"
  else
    echo "❌ $s MISSING — 代码同步不完整!"
  fi
done
```

> 🔴 **若 `auto_test_v2.sh` 不存在，说明远程代码未同步——必须执行 0.2 重新拉取。禁止使用旧版 `auto_test.sh`。**

### 0.4 本地→远程直接推送脚本（备选）

如果 git pull 失败（网络问题），可用 base64 直传脚本文件：

```powershell
# 本地：编码脚本（使用项目相对路径）
$scriptContent = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content "$PROJECT_ROOT/scripts/auto_test_v2.sh" -Raw)))

# 通过 SSH 写入远程
& $sshExe $host $port root $password "echo '$scriptContent' | base64 -d > /root/autodl-tmp/agent-system/scripts/auto_test_v2.sh && chmod +x /root/autodl-tmp/agent-system/scripts/auto_test_v2.sh"
```

---

## 阶段一：测试前提条件验证 (PRE-FLIGHT)

> **🔴 铁律：测试前必须先验证环境，否则浪费数十分钟等待然后发现基础问题。**

### 1.0 🔴 密码确认 (每次必做)

> **铁律 0：未确认密码前，禁止执行任何 SSH 命令。**

执行测试前，必须先向用户确认当前 SSH 密码。使用确认后的密码替换本文档中所有 `{PASSWORD}` 占位符或旧的记录密码。

### 1.1 🔧 一键全身体检 (zh-diag.sh)

> **优先使用 `scripts/zh-diag.sh check`，覆盖：显存/llama-server进程/端口/模型文件/数据库/磁盘。**

```bash
# (SSH) 全身体检
cd /root/autodl-tmp/agent-system && bash scripts/zh-diag.sh check
```

**输出解读**：
- 带 ✅ 的行 = 正常
- 带 ❌ 的行 = 需要处理（见下方速查表）

| zh-diag 输出 | 含义 | 修复 |
|------|------|------|
| `❌ llama-server 没有运行` | 推理服务未启动 | 执行 `bash scripts/zh-diag.sh start` |
| `❌ 端口 8081 — 挂了` | Embedding 服务异常 | 检查 `--embeddings` 参数 |
| `❌ 模型文件太小 (XGB)` | GGUF 文件损坏 | 重新下载模型 |
| `❌ PostgreSQL 未运行` | 数据库未启动 | `service postgresql start` |
| `❌ 显存不足 4GB` | 无法同时跑双服务 | 给 Embed 去掉 `-ngl` 用 CPU |

### 1.2 🚀 一键启动服务

```bash
# (SSH) 步骤 1：启动 llama.cpp 双服务（LLM:8080 + Embed:8081）
cd /root/autodl-tmp/agent-system && bash scripts/zh-diag.sh start

# (SSH) 步骤 2：启动 PostgreSQL（如未启动）
service postgresql start 2>/dev/null || true

# (SSH) 步骤 3：启动 API 服务
cd /root/autodl-tmp/agent-system && bash scripts/start-api.sh
```

> 💡 `zh-diag.sh start` 内部逻辑：停旧进程 → 模型文件验证（大小/存在性） → 启动 LLM(8080, -c 8192) → 启动 Embed(8081, --embeddings) → 等待20s → 验证健康状态。

### 1.2b ⚠️ 降级方案：手动环境检查与启动（脚本不可用时）

> 当 `zh-diag.sh` 或 `start-api.sh` 不存在时（如新算力机尚未同步代码），使用以下手动命令。

**手动环境变量验证**：

```bash
# (SSH) 一键检查
echo "=== ENV CHECK ===" && \
echo "DB_PASSWORD=${DB_PASSWORD:?NOT SET}" && \
echo "JWT_KEY=${JWT_KEY:?NOT SET}" && \
echo "ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:?NOT SET}" && \
echo "PGHOST=${PGHOST:-localhost}" && \
echo "All ENV OK"
```

| 缺失变量 | 症状 | 修复 |
|---------|------|------|
| `DB_PASSWORD` | PG 连接失败 | `export DB_PASSWORD=7758521` |
| `JWT_KEY` | API 启动失败 | `export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx` |
| `ASPNETCORE_ENVIRONMENT` | 配置不完整 | `export ASPNETCORE_ENVIRONMENT=Production` |

**手动服务健康检查（五合一）**：

```bash
# (SSH)
echo -n "PG: " && pg_isready -q && echo "OK" || echo "DEAD"
echo -n "LLM:8080: " && curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/health && echo ""
echo -n "Embed:8081: " && curl -s -o /dev/null -w '%{http_code}' http://localhost:8081/health && echo ""
echo -n "API:5000: " && curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health && echo ""
```

**手动启动 llama.cpp 双服务**：

```bash
# (SSH) 停旧进程
pkill -f "llama-server" 2>/dev/null; sleep 2

# 启动 LLM 推理服务 (8080)
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/Models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /tmp/llama-llm.log 2>&1 &

# 启动 Embedding 服务 (8081) ← 必须带 --embeddings
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/Models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 \
  > /tmp/llama-embed.log 2>&1 &

# 等待模型加载
echo "等待模型加载 (约20秒)..." && sleep 20
```

**手动启动 PostgreSQL**：

```bash
# (SSH)
service postgresql start 2>/dev/null || pg_ctlcluster 16 main start 2>/dev/null
```

**手动启动 API 服务**：

```bash
# (SSH) 设置环境变量并启动
export ASPNETCORE_URLS="http://0.0.0.0:5000"
export DB_PASSWORD=7758521
export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
export ASPNETCORE_ENVIRONMENT=Production
cd /root/autodl-tmp/agent-system
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &
echo "API PID: $!"
```

### 1.3 服务参数强制校验

> **🔴 6.29 测试中 7 个 FAIL 的直接根因——服务参数不正确。`zh-diag.sh start` 已内置正确参数，但仍需二次确认。**

```bash
# 检查 llama.cpp 进程参数 (SSH)
ps aux | grep llama-server | grep -v grep
```

| 服务 | 必须存在的参数 | 必须不存在的参数 | 含义 |
|------|:---:|:---:|------|
| Embed :8081 | `--embeddings` | — | 缺失则所有向量检索返回 501 |
| LLM :8080 | `-c 4096` 或 `-c 8192` | `-np` | `-np 4` 并行槽位导致 400 Bad Request |

### 1.4 API 健康验证

```bash
# (SSH) 确认 API 启动成功
curl -s http://localhost:5000/health | python3 -m json.tool 2>/dev/null || curl -s http://localhost:5000/health
```

**期望输出**：`"status":"healthy"`，且 `knowledge_base_docs` > 0。

---

## 阶段二：全量测试执行

> 🔴 **强制使用 `scripts/auto_test_v2.sh`（新版菜单适配 June 29）。禁止使用旧版 `auto_test.sh`（菜单号不匹配会导致大量超时/误判）。**

### 2.1 启动测试（流式模式 ⭐ 推荐 — 本地实时看进度）

```powershell
# 本地 PowerShell — 前台跑测试，实时逐行回传输出（无需 nohup！）
$sshExe = "$PROJECT_ROOT\ssh-runner\bin\Release\net8.0\SshRunner.exe"
& $sshExe --stream $host $port root $password "
  export DOTNET_ENVIRONMENT=Production
  export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
  export DB_PASSWORD=7758521
  cd /root/autodl-tmp/agent-system && bash scripts/auto_test_v2.sh
"

# 输出效果 — 等同于在远程终端前直接观看：
# 📁 测试结果目录: /root/autodl-tmp/test-results/20260630_105112
# ⏱️  单测试超时: 180s
# ── Layer 1: CoT 基础推理测试 ──
# [T1   ] CoT标准推理                    ✅ 通过 (12s)
# [T2   ] CoT流式推理                    ✅ 通过 (15s)
# ...
```

> **原理**：`--stream` 模式使用 SSH `CreateShellStream` 创建 xterm-256color 伪终端，命令前台执行，输出逐行实时回传。哨兵机制检测 `__SSHRUNNER_EXIT_<timestamp>__:` 退出码标记。
>
> **Ctrl+C 可以直接中断**（信号通过伪终端传递给远程进程）。

### 2.1b 推荐：nohup + 监控看板模式（⭐ 最稳定 — SSH 超时免疫）

> **架构优势**：测试进程与 SSH 监控通道完全解耦，即使 SSH 断开也不影响测试执行。只需 1 次 SSH 连接读取一个小 JSON 文件，彻底消除 SSH 超时问题。

**步骤 1：远程启动测试**

```bash
# (SSH) setsid 分离进程，SSH 断开后继续跑
cd /root/autodl-tmp/agent-system && \
(setsid bash scripts/auto_test_v2.sh > /root/autodl-tmp/test-nohup.log 2>&1 &)
```

> `auto_test_v2.sh` 已集成 `test-status.sh`，每项测试完成后自动更新 `status.json`（包含当前测试、通过/失败/跳过计数、耗时等）。

**步骤 2：本地启动监控看板**

```powershell
# 本地 PowerShell — 实时进度条 + 最近 8 项结果
$resultDir = "20260701_095938"  # ← 替换为实际目录名
. $PROJECT_ROOT\scripts\monitor-test.ps1 `
  -StatusPath "/root/autodl-tmp/test-results/$resultDir/status.json" `
  -Interval 10 `
  -SshPassword "{PASSWORD}"

# 看板效果：
# ╔══════════════════════════════════════════════════╗
# ║   Agent1 远程测试实时监控 | 已监控 3.5min       ║
# ╚══════════════════════════════════════════════════╝
#   状态: 🔄 运行中
#   进度: [████████████░░░░░░░░░░░░░░░░░░░░░░] 30%
#   耗时: 4.2min | 已完成: 12 项
#   ✅ 通过: 11  ❌ 失败: 0  ⚠️ 跳过: 1
#   🔄 当前: [T5   ] Reflection反思
#   ── 最近测试结果 ──
#     ✅ [T4   ] ReAct流式推理                 9s
#     ✅ [T3   ] ReAct标准推理                  9s
#     ...
```

> **关键设计**：`monitor-test.ps1` 每次轮询只做 1 次 SSH 调用读取 `status.json`（<1KB），不再多次执行 `ps/ls/wc/tail/cat`。KeepAlive 保持 SSH 连接存活，CommandTimeout 30s 防止单次调用阻塞。

### 2.1c 传统 nohup（无状态文件时使用）

```bash
# 必须使用 nohup，防止 SSH 断开后脚本被 kill (SSH)
cd /root/autodl-tmp/agent-system && \
nohup bash scripts/auto_test_v2.sh > /tmp/auto_test_nohup.log 2>&1 &
echo "Test PID: $!"
```

> 后台模式无法实时看进度，建议配合 `scripts/watch-remote-test.ps1` 轮询监控。

### 2.1d 脚本版本确认

```bash
# (SSH) 必须确认使用 v2 脚本
head -3 /root/autodl-tmp/agent-system/scripts/auto_test_v2.sh
# 期望输出：包含 "v2 (适配 June 29 菜单收敛)"
```

### 2.2 启动测试（推荐 2.1b 方案）

> ⚠️ 以下旧式 nohup 方案仍可用，但推荐使用 2.1b 的 `setsid` + `monitor-test.ps1` 组合（进度可视化 + SSH 超时免疫）。

```bash
# 旧式 nohup (SSH)
cd /root/autodl-tmp/agent-system && \
nohup bash scripts/auto_test_v2.sh > /tmp/auto_test_nohup.log 2>&1 &
echo "Test PID: $!"
```

### 2.3 测试进度监控（推荐 2.1b 的 `monitor-test.ps1`）

> ⚠️ 以下旧式轮询方案（多次 SSH 调用 `tail/ls/wc`）在远程负载高时会超时。使用 2.1b 的 `monitor-test.ps1` 单次 SSH 读取 status.json 可彻底解决。

```bash
# 旧式轮询 (SSH) — 仅当 monitor-test.ps1 不可用时使用
tail -5 /tmp/auto_test_nohup.log

# 或直接查看最新结果文件
ls -lt /root/autodl-tmp/test-results/$(ls -t /root/autodl-tmp/test-results/ | head -1)/*.log 2>/dev/null | head -5
```

### 2.4 预期时间基线

| 测试规模 | 预期耗时 | 备注 |
|---------|:---:|------|
| 25 个功能测试 (全量) | 580-650s | GPU 推理，`-c 4096` |
| T13 合规评测集 (50条) | 120-180s | 需单独调大超时 |

### 2.5 v1 与 v2 脚本菜单差异

> 旧版 `auto_test.sh` 使用直接数字指令（如 `10\n0`），新版 `auto_test_v2.sh` 使用完整菜单导航路径（如 `5\n2\n0\n0`）。菜单结构变化后，v1 脚本的输入序列无法正确导航，导致交互死锁。

| 测试 | v1 (旧菜单/错误) | v2 (新菜单/正确) |
|------|------|------|
| T10 数据库验证 | `10\n0` | `5\n2\n0\n0` (Admin→DB验证→回主→退出) |
| T20 告警邮件 | `20\n0` | `5\n10\n0\n0` (Admin→告警→回主→退出) |
| T1 CoT推理 | `1\n...\nexit\n0` | `5\nc1\n...\nexit\n0\n0` (Admin→CoT→对话→退出→回主→退出) |
| T18 应急响应 | `18\n...\n0` | `3\n...\n0` (主菜单3独立入口) |
| T13 评测集 | `13\n0` | `5\n5\n0\n0` (Admin→评测→回主→退出) |

---

## 阶段三：日志收集与下载

> 🔴 **唯一正确方法：base64 单文件中转。禁止使用 tar 打包（Windows tar 解压中文文件名 → mojibake 乱码）。禁止使用 `WriteAllText` + `cat`（吞 Unix `\n` 换行符）。**

### 3.1 确认结果目录

```bash
# (SSH) 找到最新结果目录
ls -dt /root/autodl-tmp/test-results/*/ | head -1
```

### 3.2 标准下载流程（base64 单文件中转）

**步骤 1：获取文件列表**

```bash
# (SSH) 列出所有日志文件
ls /root/autodl-tmp/test-results/{TIMESTAMP}/
```

**步骤 2：逐文件 base64 下载（PowerShell）**

```powershell
$sshExe = "$PROJECT_ROOT\ssh-runner\bin\Release\net8.0\SshRunner.exe"
$host = "connect.nmb2.seetacloud.com"
$port = 40448              # ← 每次向用户确认
$password = "rs8cVIxBSeUE" # ← 每次向用户确认
$svrDir = "/root/autodl-tmp/test-results/{TIMESTAMP}"
$localDir = "$PROJECT_ROOT\logs\linux\自动化脚本测试输出\root\autodl-tmp\test-results\{TIMESTAMP}_{备注}"

New-Item -ItemType Directory -Path $localDir -Force | Out-Null

# 先获取服务器端文件列表
$fileList = & $sshExe $host $port root $password "ls $svrDir/*.log $svrDir/summary.txt 2>/dev/null"
$files = $fileList -split "`n" | Where-Object { $_ -match '\.(log|txt)$' } | ForEach-Object { ($_ -split '/')[-1].Trim() }

Write-Host "Found $($files.Count) files to download"

foreach ($f in $files) {
    Write-Host "Downloading $f ... " -NoNewline
    try {
        # ✅ 核心：base64 编码传输，保持二进制完整性
        $b64 = & $sshExe $host $port root $password "base64 -w0 $svrDir/$f"
        if ($b64 -and $b64.Trim().Length -gt 10) {
            $bytes = [Convert]::FromBase64String($b64.Trim())
            [System.IO.File]::WriteAllBytes("$localDir\$f", $bytes)
            $lines = ([System.Text.Encoding]::UTF8.GetString($bytes) -split "`n").Count
            Write-Host "OK ($($bytes.Length)b, $lines lines)" -ForegroundColor Green
        } else {
            Write-Host "EMPTY" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "FAIL: $_" -ForegroundColor Red
    }
}

Write-Host "`nDone! Files saved to: $localDir" -ForegroundColor Cyan
```

> ❌ **反模式 1：tar + scp** — Windows tar 无法正确处理中文文件名（GNU tar vs BSD tar 编码差异），解压后变成 mojibake（如 `T5_Reflection反�\200�.log`）。
>
> ❌ **反模式 2：`WriteAllText` + `cat`** — PowerShell 的 `WriteAllText` 自动转换换行符，将 Unix `\n` 转为 Windows `\r\n` 甚至吞掉换行符，导致 450 行的日志被压缩为 1 行。
>
> ✅ **唯一正确：`base64 -w0`（服务器端）+ `FromBase64String` + `WriteAllBytes`（本地）** — 二进制透明，零编码损失。

### 3.3 大文件截断下载（> 500MB）

对于超时产生的大文件（如 T11 BM25 超时 3.15GB），只下载前 N 行用于分析：

```powershell
# 下载大文件的前200行
$f = "T18_应急响应方案.log"
$b64 = & $sshExe $host $port root $password "head -200 $svrDir/$f | base64 -w0"
$bytes = [Convert]::FromBase64String($b64.Trim())
[System.IO.File]::WriteAllBytes("$localDir\$($f -replace '\.log$','_head200.log')", $bytes)
```

### 3.4 完整性验证清单

```powershell
$files = Get-ChildItem "$localDir\*.log" | Where-Object { $_.Name -notlike "*_head*" }

Write-Host "=== INTEGRITY CHECK ==="
Write-Host "1. .log files: $($files.Count)"
Write-Host "2. summary.txt: $(Test-Path '$localDir\summary.txt')"

# 3. 换行符完整性（最关键！）
$badFiles = @()
$files | ForEach-Object {
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName))
    $lines = ($text -split "`n").Count
    if ($lines -le 1) { $badFiles += $_.Name }
}
if ($badFiles.Count -gt 0) {
    Write-Host "3. ❌ 换行符损坏 ($($badFiles.Count) 个): $badFiles ← 需用 base64 重新下载!"
} else {
    Write-Host "3. ✅ 所有文件换行符正常"
}

# 4. 小型损坏文件
$corrupt = $files | Where-Object { $_.Length -lt 10 }
Write-Host "4. 无<10B损坏文件: $($corrupt.Count -eq 0)"

# 5. 清理临时文件
Remove-Item "$localDir\*.b64", "$localDir\*.tar.gz" -Force -ErrorAction SilentlyContinue
```

---

## 阶段四：六维度日志分析框架

> **遵循《Task 11 测试日志逐行深度解析》标准结构——每条日志按 6 个维度逐行展开。**

### 4.1 六维度定义速查表

| 维度 | 核心问题 | 关键输出 |
|:---:|------|------|
| **D1 格式解析** | 这段日志在说什么？ | 字段拆解表 (时间戳/级别/TraceId/模块标签/结构化字段) |
| **D2 业务映射** | 对应哪段代码逻辑？ | 代码路径 `文件:行号` + 代码片段 |
| **D3 数据链路** | 数据从哪来、到哪去？ | TraceId 链路图 (含时序) |
| **D4 设计意图** | 为什么这样设计？ | 设计决策说明 (含降级/容错/性能权衡) |
| **D5 异常识别** | 有没有不对劲？ | 异常分类 (P0/P1/P2) + 影响范围 |
| **D6 性能分析** | 快不快、瓶颈在哪？ | 耗时分解 + 基准对比 |

### 4.2 按日志来源的分析要点

#### 4.2.1 系统启动日志 (每个测试的 1-31 行)

> 📁 来源：每个 `T*.log` 文件开头，所有测试共享相同启动段

| 检查点 | D5 异常信号 | 处置 |
|------|------|------|
| `✅ 数据库连接成功` | 出现 `❌` 则 P0 阻断 | 检查 PG 服务 + DB_PASSWORD |
| `⚠️ 中文配置不可用` | 已知降级，非阻塞 | 可选：安装 zhparser |
| `⚡ 快速模式：从数据库重建内存索引` | 应出现在已有数据时 | 首次运行应走全量模式 |
| `知识库文档数: 77` | < 50 则知识库不完整 | 检查 knowledgebase 目录 |
| `FC=AutoInvokeKernelFunctions` | 不应出现 `None` | 检查 SK 配置 |

#### 4.2.2 Pipeline 请求链路日志 (核心分析段)

> 📁 来源：每个 `T*.log` 的中间部分，含 TraceId 的全链路

| 检查点 | D5 异常信号 | D6 性能基准 |
|------|------|:---:|
| `[Pipeline] 开始` → `输入长度=N` | N=0 为异常空输入 | — |
| `[IntentRouter] 关键词 "XX" 命中` | 无命中 → 走 SimpleChat 降级 | < 50ms |
| `✅ 向量嵌入成功 (维度: 768)` | 维度≠768 或返回 501 | < 500ms |
| `[SK诊断] 本轮共调用 N 个工具` | N=0 但意图需要工具 → LLM 绕过 FC | 取决于工具复杂度 |
| `[Pipeline] 完成 \| 总耗时=Xms` | > 30s 为性能异常 | 8-12s (单次推理) |

**D3 数据链路追踪模板**：
```
用户输入 → [IntentRouter] 意图=X → [SK Auto FC] 工具=N 个
  → [CheckHazardCategory] → RAG检索 (BM25+Vector+RRF)
  → [Memory] 缓存写入 → [安全检测] 输出验证
  → [Pipeline] 完成 (总耗时=Xms)
```

#### 4.2.3 测试汇总报告 (summary.txt)

| 字段 | D5 判断逻辑 |
|------|------|
| `失败: N` | **N > 0 → D5 P1**：逐文件 grep "生成错误\|FAIL\|❌" 定位 |
| `跳过: N` | 确认跳过原因 (前置条件不满足 / 超时) |
| `LLM: XXX` | ≠200 → LLM 服务异常 |
| `Embed: XXX` | ≠200 → Embedding 服务异常 |
| `总耗时: Xs` | > 700s → 性能退化预警 |

### 4.3 错误分类与严重等级

| 等级 | 定义 | 典型日志特征 | 响应 |
|:---:|------|------|------|
| **P0** | 系统不可用 | `Connection refused`, `FATAL`, 服务状态码≠200 | 立即修复 |
| **P1** | 功能受损 | `⚠️ 生成错误`, `FAIL`, `工具调用=0` (预期>0) | 本次测试周期内修复 |
| **P2** | 降级/警告 | `⚠️ 中文配置不可用`, `⏭️ 向量生成失败，跳过` | 记录，下一周期修复 |
| **P3** | 信息/优化 | `SKIP` (前置条件不满足), `⚡ 快速模式` | 无需处理 |

---

## 阶段五：常见问题快速诊断手册

### 5.1 症状→根因速查表

| 症状 | 最可能根因 | 验证命令 | 修复 |
|------|------|------|------|
| **大量测试超时（>3个）** | 使用旧版 `auto_test.sh` 而非 v2 | `head -3 scripts/auto_test_v2.sh` | 执行阶段零同步代码 |
| `Connection refused (localhost:11434)` | SK 端点配置指向 Ollama 而非 llama.cpp | `grep "11434\|8080" appsettings.json` | 改端点为 8080 |
| `向量请求失败 [NotImplemented]` (501) | Embed 服务缺 `--embeddings` | `bash scripts/zh-diag.sh check` | `bash scripts/zh-diag.sh start` |
| `Status: 400 (Bad Request)` | LLM 服务 `-np 4` 并行冲突 | `ps aux \| grep llama-server.*8080` | `bash scripts/zh-diag.sh start` |
| `生成错误: Service request failed` | LLM 服务异常/过载 | `curl localhost:8080/health` | 检查 LLM 服务状态 |
| `input length exceeds context length` | 文本块超过嵌入模型窗口 | `grep "exceeds" *.log` | 调小分块或加大 `-c` |
| `NpgsqlException` / 数据库连接失败 | PG 未启动或密码错误 | `bash scripts/zh-diag.sh check` | 启动 PG / 修正 DB_PASSWORD |
| T13 合规评测集超时 | 50 条评测超 180s 限制 | `wc -l T13*.log` | 调大超时或分批跑 |
| **日志文件只有 1 行**（应为多行） | `WriteAllText` 吞掉 Unix `\n` 换行符 | `(Get-Content xxx.log).Count` 返回 1 | 用 `base64 -w0` + `FromBase64String` + `WriteAllBytes` 重传 |
| 多测试进入交互死循环 | 脚本菜单导航路径与代码不匹配 | 对比 v1/v2 差异（见 2.5） | 使用 `auto_test_v2.sh` |

### 5.2 服务启动问题诊断流程

```
服务不可用
  ├─ 首选：bash scripts/zh-diag.sh check（全身体检）
  ├─ 次选：bash scripts/zh-diag.sh start（一键重启双服务）
  └─ 手动排查（仅 zh-diag.sh 不可用时）：
      ├─ curl 返回空/错误 → 进程未启动
      │   └─ 修复: 按 1.2 节命令重启
      ├─ curl 返回 200 但功能异常 → 参数错误
      │   └─ 修复: kill + 修正参数重启
      └─ curl 返回 503 → 模型加载中
          └─ 等待 30s 后重试
```

---

## 阶段六：测试结果对比分析模板

### 6.1 跨批次对比表

```markdown
| 测试项 | {批次1} | {批次2} | 变化 |
|--------|:---:|:---:|:---:|
| T1 CoT标准推理 | PASS/FAIL | PASS/FAIL | → / ↑ / ↓ |
| T2 CoT流式推理 | ... | ... | ... |
| ... | ... | ... | ... |
| **总计** | X PASS / Y FAIL / Z SKIP | X PASS / Y FAIL / Z SKIP | |
| **总耗时** | Xs | Ys | |
| **LLM状态** | XXX | XXX | |
| **Embed状态** | XXX | XXX | |
```

### 6.2 回归验证清单

当修复问题后重新测试，必须对比以下维度：

- [ ] 之前 FAIL 的测试 → 必须全部变 PASS
- [ ] 之前 PASS 的测试 → 不应退化（无新增 FAIL）
- [ ] 总耗时 → 应在基线 ±20% 范围内
- [ ] LLM/Embed 服务状态 → 均为 200
- [ ] summary.txt 中 `失败: 0`

---

## 阶段七：命令与脚本速查

### 7.1 SSH 命令模板（无硬编码凭证）

```powershell
# 本地 PowerShell 中执行远程命令的标准模板
$sshExe = "$PROJECT_ROOT\ssh-runner\bin\Release\net8.0\SshRunner.exe"
$host   = "connect.nmb2.seetacloud.com"   # ← 每次向用户确认
$port   = 40448                            # ← 每次向用户确认
$pass   = "rs8cVIxBSeUE"                   # ← 每次向用户确认
& $sshExe $host $port root $pass "YOUR_COMMAND_HERE"
```

### 7.2 脚本工具速查表

| 脚本 | 用途 | 调用方式 |
|------|------|------|
| `scripts/zh-diag.sh check` | 全身体检（显存/进程/端口/模型/DB/磁盘） | `bash scripts/zh-diag.sh check` |
| `scripts/zh-diag.sh start` | 一键启动 llama.cpp 双服务 | `bash scripts/zh-diag.sh start` |
| `scripts/zh-diag.sh log` | 常见报错中文解释 | `bash scripts/zh-diag.sh log` |
| `scripts/zh-diag.sh fix` | 自动修复（安全操作） | `bash scripts/zh-diag.sh fix` |
| `scripts/start-api.sh` | 启动 API 服务（含环境变量） | `bash scripts/start-api.sh` |
| `scripts/auto_test_v2.sh` | **全量测试（v2 新菜单 + 状态跟踪）** | `setsid bash scripts/auto_test_v2.sh > ... &` |
| `scripts/test-status.sh` | 测试状态 JSON 更新器（被 auto_test_v2.sh 调用） | (自动调用) |
| `scripts/monitor-test.ps1` | **本地实时监控看板**（单次 SSH 读 status.json） | `powershell -File scripts/monitor-test.ps1 -StatusPath ...` |
| `scripts/download_logs.ps1` | 批量下载日志（base64 传输） | `powershell -File scripts/download_logs.ps1` |
| `scripts/docker-up.sh` | 一键容器化部署 | `bash scripts/docker-up.sh` |

### 7.3 常用 grep 分析命令

```bash
# 统计各测试 PASS/FAIL/SKIP (SSH)
for f in /root/autodl-tmp/test-results/{TIMESTAMP}/*.log; do
  name=$(basename "$f" .log)
  if grep -q "生成错误\|FAIL" "$f" 2>/dev/null; then
    echo "FAIL: $name"
  else
    echo "PASS: $name"
  fi
done

# 统计"生成错误"在各日志中的出现次数
grep -c "生成错误" /root/autodl-tmp/test-results/{TIMESTAMP}/*.log

# 提取所有 Pipeline 耗时
grep "\[Pipeline\] 完成" /root/autodl-tmp/test-results/{TIMESTAMP}/*.log | grep -oP '总耗时=\K\d+'

# 检查 Embedding 服务是否正常
grep "向量嵌入成功\|向量请求失败" /root/autodl-tmp/test-results/{TIMESTAMP}/*.log | sort
```

### 7.3 本地 PowerShell 分析命令

```powershell
# 统计 summary.txt 内容
Get-Content "$localDir\summary.txt" | Select-String "通过|失败|跳过|总计|总耗时"

# 批量检查所有日志中的异常信号
Get-ChildItem "$localDir\*.log" | ForEach-Object {
    $errors = Get-Content $_.FullName | Select-String "生成错误|FAIL|❌|Connection refused|400|500|501"
    if ($errors) { Write-Host "=== $($_.Name) ==="; $errors | ForEach-Object { Write-Host "  $_" } }
}

# 验证文件完整性
$files = Get-ChildItem "$localDir\*.log"
Write-Host "Total: $($files.Count) files, $([math]::Round(($files | Measure-Object Length -Sum).Sum/1KB, 1)) KB"
$files | Where-Object { $_.Length -lt 100 } | ForEach-Object { Write-Host "CORRUPT: $($_.Name) ($($_.Length) bytes)" }
```

---

## 质量保证清单

每个测试周期结束后必须执行以下检查：

### 前置检查 (PRE-FLIGHT)

- [ ] **代码已同步**：远程 commit = 本地最新 commit（阶段零）
- [ ] **脚本版本正确**：使用 `auto_test_v2.sh`（非旧版 auto_test.sh）
- [ ] **zh-diag.sh check 全绿**：显存/进程/端口/模型/DB 均 ✅
- [ ] **服务参数正确**：Embed 有 `--embeddings`，LLM 无 `-np`

### 完整性检查

- [ ] 所有 `.log` 文件均已通过 base64 单文件中转下载
- [ ] **换行符完整性**：所有 `.log` 文件均为多行（非单行），已验证无 `WriteAllText` 损坏
- [ ] `summary.txt` 内容完整（通过/失败/跳过/总计/LLM/Embed）
- [ ] 临时文件已清理（无需 b64/tar.gz，base64 中转不产生临时文件）

### 准确性检查

- [ ] summary.txt 中 `失败: N` 与实际 grep "生成错误" 结果一致
- [ ] 所有 `FAIL` 状态均有对应的异常日志证据
- [ ] `LLM: 200` 和 `Embed: 200` 与各日志中实际 HTTP 状态一致

### 对比检查 (如有历史批次)

- [ ] 已与上次测试结果对比，标注变化项
- [ ] 新增 FAIL 已分析根因
- [ ] 性能退化 (>20% 耗时增加) 已记录

### 输出规范

- [ ] 分析结果按模块化格式输出（每个测试独立说明）
- [ ] 异常信号按 P0/P1/P2 分级标注
- [ ] 所有日志引用均包含文件路径和行号
- [ ] D5/D6 维度结论有数据支撑

---

## 附录 A：日志文件路径对照表

| 日志类型 | 远程路径 | 本地路径 |
|------|------|------|
| 功能测试日志 | `/root/autodl-tmp/test-results/{TIMESTAMP}/T*.log` | `logs/linux/自动化脚本测试输出/root/autodl-tmp/test-results/{TIMESTAMP}_{备注}/` |
| 测试汇总报告 | `/root/autodl-tmp/test-results/{TIMESTAMP}/summary.txt` | 同上 |
| llama.cpp LLM 日志 | `/root/autodl-tmp/logs/llama-server.log` (zh-diag.sh) | (按需下载) |
| llama.cpp Embed 日志 | `/root/autodl-tmp/logs/llama-embed.log` (zh-diag.sh) | (按需下载) |
| 自动化脚本 nohup 日志 | `/tmp/auto_test_nohup.log` | (按需下载) |
| API 服务日志 | `/root/autodl-tmp/logs/agent1-api.log` | (按需下载) |

## 附录 B：脚本维护条款

> 🔴 **当算力机环境发生变化（新端口、新模型、新菜单结构）时，必须同步更新以下脚本**：

| 变化类型 | 需更新的脚本 | 更新内容 |
|------|------|------|
| 菜单结构调整 | `scripts/auto_test_v2.sh` | 更新 `run_test` 中的导航输入序列 |
| 模型路径/文件名变化 | `scripts/zh-diag.sh` | 更新 `LLM_MODEL`/`EMB_MODEL` 路径 |
| 服务端口变化 | `scripts/zh-diag.sh`, `scripts/start-api.sh` | 更新端口参数 |
| 新增测试用例 | `scripts/auto_test_v2.sh` | 添加 `run_test` 调用 |
| 环境变量变化 | `scripts/start-api.sh` | 更新 export 语句 |
| SSH 入口变化 | 本文档适用环境表 | 更新端口/地址 |

**更新流程**：本地修改脚本 → git commit & push → 阶段零同步到远程。

---

## 关联文档

- [Task 11 测试日志逐行深度解析](../docs/testing/Task%2011%20测试日志逐行深度解析.md) — 六维度分析标准范式
- [系统日志解读与排障实战指南](../docs/testing/系统日志解读与排障实战指南.md) — 三层日志架构与排障方法论
- [故障排查文档](../docs/troubleshooting/故障排查文档.md) — 17 个已知错误记录
- [auto_test_v2.sh](../scripts/auto_test_v2.sh) — 测试脚本源码
- [SshRunner](../ssh-runner/Program.cs) — SSH 连接工具源码
