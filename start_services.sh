#!/bin/bash
# ============================================================
# Agent1 远程服务一键启动脚本
# 用法: bash start_services.sh
# 启动顺序: PG → llama LLM → llama Embed → .NET API
# ============================================================
set -e

# ── 环境变量 ──
export ASPNETCORE_URLS='http://0.0.0.0:5001'
export DB_PASSWORD=7758521
export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
export ASPNETCORE_ENVIRONMENT=Production
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"},{"Username":"viewer","Password":"7758521","Role":"viewer"}]'

# ── 路径 ──
PROJECT_DIR="$HOME/autodl-tmp/agent-system"
LLAMA_BIN="$HOME/autodl-tmp/llama.cpp/build/bin/llama-server"
MODEL_DIR="$HOME/autodl-tmp/models"
LOG_DIR="$HOME/autodl-tmp/logs"

mkdir -p "$LOG_DIR"
cd "$PROJECT_DIR"

echo "========================================"
echo "  Agent1 四服务启动"
echo "========================================"

# ── 1. PostgreSQL ──
echo "[1/4] PostgreSQL..."
pg_ctlcluster 16 main start 2>/dev/null && echo "  ✅ PG (cluster)" || {
  pg_ctl start -D /var/lib/postgresql/16/main -l /var/log/postgresql/postgresql.log 2>/dev/null && echo "  ✅ PG (pg_ctl)" || echo "  ⚠️ PG 启动失败"
}

# ── 2. llama.cpp LLM ──
echo "[2/4] llama.cpp LLM (8080)..."
pkill -f "llama-server.*8080" 2>/dev/null; sleep 1

if [ ! -f "$LLAMA_BIN" ]; then
  echo "  ❌ llama-server 未找到: $LLAMA_BIN"
  exit 1
fi

LLM_MODEL=$(ls "$MODEL_DIR"/qwen*gguf 2>/dev/null | head -1)
if [ -z "$LLM_MODEL" ]; then
  echo "  ❌ LLM 模型未找到"
  exit 1
fi

nohup "$LLAMA_BIN" -m "$LLM_MODEL" \
  --host 0.0.0.0 --port 8080 -c 32768 -ngl 99 --flash-attn \
  > "$LOG_DIR/llama-server.log" 2>&1 &
echo "  ✅ 已启动 (pid $!)"

# ── 3. llama.cpp Embedding ──
echo "[3/4] llama.cpp Embedding (8081)..."
pkill -f "llama-server.*8081" 2>/dev/null; sleep 1

EMBED_MODEL=$(ls "$MODEL_DIR"/bge*gguf 2>/dev/null | head -1)
if [ -z "$EMBED_MODEL" ]; then
  echo "  ⚠️ Embedding 模型未找到，跳过"
else
  nohup "$LLAMA_BIN" -m "$EMBED_MODEL" \
    --host 0.0.0.0 --port 8081 --embedding -c 8192 -ngl 99 \
    > "$LOG_DIR/llama-embed.log" 2>&1 &
  echo "  ✅ 已启动 (pid $!)"
fi

# ── 4. 等 LLM 模型加载 ──
echo "[4/4] 等待 LLM 模型加载 (30s)..."
for i in $(seq 30 -1 1); do
  if curl -s http://localhost:8080/health > /dev/null 2>&1; then
    echo "  ✅ LLM 就绪 (提前 ${i}s)"
    break
  fi
  sleep 1
done

# ── 5. .NET API ──
echo ""
echo "--- 启动 .NET API ---"
pkill -f "dotnet.*Agent1.Api" 2>/dev/null; sleep 2

dotnet build Agent1.Api/Agent1.Api.csproj -c Release --nologo -v q
nohup dotnet run --project Agent1.Api --configuration Release \
  > "$LOG_DIR/api-e2e.log" 2>&1 &

sleep 8

# ── 最终验证 ──
echo ""
echo "========================================"
echo "  健康检查"
echo "========================================"

check() {
  local name=$1 url=$2
  printf "  %-20s " "$name"
  if curl -s --max-time 5 "$url" > /dev/null 2>&1; then
    echo "✅"
  else
    echo "❌"
  fi
}

check "PostgreSQL"        "http://localhost:5432"
check "LLM (8080)"        "http://localhost:8080/health"
check "Embed (8081)"      "http://localhost:8081/health"
check ".NET API (5001)"   "http://localhost:5001/health"

echo ""
echo "========================================"
echo "  启动完成"
echo "========================================"
curl -s http://localhost:5001/health | python3 -m json.tool 2>/dev/null || echo "  ⚠️ API 未响应，查看日志: tail -20 $LOG_DIR/api-e2e.log"
