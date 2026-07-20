#!/bin/bash
pkill -9 -f "Agent1.Api" 2>/dev/null || true
sleep 2

cd /root/autodl-tmp/agent-system

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

nohup dotnet run --project Agent1.Api -c Release --no-launch-profile \
    > /root/autodl-tmp/logs/api-e2e.log 2>&1 &

echo "API PID: $!"

sleep 25
echo -n "Health: "
curl -s --max-time 10 http://localhost:5001/health
echo ""

echo -n "Login: "
curl -s -X POST http://localhost:5001/api/Auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"7758521"}' --max-time 5
echo ""
