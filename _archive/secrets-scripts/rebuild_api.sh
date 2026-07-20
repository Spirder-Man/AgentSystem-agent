#!/bin/bash
set -e

echo "=== 1. 清理旧构建 ==="
pkill -9 -f "Agent1.Api" 2>/dev/null || true
sleep 2

cd /root/autodl-tmp/agent-system

# 删除 bin/obj 中的旧 appsettings（带硬编码 Key）
find . -path "*/bin/*/appsettings.json" -delete
find . -path "*/bin/*/appsettings.*.json" -delete

echo "=== 2. 重新构建 ==="
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

dotnet build Agent1.Api -c Release --no-restore 2>&1 | tail -3

echo "=== 3. 启动 API ==="
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile --no-build \
    > /root/autodl-tmp/logs/api-e2e.log 2>&1 &
API_PID=$!
echo "API PID: $API_PID"

sleep 30

echo "=== 4. 健康检查 ==="
curl -s http://localhost:5001/health
echo ""

echo "=== 5. 登录测试 ==="
LOGIN=$(curl -s -X POST http://localhost:5001/api/Auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"7758521"}')
echo "Login: ${#LOGIN} chars"

TOKEN=$(echo "$LOGIN" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

echo "=== 6. Token 验证测试 ==="
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/api/eval/status/test123 \
  -H "Authorization: Bearer $TOKEN" --max-time 5)
echo "Protected endpoint: HTTP $CODE"
