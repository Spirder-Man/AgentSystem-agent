#!/bin/bash
API_PID=$(ps aux | grep "dotnet.*Agent1.Api" | grep -v grep | awk '{print $2}' | head -1)
echo "API PID: $API_PID"
echo "CWD: $(ls -la /proc/$API_PID/cwd 2>/dev/null | awk '{print $NF}')"
echo ""

# Check JWT_KEY and see if appsettings has Jwt section
echo "=== Check if Agent1.Api/ dir has appsettings ==="
ls -la /root/autodl-tmp/agent-system/Agent1.Api/appsettings*.json 2>&1
echo ""

echo "=== Check Agent1/ appsettings Jwt section ==="
grep -A3 '"Jwt"' /root/autodl-tmp/agent-system/Agent1/appsettings.json 2>/dev/null || echo "No Jwt section in Agent1/appsettings.json"
echo ""

echo "=== Check Agent1.Api bin appsettings ==="
ls -la /root/autodl-tmp/agent-system/Agent1.Api/bin/Release/net8.0/appsettings*.json 2>&1
echo ""

echo "=== Login and decode JWT ==="
LOGIN=$(curl -s -X POST http://localhost:5001/api/Auth/login -H 'Content-Type: application/json' -d '{"username":"admin","password":"7758521"}')
TOKEN=$(echo "$LOGIN" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
echo "Token length: ${#TOKEN}"

# Decode JWT header
HEADER=$(echo "$TOKEN" | cut -d'.' -f1 | tr '_-' '/+')
# Pad
MLEN=${#HEADER}; REM=$((MLEN % 4)); [ $REM -eq 1 ] && HEADER="${HEADER}==="; [ $REM -eq 2 ] && HEADER="${HEADER}=="; [ $REM -eq 3 ] && HEADER="${HEADER}="
echo "JWT Header: $(echo $HEADER | base64 -d 2>/dev/null || echo 'decode failed')"

# Decode JWT payload
PAYLOAD=$(echo "$TOKEN" | cut -d'.' -f2 | tr '_-' '/+')
MLEN=${#PAYLOAD}; REM=$((MLEN % 4)); [ $REM -eq 1 ] && PAYLOAD="${PAYLOAD}==="; [ $REM -eq 2 ] && PAYLOAD="${PAYLOAD}="; [ $REM -eq 3 ] && PAYLOAD="${PAYLOAD}="
echo "JWT Payload: $(echo $PAYLOAD | base64 -d 2>/dev/null || echo 'decode failed')"
