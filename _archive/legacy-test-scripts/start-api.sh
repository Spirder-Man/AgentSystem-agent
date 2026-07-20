#!/bin/bash
# ═══════════════════════════════════════════════════════════
# Agent1 API 服务启动脚本
# 用法：bash start-api.sh
# 前提：.env 已配置 + PostgreSQL 已启动 + LLM(8080) + Embed(8081)
# ═══════════════════════════════════════════════════════════

# ── 从脚本位置推导项目根目录 ──
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

# ── 加载 .env 配置（敏感配置唯一源）──
if [ -f "$PROJECT_DIR/.env" ]; then
    set -o noglob
    set -a
    source "$PROJECT_DIR/.env"
    set +a
    set +o noglob
else
    echo "错误：未找到 $PROJECT_DIR/.env 配置文件"
    echo "请确保项目根目录存在 .env 文件（参考 .env.example）"
    exit 1
fi

# ── 强制校验必要环境变量 ──
if [ -z "${JWT_KEY:-}" ] || [ -z "${DB_PASSWORD:-}" ]; then
    echo "错误：JWT_KEY 和 DB_PASSWORD 必须在 .env 中设置"
    exit 1
fi

# ── 可选覆盖（.env 中已设则使用，否则用默认值）──
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5000}"

cd "$PROJECT_DIR"
LOG_DIR="${AGENT1_LOG_DIR:-$PROJECT_DIR/logs}"
mkdir -p "$LOG_DIR"
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile > "$LOG_DIR/agent1-api.log" 2>&1 &
echo "API PID: $!"
