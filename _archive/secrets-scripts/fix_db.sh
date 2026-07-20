#!/bin/bash
# 1. 设置 postgres 密码
echo "=== 设置数据库密码 ==="
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD 'postgres123';\""
echo "密码设置完成"

# 2. 验证
echo "=== 验证数据库连接 ==="
PGPASSWORD=postgres123 psql -h localhost -U postgres -d chemical_park_ai_agent -c "SELECT 1 as test;" && echo "DB OK"

# 3. 停止旧 API
pkill -9 -f "Agent1.Api" 2>/dev/null || true
sleep 2

# 4. 启动 API
echo "=== 启动 API ==="
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

nohup dotnet run --project Agent1.Api -c Release --no-launch-profile \
    > /root/autodl-tmp/logs/api-e2e.log 2>&1 &

echo "API PID: $!"

sleep 25
echo -n "Health: "
curl -s --max-time 10 http://localhost:5001/health
echo ""
