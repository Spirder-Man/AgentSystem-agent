# AutoDL 远程服务器 Cron 定时评测配置指南

> **适用场景**：AutoDL 容器重启后 cron 丢失，需重新安装配置
> **更新时间**：2026-07-20

---

## 一、安装 cron

AutoDL 容器默认不带 cron，首次使用需安装：

```bash
apt-get update -qq && apt-get install -y cron
service cron start
service cron status    # 确认 cron is running
```

---

## 二、写入 crontab 配置

用 `cat` 直接写入（避免 `crontab -e` 的编码问题）：

```bash
cat > /tmp/crontab.txt << 'EOF'
PROJECT_DIR=/root/autodl-tmp/agent-system
LOG_DIR=/root/autodl-tmp/agent-system/eval_reports/cron_logs

0 3 * * * mkdir -p /root/autodl-tmp/agent-system/eval_reports/cron_logs && ADMIN_PWD=123456789 bash /root/autodl-tmp/agent-system/scripts/post-deploy-eval.sh >> /root/autodl-tmp/agent-system/eval_reports/cron_logs/post-deploy-$(date +\%Y\%m\%d).log 2>&1 && ADMIN_PWD=123456789 bash /root/autodl-tmp/agent-system/scripts/post-deploy-analyze.sh >> /root/autodl-tmp/agent-system/eval_reports/cron_logs/analyze-$(date +\%Y\%m\%d).log 2>&1
0 * * * * curl -sf http://localhost:5000/health/live -o /dev/null || echo "$(date): FAIL" >> /root/autodl-tmp/agent-system/eval_reports/cron_logs/heartbeat.log
EOF
```

安装并验证：

```bash
crontab /tmp/crontab.txt && crontab -l
```

---

## 三、定时任务说明

| 时间 | 任务 | 输出 |
|------|------|------|
| 每日凌晨 3:00 | ① 64 条合规评测跑分 (Step 1-4) | `cron_logs/post-deploy-YYYYMMDD.log` |
| 每日凌晨 3:00 | ② 日志切片 + 六维度分析 (Step 5-7) | `cron_logs/analyze-YYYYMMDD.log` |
| 每小时整点 | API 心跳检测 | `cron_logs/heartbeat.log` |

评测结果写入 `eval_reports/YYYYMMDD_HHMM/`：
- `summary.json` — 评测指标摘要
- `analysis.md` — 六维度深度分析报告 (D1-D6)
- `log_slices/` — API/llama.cpp 服务日志切片

---

## 四、容器重启后恢复

AutoDL 容器重启后 cron 仍在（已安装至系统），但需手动**拉起 cron 服务**：

```bash
service cron start
```

验证：

```bash
service cron status && crontab -l
```

---

## 五、常用运维命令

```bash
crontab -l              # 查看当前定时任务
crontab -e              # 编辑定时任务
service cron status     # 查看 cron 服务状态
service cron restart    # 重启 cron 服务

# 查看最近评测结果
ls -lt /root/autodl-tmp/agent-system/eval_reports/ | head -5

# 查看心跳日志（确认每小时监控正常）
tail -5 /root/autodl-tmp/agent-system/eval_reports/cron_logs/heartbeat.log

# 手动触发完整评测 + 分析（不等待凌晨3点）
ADMIN_PWD=123456789 bash /root/autodl-tmp/agent-system/scripts/post-deploy-eval.sh && ADMIN_PWD=123456789 bash /root/autodl-tmp/agent-system/scripts/post-deploy-analyze.sh

# 手动触发仅分析（已有评测结果时）
bash /root/autodl-tmp/agent-system/scripts/post-deploy-analyze.sh eval_reports/20260721_1450
```

---

## 六、关联文档

| 文档 | 位置 |
|------|------|
| 测试总纲 | `docs/testing/测试总纲.md` |
| crontab 示例 | `scripts/crontab.example` |
| 评测脚本 | `scripts/post-deploy-eval.sh` |
| 分析脚本 | `scripts/post-deploy-analyze.sh` |
| 本地下载 | `scripts/download-analysis.ps1` |
| 部署前检查 | `scripts/pre-deploy-check.sh` |
