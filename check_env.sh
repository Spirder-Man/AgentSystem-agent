#!/bin/bash
API_PID=$(ps aux | grep "dotnet run.*Agent1.Api" | grep -v grep | awk '{print $2}' | head -1)
echo "API PID: $API_PID"
echo "=== JWT env ==="
cat /proc/$API_PID/environ 2>/dev/null | tr '\0' '\n' | grep -E 'JWT|AUTH|DB_' || echo "no env found"
echo "=== JWT from API debug ==="
curl -s http://localhost:5001/health
echo ""
