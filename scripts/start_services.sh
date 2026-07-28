#!/bin/bash
set -e

MODEL_DIR=/root/autodl-tmp/models
LLAMA_SERVER=/root/autodl-tmp/llama.cpp/build/bin/llama-server
PROJECT_DIR=/root/autodl-tmp/agent-system
LOG_DIR=/root/autodl-tmp/logs

mkdir -p $LOG_DIR

# 清理旧进程
pkill -9 -f llama-server 2>/dev/null || true
pkill -9 -f "Agent1.Api" 2>/dev/null || true
sleep 2

# ===== 1. LLM 服务 (Qwen3-8B, 端口 8080) =====
echo "[1/3] 启动 LLM 服务 (Qwen3-8B, 端口 8080)..."
CUDA_VISIBLE_DEVICES=0 nohup $LLAMA_SERVER \
    -m $MODEL_DIR/Qwen_Qwen3-8B-Q4_K_M.gguf \
    --host 0.0.0.0 --port 8080 \
    -ngl 99 -c 4096 \
    > $LOG_DIR/llm-server.log 2>&1 &
LLM_PID=$!
echo "  PID: $LLM_PID"

# ===== 2. Embedding 服务 (nomic-embed, 端口 8081) =====
echo "[2/3] 启动 Embedding 服务 (nomic-embed, 端口 8081)..."
CUDA_VISIBLE_DEVICES=0 nohup $LLAMA_SERVER \
    -m $MODEL_DIR/nomic-embed-text-v1.5.f16.gguf \
    --host 0.0.0.0 --port 8081 \
    -ngl 99 --embeddings \
    > $LOG_DIR/embed-server.log 2>&1 &
EMBED_PID=$!
echo "  PID: $EMBED_PID"

# 等待模型加载
echo "  等待模型加载..."
sleep 30

# ===== 3. .NET API (端口 5001) =====
echo "[3/3] 启动 .NET API (端口 5001)..."

# 加载环境变量
export LLM_ENDPOINT="http://localhost:8080/v1"
export EMBEDDING_ENDPOINT="http://localhost:8081/v1"
export DB_HOST="localhost"
export DB_NAME="chemical_park_ai_agent"
export DB_USER="postgres"
export DB_PASSWORD="postgres123"
export ASPNETCORE_URLS="http://0.0.0.0:5001"
export DOTNET_USE_POLLING_FILE_WATCHER=true
export KNOWLEDGE_BASE_PATH="/root/autodl-tmp/knowledgebase"
export JWT_KEY="Agent1-Production-JWT-Key-2024-Secure"
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"}]'

cd $PROJECT_DIR
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile \
    > $LOG_DIR/api-e2e.log 2>&1 &
API_PID=$!
echo "  PID: $API_PID"

# ===== 健康检查 =====
echo ""
echo "===== 健康检查 ====="
sleep 10

echo -n "LLM (8080): "
curl -s --max-time 5 http://localhost:8080/health 2>/dev/null && echo "" || echo "未就绪"

echo -n "Embedding (8081): "
curl -s --max-time 5 http://localhost:8081/health 2>/dev/null && echo "" || echo "未就绪"

echo -n "API (5001): "
curl -s --max-time 10 http://localhost:5001/health 2>/dev/null || echo "未就绪"

echo ""
echo "===== 启动完成 ====="
echo "LLM PID: $LLM_PID | Embed PID: $EMBED_PID | API PID: $API_PID"
