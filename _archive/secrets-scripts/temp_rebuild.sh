#!/bin/bash
cd /root/autodl-tmp/agent-system

# Kill old (force kill .NET API process only)
kill -9 $(lsof -t -i:5001) 2>/dev/null || true
pkill -9 -f "Agent1.Api" 2>/dev/null || true
sleep 3

# Build
echo "Building..."
dotnet build Agent1.Api/Agent1.Api.csproj -c Release 2>&1 | grep -E 'Error|Build succeeded|Error\(s\)|Time Elapsed'

if [ ${PIPESTATUS[0]} -ne 0 ]; then
  echo "BUILD FAILED"
  exit 1
fi

# Start
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

echo "Starting..."
setsid dotnet run --project Agent1.Api -c Release --no-launch-profile \
  > /root/autodl-tmp/logs/api-e2e.log 2>&1 &

echo "PID: $!"
echo "Waiting 35s..."
sleep 35
echo "Health: $(curl -s --max-time 5 http://localhost:5001/health | grep -o '"status":"[^"]*"')"
echo "Done"
