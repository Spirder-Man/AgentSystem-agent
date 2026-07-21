#!/bin/bash
# ============================================================
# push-eval-metrics.sh — 将 post-deploy-eval.sh 产出推送到 Prometheus Pushgateway
#
# 用法：bash scripts/push-eval-metrics.sh <eval_summary.json>
# 示例：bash scripts/push-eval-metrics.sh eval_reports/20260720_0300/summary.json
#
# 前置条件：
#   - Prometheus Pushgateway 运行在 localhost:9091
#   - 输入文件为 post-deploy-eval.sh 生成的 summary.json
#
# 推送到 Grafana "质量趋势看板" 展示的指标：
#   agent1_eval_conclusion_accuracy  — 结论准确率
#   agent1_eval_hallucination_rate   — 幻觉率
#   agent1_eval_tool_call_rate       — 工具触发率
#   agent1_predeploy_check_result    — Pre-deploy 结果
# ============================================================

set -euo pipefail

SUMMARY_FILE="${1:-}"
PUSHGATEWAY="${PUSHGATEWAY_URL:-http://localhost:9091}"
JOB_NAME="agent1-eval"

if [ -z "$SUMMARY_FILE" ] || [ ! -f "$SUMMARY_FILE" ]; then
  echo "用法: $0 <summary.json>"
  echo "错误: 文件不存在: ${SUMMARY_FILE}"
  exit 1
fi

echo "=== 推送评测指标到 Prometheus Pushgateway ==="

# 提取指标
CONCLUSION=$(jq -r '.conclusionAccuracy // .conclusion_accuracy // 0' "$SUMMARY_FILE")
HALLUC=$(jq -r '.hallucinationRate // .hallucination_rate // 0' "$SUMMARY_FILE")
TOOL=$(jq -r '.toolCallRate // .tool_call_rate // 0' "$SUMMARY_FILE")
MODEL=$(jq -r '.model_version // "unknown"' "$SUMMARY_FILE")
RUN_ID=$(jq -r '.run_id // "unknown"' "$SUMMARY_FILE")
TIMESTAMP=$(date +%s)

# 构建 Prometheus text 格式
cat <<EOF | curl -s --data-binary @- "${PUSHGATEWAY}/metrics/job/${JOB_NAME}/instance/agent1"
# HELP agent1_eval_conclusion_accuracy 合规评测结论准确率 (0-1)
# TYPE agent1_eval_conclusion_accuracy gauge
agent1_eval_conclusion_accuracy{model="${MODEL}",run="${RUN_ID}"} ${CONCLUSION} ${TIMESTAMP}

# HELP agent1_eval_hallucination_rate 幻觉法规引用比例 (0-1)
# TYPE agent1_eval_hallucination_rate gauge
agent1_eval_hallucination_rate{model="${MODEL}",run="${RUN_ID}"} ${HALLUC} ${TIMESTAMP}

# HELP agent1_eval_tool_call_rate LLM 工具触发率 (0-1)
# TYPE agent1_eval_tool_call_rate gauge
agent1_eval_tool_call_rate{model="${MODEL}",run="${RUN_ID}"} ${TOOL} ${TIMESTAMP}

# HELP agent1_eval_run_total 评测运行计数
# TYPE agent1_eval_run_total counter
agent1_eval_run_total{model="${MODEL}"} 1 ${TIMESTAMP}
EOF

echo ""
echo "✅ 指标已推送到 ${PUSHGATEWAY}"
echo "   conclusion=${CONCLUSION} hallucination=${HALLUC} tool_call=${TOOL}"
