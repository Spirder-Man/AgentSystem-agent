# 远程 GPU 评测自动化部署

## 前置依赖

远程服务器需安装：
- `bash` `jq` `python3` `curl` — post-deploy-eval.sh 核心依赖
- `crontab` — 定时任务调度

验证命令：
```bash
which jq python3 curl crontab
```

## 配置环境变量

```bash
# 评测 API 密码（必填）
export ADMIN_PWD="your_admin_password"

# 模型版本标识（可选，默认 qwen3-8b-q4_k_m）
export MODEL_VERSION="qwen3-8b-q4_k_m"
```

## 部署步骤

### 1. 确保 API 服务运行

```bash
cd /path/to/Agent1
dotnet run --project Agent1.Api &
# 等待启动后验证
curl http://localhost:5000/health/live
```

### 2. 修改 crontab.example 中的 PROJECT_DIR

编辑 `scripts/crontab.example`，将 `PROJECT_DIR=/root/autodl-tmp/Agent1` 改为实际路径。

### 3. 安装 crontab

```bash
# 方法A: 编辑 crontab
crontab -e
# 然后粘贴 crontab.example 中的定时任务行

# 方法B: 直接导入（如 crontab 当前为空）
crontab scripts/crontab.example
```

## 定时任务说明

| 时间 | 任务 | 输出 |
|------|------|------|
| 每日 03:00 | 全量 64 条合规评测 + 推送 Prometheus | `eval_reports/cron_logs/post-deploy-*.log` |
| 每周一 04:00 | 清理 30 天前的旧报告 | - |
| 每 12 小时 | JSON Schema 验证 | `eval_reports/cron_logs/schema-check.log` |
| 每小时 | 心跳监控 `/health/live` | `eval_reports/cron_logs/heartbeat.log` |

## 查看评测结果

```bash
# 最新评测报告
cat eval_reports/latest/summary.json | jq .

# 关键指标
jq '{conclusion: .conclusion_accuracy, hallucination: .hallucination_rate, tool_call: .tool_call_rate}' eval_reports/latest/summary.json
```

## 告警阈值

- 结论准确率下降 > 5% → `CONCLUSION_ACCURACY_DROP` 告警
- 幻觉率上升 > 10% → `HALLUCINATION_RISE` 警告

告警信息输出到 crontab 日志和 summary.json 中的 `alerts` 字段。
