#!/bin/bash
# ============================================================
# Post-deploy Quality Evaluation — 上线后 LLM 推理质量监控
#
# 定位：Post-deployment（告警式）—— 退化 → 通知，不阻断服务
# 原则：
#   - 只评估"好/不好"，不验证"能/不能"（那是 Pre-deploy 的职责）
#   - 与 Pre-deploy 共享同一 JSON schema，方便 Grafana 统一展示
#   - 关键指标退化超阈值 → 发送告警（文本输出 + 邮件/Webhook）
#
# 用法：bash scripts/post-deploy-eval.sh
#       输出 eval_reports/YYYYMMDD_HHMM/eval.json
#       同时更新 eval_reports/latest.json（供下次对比）
#
# Cron 示例（每日凌晨3点）：
#   0 3 * * * bash /path/to/scripts/post-deploy-eval.sh
# ============================================================

set -euo pipefail

# ── 配置 ──
API_PORT="${API_PORT:-5000}"
ADMIN_USER="${ADMIN_USER:-admin}"
ADMIN_PWD="${ADMIN_PWD:-}"
EVAL_DIR="eval_reports"
REPORT_ALERT_THRESHOLD_CONCLUSION=0.05   # 结论准确率下降 >5% → 告警
REPORT_ALERT_THRESHOLD_HALLUCINATION=0.10 # 幻觉率上升 >10% → 警告
CURRENT_MODEL_VERSION="${MODEL_VERSION:-qwen3-8b-q4_k_m}"

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
RUN_ID="post-deploy-$(date +%Y%m%d-%H%M)"
REPORT_DIR="${EVAL_DIR}/$(date +%Y%m%d_%H%M)"
mkdir -p "$REPORT_DIR"

ALERTS=""
WARNINGS=""

# ── Step 1: 全量评测集跑分（通过 API 触发） ──
run_eval() {
  echo "=== [1/4] Running 64-case compliance eval ==="

  TOKEN=$(curl -s -X POST "http://localhost:${API_PORT}/api/Auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"${ADMIN_USER}\",\"password\":\"${ADMIN_PWD}\"}" | jq -r '.token')

  # 触发评测
  TASK_ID=$(curl -s -X POST "http://localhost:${API_PORT}/api/Eval/run" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' | jq -r '.taskId // empty')

  if [ -z "$TASK_ID" ]; then
    echo "  [FAIL] Could not start eval task"
    return 1
  fi
  echo "  Task ID: $TASK_ID"

  # 轮询等待完成（最多 30 分钟）
  ELAPSED=0
  MAX_WAIT=1800
  while [ $ELAPSED -lt $MAX_WAIT ]; do
    STATUS=$(curl -s "http://localhost:${API_PORT}/api/Eval/status/${TASK_ID}" \
      -H "Authorization: Bearer $TOKEN" | jq -r '.status // "running"')
    if [ "$STATUS" = "completed" ]; then
      echo "  Eval completed in ${ELAPSED}s"
      break
    elif [ "$STATUS" = "failed" ]; then
      echo "  [FAIL] Eval task failed"
      return 1
    fi
    sleep 15
    ELAPSED=$((ELAPSED + 15))
    if [ $((ELAPSED % 60)) -eq 0 ]; then
      echo "  Still running... (${ELAPSED}s / ${MAX_WAIT}s)"
    fi
  done

  # 拉取评测报告
  curl -s "http://localhost:${API_PORT}/api/Eval/status/${TASK_ID}" \
    -H "Authorization: Bearer $TOKEN" | jq '.report' > "${REPORT_DIR}/eval.json"

  echo "  Report saved: ${REPORT_DIR}/eval.json"
}

# ── Step 2: 关键指标对比 ──
compare_baseline() {
  echo ""
  echo "=== [2/4] Comparing with previous baseline ==="

  CURR_FILE="${REPORT_DIR}/eval.json"
  PREV_FILE="${EVAL_DIR}/latest/eval.json"

  if [ ! -f "$PREV_FILE" ]; then
    echo "  No previous baseline — saving current as baseline"
    mkdir -p "${EVAL_DIR}/latest"
    cp "$CURR_FILE" "${EVAL_DIR}/latest/eval.json"
    echo "{}" | jq '{conclusion_accuracy: 0, hallucination_rate: 0}' > "${REPORT_DIR}/comparison.json"
    return 0
  fi

  # 使用 jq 提取指标（后端标准 JSON schema）
  CURR_CONCLUSION=$(jq -r '.conclusionAccuracy // .conclusion_accuracy // 0' "$CURR_FILE" 2>/dev/null || echo "0")
  PREV_CONCLUSION=$(jq -r '.conclusionAccuracy // .conclusion_accuracy // 0' "$PREV_FILE" 2>/dev/null || echo "0")
  CURR_HALLUC=$(jq -r '.hallucinationRate // .hallucination_rate // 0' "$CURR_FILE" 2>/dev/null || echo "0")
  PREV_HALLUC=$(jq -r '.hallucinationRate // .hallucination_rate // 0' "$PREV_FILE" 2>/dev/null || echo "0")

  # 计算退化量
  DROP=$(python3 -c "print(max(0, ${PREV_CONCLUSION} - ${CURR_CONCLUSION}))" 2>/dev/null || echo "0")
  HALLUC_RISE=$(python3 -c "print(max(0, ${CURR_HALLUC} - ${PREV_HALLUC}))" 2>/dev/null || echo "0")

  echo "  Conclusion Accuracy: ${PREV_CONCLUSION} → ${CURR_CONCLUSION}"
  echo "  Hallucination Rate:  ${PREV_HALLUC} → ${CURR_HALLUC}"

  # 告警判断
  ALERT_FLAG=false
  if python3 -c "exit(0 if ${DROP} > ${REPORT_ALERT_THRESHOLD_CONCLUSION} else 1)" 2>/dev/null; then
    ALERTS="${ALERTS}CONCLUSION_ACCURACY_DROP(${DROP}) "
    ALERT_FLAG=true
    echo "  ⚠️  ALERT: Conclusion accuracy dropped by ${DROP} (>${REPORT_ALERT_THRESHOLD_CONCLUSION})"
  fi
  if python3 -c "exit(0 if ${HALLUC_RISE} > ${REPORT_ALERT_THRESHOLD_HALLUCINATION} else 1)" 2>/dev/null; then
    ALERTS="${ALERTS}HALLUCINATION_RISE(${HALLUC_RISE}) "
    ALERT_FLAG=true
    echo "  ⚠️  WARNING: Hallucination rate rose by ${HALLUC_RISE} (>${REPORT_ALERT_THRESHOLD_HALLUCINATION})"
  fi
  if ! $ALERT_FLAG; then
    echo "  ✅ Quality stable"
  fi

  # 输出对比 JSON
  jq -n \
    --argjson prev_conclusion "$PREV_CONCLUSION" \
    --argjson curr_conclusion "$CURR_CONCLUSION" \
    --argjson drop "$DROP" \
    --argjson prev_halluc "$PREV_HALLUC" \
    --argjson curr_halluc "$CURR_HALLUC" \
    --argjson halluc_rise "$HALLUC_RISE" \
    --argjson alert "$ALERT_FLAG" \
    '{
      conclusion_accuracy: {previous: $prev_conclusion, current: $curr_conclusion, drop: $drop},
      hallucination_rate: {previous: $prev_halluc, current: $curr_halluc, rise: $halluc_rise},
      alert: $alert
    }' > "${REPORT_DIR}/comparison.json"

  # 更新 latest baseline
  mkdir -p "${EVAL_DIR}/latest"
  cp "$CURR_FILE" "${EVAL_DIR}/latest/eval.json"
}

# ── Step 3: 幻觉法规专项监控 ──
check_hallucination_detail() {
  echo ""
  echo "=== [3/4] Hallucination detail check ==="

  CURR_FILE="${REPORT_DIR}/eval.json"
  TOTAL=$(jq -r '.total // .cases | length // 0' "$CURR_FILE" 2>/dev/null || echo "64")

  # 统计含幻觉法规的 case 数
  HALLUC_COUNT=$(jq -r '[.cases[]? | select(.hallucinatedRegulations? | length > 0)] | length' "$CURR_FILE" 2>/dev/null || echo "0")
  HALLUC_RATE=$(python3 -c "print(round(${HALLUC_COUNT}/${TOTAL}, 3))" 2>/dev/null || echo "0")

  echo "  Hallucinated cases: ${HALLUC_COUNT}/${TOTAL} (${HALLUC_RATE})"

  # 列出幻觉法规（前 5）
  echo "  Top hallucinated regulations:"
  jq -r '[.cases[]? | select(.hallucinatedRegulations? | length > 0) | .hallucinatedRegulations[]] | group_by(.) | sort_by(-length) | .[:5] | .[] | "    \(.[0]) (×\(length))"' "$CURR_FILE" 2>/dev/null || echo "    (parse error)"

  # 输出详细 JSON
  jq -n \
    --argjson total "$TOTAL" \
    --argjson halluc_count "$HALLUC_COUNT" \
    --argjson halluc_rate "$HALLUC_RATE" \
    '{hallucination_detail: {total_cases: $total, hallucinated_cases: $halluc_count, rate: $halluc_rate}}' \
    > "${REPORT_DIR}/hallucination.json"
}

# ── Step 4: 汇总报告 ──
generate_summary() {
  echo ""
  echo "=== [4/4] Generating summary ==="

  # 合并所有结果
  jq -s 'add' \
    "${REPORT_DIR}/eval.json" \
    "${REPORT_DIR}/comparison.json" \
    "${REPORT_DIR}/hallucination.json" \
    > "${REPORT_DIR}/summary.json" 2>/dev/null || {
      echo "  [WARN] Could not merge JSON — saving eval report directly"
      cp "${REPORT_DIR}/eval.json" "${REPORT_DIR}/summary.json"
    }

  # 注入元数据
  FINAL=$(jq \
    --arg run_id "$RUN_ID" \
    --arg model "$CURRENT_MODEL_VERSION" \
    --arg timestamp "$TIMESTAMP" \
    --arg alerts "$ALERTS" \
    '{run_id: $run_id, model_version: $model, timestamp: $timestamp, alerts: $alerts} + .' \
    "${REPORT_DIR}/summary.json")

  echo "$FINAL" > "${REPORT_DIR}/summary.json"
  echo "  Summary: ${REPORT_DIR}/summary.json"

  # 输出告警摘要
  if [ -n "$ALERTS" ]; then
    echo ""
    echo "⚠️  QUALITY ALERTS: $ALERTS"
    echo "   Report: ${REPORT_DIR}/summary.json"
  else
    echo "  ✅ No quality alerts"
  fi
}

# ── 主流程 ──
main() {
  echo "╔══════════════════════════════════════════╗"
  echo "║  Post-deploy Quality Monitor             ║"
  echo "║  Model: ${CURRENT_MODEL_VERSION}         ║"
  echo "║  Time:  ${TIMESTAMP}                     ║"
  echo "╚══════════════════════════════════════════╝"

  run_eval || { echo "[FATAL] Eval failed — skipping comparison"; exit 1; }
  compare_baseline
  check_hallucination_detail
  generate_summary

  echo ""
  echo "=== Post-deploy evaluation complete ==="
}

main
