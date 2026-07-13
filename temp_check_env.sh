#!/bin/bash
echo "=== API Process Environment ==="
API_PID=$(ps aux | grep 'Agent1.Api.dll' | grep -v grep | awk '{print $2}' | head -1)
echo "API PID: $API_PID"
if [ -n "$API_PID" ]; then
  cat /proc/$API_PID/environ | tr '\0' '\n' | grep -E 'AUTH|JWT|DB_PASS|MULTIMODAL|EMBEDDING' || echo "No matching vars found"
else
  echo "API process not found"
  ps aux | grep dotnet | grep -v grep
fi

echo ""
echo "=== Test login ==="
curl -s -X POST http://localhost:5001/api/Auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}'
