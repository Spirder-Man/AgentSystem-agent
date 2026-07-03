# 远程日志分析操作速查手册

> 📖 **完整方法论**（怎么想 + 怎么做 + 怎么问）见 [engineering-deep-learning-methodology.md](file:///d:/桌面/agent/项目/Agent1/.agents/skills/engineering-deep-learning-methodology.md)。本文档是纯操作速查，不含思考框架。

## 技能描述

远程 Linux 服务器自动化测试执行、日志收集与问题诊断的标准化操作流程速查手册。

## 项目根目录约定

```powershell
$PROJECT_ROOT = Resolve-Path "$PSScriptRoot\..\.."
```

| 路径变量 | 相对路径 | 说明 |
|------|------|------|
| SSH 工具 | `ssh-runner/bin/Release/net8.0/SshRunner.exe` | C# SSH 客户端 |
| 测试脚本 | `scripts/auto_test_v2.sh` | 全量测试 v2 |
| 诊断脚本 | `scripts/zh-diag.sh` | 环境诊断与启动 |
| 本地日志目录 | `logs/linux/自动化脚本测试输出/...` | 测试结果存储 |

> 🔴 **每次使用前必须先向用户确认 SSH 密码。** 禁止假定旧密码有效。

## 适用环境

| 组件 | 地址/配置 |
|------|----------|
| SSH 入口 | **需每次向用户确认** |
| 认证 | `root` / **需每次向用户确认** |
| LLM 服务 | `localhost:8080` (llama.cpp / Qwen3-8B-Q4_K_M) |
| Embedding 服务 | `localhost:8081` (llama.cpp / nomic-embed-text-v1.5, 必须带 `--embeddings`) |
| PostgreSQL | 本地实例 (端口 5432) |
| 项目目录 | `/root/autodl-tmp/agent-system/` |
| 结果目录 | `/root/autodl-tmp/test-results/{timestamp}/` |

---

## 阶段零：代码同步

```powershell
# 本地
git push origin feature/partner-dev
```

```bash
# (SSH) 远程拉取
cd /root/autodl-tmp/agent-system && \
git fetch origin && \
git reset --hard origin/feature/partner-dev
```

---

## 阶段一：前提条件验证

### 全身体检

```bash
cd /root/autodl-tmp/agent-system && bash scripts/zh-diag.sh check
```

| 输出 | 修复 |
|------|------|
| `❌ llama-server 没有运行` | `bash scripts/zh-diag.sh start` |
| `❌ 端口 8081 — 挂了` | 检查 `--embeddings` 参数 |
| `❌ PostgreSQL 未运行` | `service postgresql start` |
| `❌ 显存不足 4GB` | 给 Embed 去掉 `-ngl` 用 CPU |

### 一键启动服务

```bash
# (SSH) 步骤 1：llama.cpp 双服务
bash scripts/zh-diag.sh start

# (SSH) 步骤 2：PostgreSQL
service postgresql start 2>/dev/null || true

# (SSH) 步骤 3：API 服务
bash scripts/start-api.sh
```

### 服务参数强制校验

```bash
ps aux | grep llama-server | grep -v grep
```

| 服务 | 必须存在 | 必须不存在 |
|------|:---:|:---:|
| Embed :8081 | `--embeddings` | — |
| LLM :8080 | `-c 4096+` | `-np` |

### 降级方案（脚本不可用时的手动命令）

<details><summary>手动环境变量验证</summary>

```bash
echo "DB_PASSWORD=${DB_PASSWORD:?NOT SET}" && \
echo "JWT_KEY=${JWT_KEY:?NOT SET}" && \
echo "ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:?NOT SET}"
```

| 缺失变量 | 修复 |
|---------|------|
| `DB_PASSWORD` | `export DB_PASSWORD=7758521` |
| `JWT_KEY` | `export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx` |
| `ASPNETCORE_ENVIRONMENT` | `export ASPNETCORE_ENVIRONMENT=Production` |

</details>

<details><summary>手动启动 llama.cpp 双服务</summary>

```bash
pkill -f "llama-server" 2>/dev/null; sleep 2

nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/Models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /tmp/llama-llm.log 2>&1 &

nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/Models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 \
  > /tmp/llama-embed.log 2>&1 &

echo "等待模型加载 (约20秒)..." && sleep 20
```

</details>

<details><summary>手动启动 API</summary>

```bash
export ASPNETCORE_URLS="http://0.0.0.0:5000"
export DB_PASSWORD=7758521
export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
export ASPNETCORE_ENVIRONMENT=Production
cd /root/autodl-tmp/agent-system
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &
```

</details>

---

## 阶段二：全量测试执行

### 推荐：nohup + 监控看板（SSH 超时免疫）

```bash
# (SSH) 启动测试
cd /root/autodl-tmp/agent-system && \
(setsid bash scripts/auto_test_v2.sh > /root/autodl-tmp/test-nohup.log 2>&1 &)
```

```powershell
# 本地：启动监控看板
. $PROJECT_ROOT\scripts\monitor-test.ps1 `
  -StatusPath "/root/autodl-tmp/test-results/{TIMESTAMP}/status.json" `
  -Interval 10 -SshPassword "{PASSWORD}"
```

> 🔴 必须使用 `auto_test_v2.sh`（非旧版 `auto_test.sh`）。

### 流式模式（备选）

```powershell
& $sshExe --stream $host $port root $password "
  cd /root/autodl-tmp/agent-system && bash scripts/auto_test_v2.sh
"
```

### 预期时间基线

| 测试规模 | 预期耗时 |
|---------|:---:|
| 25 个功能测试 (全量) | 580-650s |
| T13 合规评测集 (50条) | 120-180s |

---

## 阶段三：日志下载

> 🔴 **唯一正确方法：base64 单文件中转。**
> ❌ tar + scp（Windows tar 中文文件名乱码）
> ❌ WriteAllText + cat（吞 Unix `\n` 换行符）

### 标准下载流程

```powershell
$sshExe = "$PROJECT_ROOT\ssh-runner\bin\Release\net8.0\SshRunner.exe"
$svrDir = "/root/autodl-tmp/test-results/{TIMESTAMP}"
$localDir = "$PROJECT_ROOT\logs\linux\自动化脚本测试输出\root\autodl-tmp\test-results\{TIMESTAMP}_{备注}"

New-Item -ItemType Directory -Path $localDir -Force | Out-Null
$fileList = & $sshExe $host $port root $password "ls $svrDir/*.log $svrDir/summary.txt 2>/dev/null"
$files = $fileList -split "`n" | Where-Object { $_ -match '\.(log|txt)$' } | ForEach-Object { ($_ -split '/')[-1].Trim() }

foreach ($f in $files) {
    $b64 = & $sshExe $host $port root $password "base64 -w0 $svrDir/$f"
    $bytes = [Convert]::FromBase64String($b64.Trim())
    [System.IO.File]::WriteAllBytes("$localDir\$f", $bytes)
}
```

### 大文件截断下载（> 500MB）

```powershell
$b64 = & $sshExe $host $port root $password "head -200 $svrDir/$f | base64 -w0"
```

### 完整性验证清单

```powershell
# 换行符完整性（最关键！）
$files | ForEach-Object {
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName))
    $lines = ($text -split "`n").Count
    if ($lines -le 1) { Write-Host "❌ 换行符损坏: $($_.Name)" }
}
```

---

## 阶段四：六维度日志分析框架

> 📖 完整分析方法和思维模型见 [engineering-deep-learning-methodology.md](file:///d:/桌面/agent/项目/Agent1/.agents/skills/engineering-deep-learning-methodology.md) 第二部分。

| 维度 | 核心问题 |
|:---:|------|
| **D1 格式解析** | 这段日志在说什么？ |
| **D2 业务映射** | 对应哪段代码逻辑？ |
| **D3 数据链路** | 数据从哪来、到哪去？ |
| **D4 设计意图** | 为什么这样设计？ |
| **D5 异常识别** | 有没有不对劲？ |
| **D6 性能分析** | 快不快、瓶颈在哪？ |

### 错误等级

| 等级 | 定义 | 典型日志特征 | 响应 |
|:---:|------|------|------|
| **P0** | 系统不可用 | `Connection refused`, `FATAL` | 立即修复 |
| **P1** | 功能受损 | `⚠️ 生成错误`, `FAIL` | 本次周期内修复 |
| **P2** | 降级/警告 | `降级到硬编码字典` | 记录，下一周期 |
| **P3** | 信息 | `SKIP`, `⚡ 快速模式` | 无需处理 |

---

## 阶段五：症状→根因速查表

| 症状 | 最可能根因 | 验证命令 | 修复 |
|------|------|------|------|
| 大量测试超时（>3个） | 旧版 `auto_test.sh` | `head -3 scripts/auto_test_v2.sh` | 阶段零同步代码 |
| `Connection refused (11434)` | SK 配置指向 Ollama | `grep "11434\|8080" appsettings.json` | 改端点为 8080 |
| `向量请求失败 501` | Embed 缺 `--embeddings` | `bash scripts/zh-diag.sh check` | `zh-diag.sh start` |
| `Status: 400` | LLM `-np 4` 并行冲突 | `ps aux \| grep llama.*8080` | `zh-diag.sh start` |
| 日志文件只有 1 行 | `WriteAllText` 吞 `\n` | `(Get-Content xxx.log).Count` | base64 重传 |

---

## 常用命令速查

### SSH 命令模板

```powershell
$sshExe = "$PROJECT_ROOT\ssh-runner\bin\Release\net8.0\SshRunner.exe"
& $sshExe $host $port root $password "YOUR_COMMAND_HERE"
```

### 脚本速查

| 脚本 | 用途 | 调用方式 |
|------|------|------|
| `scripts/zh-diag.sh check` | 全身体检 | `bash scripts/zh-diag.sh check` |
| `scripts/zh-diag.sh start` | 一键启动 llama.cpp 双服务 | `bash scripts/zh-diag.sh start` |
| `scripts/auto_test_v2.sh` | 全量测试 | `setsid bash scripts/auto_test_v2.sh > ... &` |
| `scripts/monitor-test.ps1` | 本地实时监控看板 | `powershell -File scripts/monitor-test.ps1 -StatusPath ...` |

### 常用 grep 分析命令

```bash
# 统计各测试 PASS/FAIL/SKIP
for f in /root/autodl-tmp/test-results/{TIMESTAMP}/*.log; do
  name=$(basename "$f" .log)
  grep -q "生成错误\|FAIL" "$f" 2>/dev/null && echo "FAIL: $name" || echo "PASS: $name"
done

# 提取所有 Pipeline 耗时
grep "\[Pipeline\] 完成" /root/autodl-tmp/test-results/{TIMESTAMP}/*.log | grep -oP '总耗时=\K\d+'
```

### 本地 PowerShell 分析

```powershell
# 批量检查异常信号
Get-ChildItem "$localDir\*.log" | ForEach-Object {
    $errors = Get-Content $_.FullName | Select-String "生成错误|FAIL|❌|Connection refused|400|500|501"
    if ($errors) { Write-Host "=== $($_.Name) ==="; $errors }
}
```

---

## 关联文档

- [工程问题深度拆解与远程日志分析技能](file:///d:/桌面/agent/项目/Agent1/.agents/skills/engineering-deep-learning-methodology.md) — 完整方法论 + 提问驱动模式
- [Task 11 测试日志逐行深度解析](../docs/testing/Task%2011%20测试日志逐行深度解析.md) — 六维度分析标准范式
- [Bug知识库](../docs/project/Bug知识库.md) — 历史 Bug 与系统弱点
