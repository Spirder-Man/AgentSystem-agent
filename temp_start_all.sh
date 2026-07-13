#!/bin/bash
# ==========================================
# Agent1 全服务启动脚本 (E2E Testing)
# ==========================================
cd /root/autodl-tmp/agent-system

echo "[$(date)] === 全服务启动 ==="

# 1. 更新 .env（补充多模态 + Embedding 配置）
echo "Step 1: 更新 .env..."
grep -q "MULTIMODAL_ENDPOINT" .env 2>/dev/null || {
  cat >> .env << 'EOF'
# 多模态视觉分析
MULTIMODAL_ENDPOINT=http://localhost:8082/v1
MULTIMODAL_MODEL_ID=llava-llama-3-8b-v1_1-int4.gguf
# Embedding 端点
EMBEDDING_ENDPOINT=http://localhost:8081/v1
EOF
}
grep -q "AUTH_ACCOUNTS_JSON" .env 2>/dev/null || {
  echo "AUTH_ACCOUNTS_JSON='[{\"Username\":\"admin\",\"Password\":\"7758521\",\"Role\":\"admin\"},{\"Username\":\"auditor\",\"Password\":\"7758521\",\"Role\":\"auditor\"}]'" >> .env
}
echo ".env updated"

# 2. 启动 Embedding 服务 (:8081)
echo ""
echo "Step 2: 启动 Embedding 服务 (:8081)..."
pkill -f "llama-server.*8081" 2>/dev/null || true
sleep 1
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 \
  > /root/autodl-tmp/logs/embed-8081.log 2>&1 &
echo "Embed PID: $!"

# 3. 启动 PostgreSQL
echo ""
echo "Step 3: 启动 PostgreSQL..."
service postgresql start 2>/dev/null || pg_ctlcluster 16 main start 2>/dev/null || echo "PG start attempted"
sleep 2
pg_isready -q && echo "PG OK" || echo "PG FAILED (non-blocking)"

# 4. 等待 Embedding 模型加载
echo ""
echo "Step 4: 等待模型加载 (20s)..."
sleep 20

# 验证 Embed
curl -s --max-time 3 http://localhost:8081/health && echo " Embed:8081 OK" || echo " Embed:8081 WARN"

# 5. 重新启动 API (:5001)
echo ""
echo "Step 5: 启动 .NET API (:5001)..."
fuser -k 5000/tcp 2>/dev/null || true
fuser -k 5001/tcp 2>/dev/null || true
sleep 2

# 加载环境变量
set -a
source .env 2>/dev/null || true
set +a

export ASPNETCORE_URLS="http://0.0.0.0:5001"
export ASPNETCORE_ENVIRONMENT=Production
export DB_PASSWORD="${DB_PASSWORD:-7758521}"
export JWT_KEY="${JWT_KEY:-qazwsxedcrfvtgbyhnujmikolpqazwsx}"
export KNOWLEDGE_BASE_PATH=/root/autodl-tmp/agent-system/knowledgebase
export MULTIMODAL_ENDPOINT="${MULTIMODAL_ENDPOINT:-http://localhost:8082/v1}"
export MULTIMODAL_MODEL_ID="${MULTIMODAL_MODEL_ID:-llava-llama-3-8b-v1_1-int4.gguf}"

# 修复 global.json SDK 版本（如不匹配）
DOTNET_SDK=$(dotnet --list-sdks | head -1 | awk '{print $1}')
echo "{\"sdk\":{\"version\":\"$DOTNET_SDK\",\"rollForward\":\"latestFeature\"}}" > global.json

setsid dotnet run --project Agent1.Api -c Release --no-launch-profile \
  > /root/autodl-tmp/logs/api-e2e.log 2>&1 &
echo "API PID: $!"

echo ""
echo "[$(date)] === 启动完成，等待 API 就绪 (30s) ==="
