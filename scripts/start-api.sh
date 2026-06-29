#!/bin/bash
export ASPNETCORE_URLS="http://0.0.0.0:5000"
export DB_PASSWORD=7758521
export JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
export ASPNETCORE_ENVIRONMENT=Production
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"}]'

cd /root/autodl-tmp/agent-system
nohup dotnet run --project Agent1.Api -c Release --no-launch-profile > /root/autodl-tmp/logs/agent1-api-run3.log 2>&1 &
echo "API PID: $!"
