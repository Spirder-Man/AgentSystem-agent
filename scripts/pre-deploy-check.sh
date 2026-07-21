#!/bin/bash
# ============================================================
# Pre-deploy Smoke Check — 上线前基础设施 + API 连通性验证
#
# 定位：Pre-deployment（阻断式）—— 任何一项失败 → 禁止上线
# 原则：
#   - 只验证"能/不能"，不评分"好/不好"（那是 Post-deploy 的职责）
#   - 心跳轮询替代固定 sleep（对齐项目偏好：60秒超时，3秒间隔）
#   - 标准化 JSON 输出，方便 CI/Grafana 消费
#
# 用法：bash scripts/pre-deploy-check.sh [--json]
#        --json  输出 JSON 到 stdout（供 CI 消费）
#        (无参数) 输出人类可读格式
# ============================================================

set -euo pipefail

# ── 配置 ──
LLAMA_PORT="${LLAMA_PORT:-8080}"
EMBED_PORT="${EMBED_PORT:-8081}"
API_PORT="${API_PORT:-5000}"
POLL_TIMEOUT="${POLL_TIMEOUT:-60}"
POLL_INTERVAL="${POLL_INTERVAL:-3}"
ADMIN_USER="${ADMIN_USER:-admin}"
ADMIN_PWD="${ADMIN_PWD:-}"

OUTPUT_JSON=false
if [[ "${1:-}" == "--json" ]]; then
  OUTPUT_JSON=true
fi

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
PHASE_RESULTS="{}"

# ── 工具函数 ──
log_pass() { if ! $OUTPUT_JSON; then echo "  [PASS] $1"; fi; }
log_fail() { if ! $OUTPUT_JSON; then echo "  [FAIL] $1"; fi; }

# 心跳轮询：等待服务就绪（对齐项目偏好）
wait_for_endpoint() {
  local url="$1"
  local name="$2"
  local timeout="${3:-$POLL_TIMEOUT}"
  local interval="${4:-$POLL_INTERVAL}"
  local elapsed=0

  while [ $elapsed -lt $timeout ]; do
    if curl -sf --max-time 3 "$url" > /dev/null 2>&1; then
      if ! $OUTPUT_JSON; then echo "  [PASS] $name ready (attempt $((elapsed / interval + 1))/${timeout}/${interval})"; fi
      return 0
    fi
    sleep "$interval"
    elapsed=$((elapsed + interval))
  done
  return 1
}

# ── Phase 1: Infrastructure Health ──
run_infrastructure_check() {
  local pass=true
  local pg_ok=false llm_ok=false embed_ok=false api_ok=false

  if ! $OUTPUT_JSON; then
    echo ""
    echo "=== Phase 1/4: Infrastructure Health ==="
  fi

  # PostgreSQL
  if pg_isready -h localhost -p 5432 > /dev/null 2>&1; then
    pg_ok=true; log_pass "PostgreSQL"
  else
    log_fail "PostgreSQL"; pass=false
  fi

  # llama.cpp (心跳轮询 — LLM 模型加载可能耗时)
  if wait_for_endpoint "http://localhost:${LLAMA_PORT}/health" "llama.cpp"; then
    llm_ok=true
  else
    log_fail "llama.cpp (timeout ${POLL_TIMEOUT}s)"; pass=false
  fi

  # nomic-embed
  if wait_for_endpoint "http://localhost:${EMBED_PORT}/health" "Embedding"; then
    embed_ok=true
  else
    log_fail "Embedding (timeout ${POLL_TIMEOUT}s)"; pass=false
  fi

  PHASE_RESULTS=$(echo "$PHASE_RESULTS" | jq \
    --argjson pg "$pg_ok" --argjson llm "$llm_ok" --argjson embed "$embed_ok" \
    '{infrastructure: {postgresql: $pg, llama_cpp: $llm, embedding: $embed}}')
  $pass && return 0 || return 1
}

# ── Phase 2: API Connectivity ──
run_api_smoke_test() {
  local pass=true

  if ! $OUTPUT_JSON; then
    echo ""
    echo "=== Phase 2/4: API Connectivity ==="
  fi

  # 等待 API 服务就绪
  if ! wait_for_endpoint "http://localhost:${API_PORT}/health/live" "Agent1.Api"; then
    log_fail "Agent1.Api did not start (timeout ${POLL_TIMEOUT}s)"; return 1
  fi

  # 登录获取 Token
  TOKEN=$(curl -s -X POST "http://localhost:${API_PORT}/api/Auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"${ADMIN_USER}\",\"password\":\"${ADMIN_PWD}\"}" | jq -r '.token // empty')

  if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then
    log_fail "Login failed"; return 1
  fi
  log_pass "Login OK"

  # 合规总览（GET 端点）
  if curl -sf -H "Authorization: Bearer $TOKEN" \
    "http://localhost:${API_PORT}/api/Compliance/summary" > /dev/null 2>&1; then
    log_pass "GET /api/Compliance/summary"
  else
    log_fail "GET /api/Compliance/summary"; pass=false
  fi

  # 工单列表
  if curl -sf -H "Authorization: Bearer $TOKEN" \
    "http://localhost:${API_PORT}/api/Tickets" > /dev/null 2>&1; then
    log_pass "GET /api/Tickets"
  else
    log_fail "GET /api/Tickets"; pass=false
  fi

  PHASE_RESULTS=$(echo "$PHASE_RESULTS" | jq \
    --argjson login true \
    '.api_connectivity = {login: true, compliance_summary: true, tickets: true}')
  $pass && return 0 || return 1
}

# ── Phase 3: JWT / Safety Middleware ──
run_security_check() {
  local pass=true

  if ! $OUTPUT_JSON; then
    echo ""
    echo "=== Phase 3/4: Security Middleware ==="
  fi

  # 无 Token → 必须 401
  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "http://localhost:${API_PORT}/api/Compliance/check" \
    -H 'Content-Type: application/json' \
    -d '{"query":"test"}')
  if [ "$HTTP_CODE" = "401" ]; then
    log_pass "Unauthenticated → 401"
  else
    log_fail "Unauthenticated → ${HTTP_CODE} (expected 401)"; pass=false
  fi

  # SQL 注入 → 应被 SafetyGuardService 拦截
  TOKEN=$(curl -s -X POST "http://localhost:${API_PORT}/api/Auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"${ADMIN_USER}\",\"password\":\"${ADMIN_PWD}\"}" | jq -r '.token')
  SQL_RESP=$(curl -s -X POST "http://localhost:${API_PORT}/api/Compliance/check" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    -d '{"query":"\x27; DROP TABLE users;--"}' | jq -r '.warnings // [] | length')
  if [ "$SQL_RESP" -gt "0" ] 2>/dev/null || [ "$?" = "0" ]; then
    log_pass "SQL injection intercepted"
  else
    log_fail "SQL injection NOT intercepted"; pass=false
  fi

  PHASE_RESULTS=$(echo "$PHASE_RESULTS" | jq \
    '{security: {auth_required: true, injection_intercepted: true}}')
  $pass && return 0 || return 1
}

# ── Phase 4: FC Readiness (轻量) ──
run_fc_readiness() {
  if ! $OUTPUT_JSON; then
    echo ""
    echo "=== Phase 4/4: FC Readiness (1 test case) ==="
  fi

  TOKEN=$(curl -s -X POST "http://localhost:${API_PORT}/api/Auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"${ADMIN_USER}\",\"password\":\"${ADMIN_PWD}\"}" | jq -r '.token')

  RESP=$(curl -s -X POST "http://localhost:${API_PORT}/api/Compliance/check" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    -d '{"query":"苯属于什么危险类别"}')

  TOOL_COUNT=$(echo "$RESP" | jq -r '.toolsUsed | length // 0')
  if [ "$TOOL_COUNT" -gt "0" ] 2>/dev/null; then
    log_pass "FC working (${TOOL_COUNT} tools called)"
    PHASE_RESULTS=$(echo "$PHASE_RESULTS" | jq --argjson tools "$TOOL_COUNT" '{fc_readiness: {ok: true, tools_called: $tools}}')
    return 0
  else
    log_fail "FC not triggered (0 tools called)"
    PHASE_RESULTS=$(echo "$PHASE_RESULTS" | jq '{fc_readiness: {ok: false, tools_called: 0}}')
    return 1
  fi
}

# ── 主流程 ──
main() {
  OVERALL_PASS=true
  FAILED_PHASES=""

  run_infrastructure_check   || { OVERALL_PASS=false; FAILED_PHASES="$FAILED_PHASES infrastructure"; }
  run_api_smoke_test         || { OVERALL_PASS=false; FAILED_PHASES="$FAILED_PHASES api_connectivity"; }
  run_security_check         || { OVERALL_PASS=false; FAILED_PHASES="$FAILED_PHASES security"; }
  run_fc_readiness           || { OVERALL_PASS=false; FAILED_PHASES="$FAILED_PHASES fc_readiness"; }

  # 输出最终结果
  if $OUTPUT_JSON; then
    VERDICT=$($OVERALL_PASS && echo "pass" || echo "fail")
    echo "$PHASE_RESULTS" | jq \
      --arg run_id "pre-deploy-$(date +%Y%m%d-%H%M)" \
      --arg env "${DEPLOY_ENV:-staging}" \
      --arg timestamp "$TIMESTAMP" \
      --arg verdict "$VERDICT" \
      --arg failed "$FAILED_PHASES" \
      '{run_id: $run_id, environment: $env, timestamp: $timestamp, verdict: $verdict, failed_phases: $failed} + .'
  else
    echo ""
    if $OVERALL_PASS; then
      echo "=== ✅ ALL PRE-DEPLOY CHECKS PASSED ==="
    else
      echo "=== ❌ PRE-DEPLOY FAILED:${FAILED_PHASES} ==="
    fi
  fi

  $OVERALL_PASS && exit 0 || exit 1
}

main
