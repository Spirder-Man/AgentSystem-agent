#!/bin/bash
# ==========================================
# Agent1 远程服务一键启动脚本
# 用途：开机自启 / 手动一键拉起所有服务
# 用法：bash remote-start.sh [all|api|frontend]
#   all      - 启动全部服务（基础设施 + API + 前端）
#   api      - 只启动 API
#   frontend - 只启动前端
# ==========================================
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$HOME/autodl-tmp/agent-system"
LOG_DIR="$HOME/autodl-tmp/logs"
mkdir -p "$LOG_DIR"

# 载入环境变量
if [ -f "$PROJECT_DIR/.env" ]; then
  set -a
  source "$PROJECT_DIR/.env"
  set +a
fi

# 默认值（如果 .env 加载失败）
DB_PASSWORD=${DB_PASSWORD:-12345678}
JWT_KEY=${JWT_KEY:-G01UL9fjNTr5DPBxewoC68cX3zqKaudOW4VygYn7lFRZ2MJQ}
AUTH_ACCOUNTS_JSON=${AUTH_ACCOUNTS_JSON:-'[{"Username":"admin","Role":"admin","Password":"123456789"},{"Username":"auditor","Role":"auditor","Password":"123456789"}]'}
ALERT_ENABLED=${ALERT_ENABLED:-true}
ALERT_RECIPIENT_EMAILS=${ALERT_RECIPIENT_EMAILS:-lcy.050801@qq.com}
ALERT_SMTP_HOST=${ALERT_SMTP_HOST:-smtp.qq.com}
ALERT_SMTP_PORT=${ALERT_SMTP_PORT:-587}
ALERT_SMTP_USER=${ALERT_SMTP_USER:-2732232706@qq.com}
ALERT_SMTP_PASS=${ALERT_SMTP_PASS:-gqczheqvhdviddfb}

# 代理转发地址：前端 Vite → 后端 API（同机，不走 SSH 隧道）
VITE_PROXY_TARGET="http://127.0.0.1:5001"

start_infra() {
  echo "━━━ 启动基础设施服务 ━━━"

  # 1. llama-server 双服务
  echo "[1/3] llama-server (LLM + Embedding)..."
  bash "$HOME/autodl-tmp/zh-diag.sh" start 2>/dev/null || {
    echo "  ⚠️ zh-diag.sh 不存在或失败，跳过"
  }

  # 2. PostgreSQL
  echo "[2/3] PostgreSQL..."
  pg_ctlcluster 16 main start 2>/dev/null || echo "  ⚠️ 可能已运行"

  # 3. 验证
  echo "[3/3] 验证..."
  sleep 2
  pg_isready -q && echo "  ✅ PostgreSQL 已就绪" || echo "  ❌ PostgreSQL 异常"
  curl -sf http://localhost:8080/v1/models >/dev/null 2>&1 && echo "  ✅ LLM 推理已就绪" || echo "  ❌ LLM 推理异常"
  curl -sf http://localhost:8081/v1/models >/dev/null 2>&1 && echo "  ✅ Embedding 已就绪" || echo "  ❌ Embedding 异常"

  echo "━━━ 基础设施启动完成 ━━━"
}

start_api() {
  echo "━━━ 启动 API 服务 ━━━"
  local log_file="$LOG_DIR/api-$(date +%Y%m%d_%H%M%S).log"
  echo "日志文件: $log_file"

  cd "$PROJECT_DIR"
  export ASPNETCORE_URLS="http://0.0.0.0:5001"
  nohup dotnet run --project Agent1.Api --no-launch-profile \
    DB_PASSWORD="$DB_PASSWORD" \
    JWT_KEY="$JWT_KEY" \
    AUTH_ACCOUNTS_JSON="$AUTH_ACCOUNTS_JSON" \
    ALERT_ENABLED="$ALERT_ENABLED" \
    ALERT_RECIPIENT_EMAILS="$ALERT_RECIPIENT_EMAILS" \
    ALERT_SMTP_HOST="$ALERT_SMTP_HOST" \
    ALERT_SMTP_PORT="$ALERT_SMTP_PORT" \
    ALERT_SMTP_USER="$ALERT_SMTP_USER" \
    ALERT_SMTP_PASS="$ALERT_SMTP_PASS" \
    > "$log_file" 2>&1 &
  echo "  ✅ API PID: $!"
}

start_frontend() {
  echo "━━━ 启动前端服务 ━━━"
  local frontend_dir="$PROJECT_DIR/agent1-web"
  local log_file="$LOG_DIR/frontend-$(date +%Y%m%d_%H%M%S).log"

  if [ ! -d "$frontend_dir" ]; then
    echo "  ❌ 前端目录不存在: $frontend_dir"
    return 1
  fi

  # 检查 Node.js
  if ! command -v node &>/dev/null; then
    echo "  ❌ Node.js 未安装，无法启动前端"
    return 1
  fi

  # 安装依赖（首次或缺失时）
  cd "$frontend_dir"
  if [ ! -d "node_modules" ]; then
    echo "  [npm install] 安装依赖..."
    npm install --silent 2>>"$log_file" || {
      echo "  ❌ npm install 失败，详见 $log_file"
      return 1
    }
  fi

  # 启动 Vite（后台，暴露到 0.0.0.0，代理指向本地 API）
  VITE_PROXY_TARGET="$VITE_PROXY_TARGET" nohup npm run dev -- --host 0.0.0.0 \
    > "$log_file" 2>&1 &
  local pid=$!
  echo "  ✅ 前端 PID: $pid"
  echo "  🌐 浏览器访问：http://服务器IP:5173"
  echo "  🔑 登录账号：admin / 123456789"
  echo "日志文件: $log_file"
}

case "${1:-all}" in
  api)
    start_api
    echo "查看实时日志: tail -f $LOG_DIR/api-最新.log"
    ;;
  frontend)
    start_frontend
    echo "查看实时日志: tail -f $LOG_DIR/frontend-最新.log"
    ;;
  all|*)
    start_infra
    sleep 3
    start_api
    sleep 3
    start_frontend
    echo ""
    echo "━━━ 所有服务已启动 ━━━"
    echo "实时日志:"
    echo "  API:      tail -f $LOG_DIR/api-最新.log"
    echo "  前端:     tail -f $LOG_DIR/frontend-最新.log"
    echo "浏览器访问: http://服务器IP:5173"
    echo ""
    ;;
esac
