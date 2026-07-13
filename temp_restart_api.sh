#!/bin/bash
# ==========================================
# Agent1 API Restart Script
# ==========================================
cd /root/autodl-tmp/agent-system

# Kill old processes
fuser -k 5000/tcp 2>/dev/null || true
fuser -k 5001/tcp 2>/dev/null || true
sleep 2

# Kill any zombie dotnet processes
pkill -f "dotnet run.*Agent1.Api" 2>/dev/null || true
pkill -f "Agent1.Api.dll" 2>/dev/null || true
sleep 1

# Set ALL environment variables
export ASPNETCORE_URLS="http://0.0.0.0:5001"
export ASPNETCORE_ENVIRONMENT="Production"
export DB_PASSWORD="7758521"
export JWT_KEY="qazwsxedcrfvtgbyhnujmikolpqazwsx"
export KNOWLEDGE_BASE_PATH="/root/autodl-tmp/agent-system/knowledgebase"
export MULTIMODAL_ENDPOINT="http://localhost:8082/v1"
export MULTIMODAL_MODEL_ID="llava-llama-3-8b-v1_1-int4.gguf"
export EMBEDDING_ENDPOINT="http://localhost:8081/v1"
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"}]'
export DOTNET_USE_POLLING_FILE_WATCHER="true"

# Verify env
echo "AUTH_ACCOUNTS_JSON=${AUTH_ACCOUNTS_JSON:0:30}..."
echo "DB_PASSWORD=${DB_PASSWORD:0:4}***"
echo "ASPNETCORE_URLS=${ASPNETCORE_URLS}"

# Fix global.json
DOTNET_SDK=$(dotnet --list-sdks | head -1 | awk '{print $1}')
echo "{\"sdk\":{\"version\":\"$DOTNET_SDK\",\"rollForward\":\"latestFeature\"}}" > global.json

# Start API with setsid
setsid dotnet run --project Agent1.Api -c Release --no-launch-profile \
  > /root/autodl-tmp/logs/api-e2e.log 2>&1 &

API_PID=$!
echo "API started: PID=$API_PID"
echo "Waiting 40s for startup..."
sleep 40

# Check child process env
CHILD_PID=$(ps aux | grep 'Agent1.Api.dll' | grep -v grep | awk '{print $2}' | head -1)
echo "Child PID: $CHILD_PID"

# Health check
echo ""
echo "=== Health Check ==="
curl -s --max-time 10 http://localhost:5001/health
echo ""

# Login test
echo ""
echo "=== Login Test ==="
curl -s --max-time 10 -X POST http://localhost:5001/api/Auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}'
echo ""
