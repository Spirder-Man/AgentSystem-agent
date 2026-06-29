# Task 11 集成测试 - 部署与执行指南

> **创建日期**: 2026-06-25
> **适用环境**: AutoDL RTX 3090 / Ubuntu 22.04
> **分支**: `linux原生编译模型llama.cpp`

---

## 一、部署前准备

### 1.1 同步最新代码到 Linux 服务器

```bash
cd /root/autodl-tmp/agent-system
git stash
git pull origin linux原生编译模型llama.cpp
git stash pop 2>/dev/null || true
```

### 1.2 上传测试脚本

将以下文件上传到 `/root/autodl-tmp/agent-system/scripts/`：

| 文件 | 说明 |
|------|------|
| `scripts/int-test-task11.sh` | 主集成测试脚本 (API + 评测 + RAG) |
| `scripts/zh-diag.sh` | 环境诊断（已存在，确认最新版本） |

上传方式（任选一种）：
```bash
# 方式1: 如果已 git push，直接 pull
cd /root/autodl-tmp/agent-system
git pull origin linux原生编译模型llama.cpp

# 方式2: scp 上传
scp scripts/int-test-task11.sh root@<服务器IP>:/root/autodl-tmp/agent-system/scripts/

# 方式3: AutoDL 网页上传
# 通过 AutoDL 控制台上传文件
```

---

## 二、执行测试

### 2.1 环境检查

```bash
cd /root/autodl-tmp/agent-system
bash scripts/zh-diag.sh check
```

确认以下服务全部正常：
- PostgreSQL: accepting connections
- LLM 推理 (8080): HTTP 200
- Embedding (8081): HTTP 200

### 2.2 启动 API 服务（如果未运行）

```bash
# 检查 API 是否已运行
curl -s http://localhost:5000/health

# 如未运行，启动 API 服务
cd /root/autodl-tmp/agent-system
nohup dotnet run --project Agent1.Api \
  --environment Production \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &
sleep 10
curl -s http://localhost:5000/health
```

### 2.3 运行集成测试

```bash
cd /root/autodl-tmp/agent-system
bash scripts/int-test-task11.sh
```

**预计耗时**: 
- API 测试: ~2 分钟
- 64 条评测集: 3-15 分钟（GPU/CPU 模式）
- 总计: 约 5-20 分钟

### 2.4 查看结果

```bash
# 结果目录（时间戳自动生成）
ls -la test-results/int-test-*/

# 查看汇总报告
cat test-results/int-test-*/summary.txt

# 查看评测完整输出
cat test-results/int-test-*/C-eval-full.log

# 查看 RAG 召回率报告
cat test-results/int-test-*/D-rag-recall-report.txt

# 查看 API 测试日志
cat test-results/int-test-*/B3-compliance-hazard.log
```

---

## 三、交付物清单

执行完成后，以下文件将生成在 `test-results/int-test-YYYYMMDD_HHMMSS/` 目录：

| 文件 | 内容 |
|------|------|
| `summary.txt` | 总览汇总报告 |
| `A1-postgresql.log` | PostgreSQL 健康检查 |
| `A2-llm-health.log` | LLM 推理服务状态 |
| `A3-embedding-health.log` | Embedding 服务状态 |
| `A4-embedding-dims.log` | Embedding 维度验证 |
| `A5-llm-chat.log` | LLM 功能测试 |
| `A6-api-health.log` | API 服务状态 |
| `B1-auth-login.log` | 登录认证测试 |
| `B2-unauth.log` | 未认证拦截测试 |
| `B3-compliance-hazard.log` | 危险类别 API 测试 |
| `B4-compliance-storage.log` | 储存兼容性 API 测试 |
| `B5-compliance-distance.log` | 安全距离 API 测试 |
| `B6-safety-injection.log` | Prompt 注入拦截测试 |
| `B7-token-blacklist.log` | Token 黑名单测试 |
| `B8-metrics.log` | Prometheus 指标端点 |
| `C-eval-full.log` | 64 条评测集完整输出 |
| `C-eval-metrics.txt` | 评测关键指标提取 |
| `D-rag-recall-report.txt` | RAG 召回率报告 |
| `D2-rag-direct.log` | RAG 直接检索日志 |

---

## 四、验收标准

| 指标 | 目标 | 备注 |
|------|:--:|------|
| API 登录成功率 | 100% | 全部 8 项 API 测试 |
| 评测集执行率 | ≥95% | 64 条至少执行 61 条 |
| 工具触发率 | ≥80% | 预期工具正确调用 |
| RAG Recall@5 | ≥55% | Top-5 检索召回率 |
| RAG Recall@10 | ≥65% | Top-10 检索召回率 |
| 安全注入拦截 | 100% | Prompt 注入返回 400 |
| Token 黑名单 | 通过 | 登出后旧 Token = 401 |
