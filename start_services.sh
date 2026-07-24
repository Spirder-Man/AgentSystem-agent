#!/bin/bash
# ============================================================
# Agent1 远程服务一键启动脚本
# 用法: bash start_services.sh
# 启动顺序: PG → llama LLM → llama Embed → .NET API
# ============================================================

# ── 环境变量 ──
export ASPNETCORE_URLS='http://0.0.0.0:5001'
export DB_PASSWORD=7758521
export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
export ASPNETCORE_ENVIRONMENT=Production
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"},{"Username":"viewer","Password":"7758521","Role":"viewer"}]'

# ── 路径 ──
PROJECT_DIR="$HOME/autodl-tmp/agent-system"
LLAMA_BIN="$HOME/autodl-tmp/llama.cpp/build/bin/llama-server"
MODEL_DIR="$HOME/autodl-tmp/Models"
LOG_DIR="$HOME/autodl-tmp/logs"

mkdir -p "$LOG_DIR"
cd "$PROJECT_DIR"

echo "========================================"
echo "  Agent1 四服务启动"
echo "========================================"

# ── 1. PostgreSQL ──
echo "[1/4] PostgreSQL..."
if pg_isready -q 2>/dev/null; then
  echo "  ✅ 已运行"
else
  pg_ctlcluster 16 main start 2>/dev/null && echo "  ✅ 已启动" || {
    pg_ctl start -D /var/lib/postgresql/16/main -l /var/log/postgresql/postgresql.log 2>/dev/null && echo "  ✅ 已启动" || echo "  ❌ 启动失败"
  }
fi

# ── 2. llama.cpp LLM ──
echo "[2/4] llama.cpp LLM (8080)..."
pkill -f "llama-server.*8080" 2>/dev/null || true
sleep 1

if [ ! -f "$LLAMA_BIN" ]; then
  echo "  ❌ llama-server 未找到: $LLAMA_BIN"
  exit 1
fi

LLM_MODEL=$(ls "$MODEL_DIR"/[Qq]wen*gguf 2>/dev/null | head -1)
if [ -z "$LLM_MODEL" ]; then
  echo "  ❌ LLM 模型未找到 ($MODEL_DIR)"
  exit 1
fi

nohup "$LLAMA_BIN" -m "$LLM_MODEL" \
  --host 0.0.0.0 --port 8080 -c 32768 -ngl 99 --flash-attn on \
  > "$LOG_DIR/llama-server.log" 2>&1 &
echo "  ✅ 已启动 (pid $!) → $(basename "$LLM_MODEL")"

# ── 3. llama.cpp Embedding ──
echo "[3/4] llama.cpp Embedding (8081)..."
pkill -f "llama-server.*8081" 2>/dev/null || true
sleep 1

EMBED_MODEL=$(ls "$MODEL_DIR"/{nomic,bge}*gguf 2>/dev/null | head -1)
if [ -z "$EMBED_MODEL" ]; then
  echo "  ⚠️ Embedding 模型未找到，跳过"
else
  nohup "$LLAMA_BIN" -m "$EMBED_MODEL" \
    --host 0.0.0.0 --port 8081 --embedding -c 8192 -ngl 99 \
    > "$LOG_DIR/llama-embed.log" 2>&1 &
  echo "  ✅ 已启动 (pid $!) → $(basename "$EMBED_MODEL")"
fi

# ── 4. 心跳轮询等 LLM 就绪 ──
echo "[4/4] 等待 LLM 模型加载..."
LOADED=false
for i in $(seq 1 20); do
  if curl -s http://localhost:8080/health > /dev/null 2>&1; then
    echo "  ✅ LLM 就绪 (${i}x3s)"
    LOADED=true
    break
  fi
  sleep 3
done
if [ "$LOADED" = false ]; then
  echo "  ⚠️ LLM 超时未就绪，查看日志: tail -5 $LOG_DIR/llama-server.log"
fi

# ── 5. .NET API ──
echo ""
echo "--- 启动 .NET API ---"
pkill -f "dotnet.*Agent1.Api" 2>/dev/null || true
sleep 2

dotnet build Agent1.Api/Agent1.Api.csproj -c Release --nologo -v q
nohup dotnet run --project Agent1.Api --configuration Release --no-launch-profile \
  > "$LOG_DIR/api-e2e.log" 2>&1 &

for i in $(seq 1 10); do
  if curl -s http://localhost:5001/health > /dev/null 2>&1; then
    echo "  ✅ API 就绪 (${i}x2s)"
    break
  fi
  sleep 2
done

# ── 最终验证 ──
echo ""
echo "========================================"
echo "  健康检查"
echo "========================================"

check() {
  local name=$1 url=$2
  printf "  %-20s " "$name"
  if curl -s --max-time 3 "$url" > /dev/null 2>&1; then
    echo "✅"
  else
    echo "❌"
  fi
}

check "LLM (8080)"        "http://localhost:8080/health"
check "Embed (8081)"      "http://localhost:8081/health"
check ".NET API (5001)"   "http://localhost:5001/health"

echo ""
curl -s http://localhost:5001/health | python3 -m json.tool 2>/dev/null || echo "⚠️ API 未响应 → tail -20 $LOG_DIR/api-e2e.log"
