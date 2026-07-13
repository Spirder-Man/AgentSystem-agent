#!/bin/bash
# ==========================================
# Backend API E2E Tests (no python dependency)
# ==========================================

echo "=========================================="
echo "  Agent1 后端 API 端到端测试"
echo "=========================================="
echo ""

# 2.1 Login
echo "--- 2.1 登录认证 ---"
LOGIN=$(curl -s --max-time 10 -X POST http://localhost:5001/api/Auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}')
TOKEN=$(echo "$LOGIN" | sed 's/.*"token":"\([^"]*\)".*/\1/')
ROLE=$(echo "$LOGIN" | sed 's/.*"role":"\([^"]*\)".*/\1/')
if [ -n "$TOKEN" ] && [ "$TOKEN" != "$LOGIN" ]; then
  echo "✅ 登录成功"
  echo "   Token: ${TOKEN:0:50}..."
  echo "   Role: $ROLE"
else
  echo "❌ 登录失败: ${LOGIN:0:200}"
  exit 1
fi

echo ""

# 2.2 Compliance Check (GPU推理)
echo "--- 2.2 合规检查 (GPU推理, 约30-60s) ---"
COMPLIANCE=$(timeout 120 curl -s --max-time 120 -X POST http://localhost:5001/api/Compliance/check \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"query":"苯和丙酮可以存放在同一库房吗？"}')

# Extract key fields
TOOLS=$(echo "$COMPLIANCE" | grep -o '"toolsUsed":\[[^]]*\]' | head -1)
VERIFIED=$(echo "$COMPLIANCE" | grep -o '"verifiedRegulations":\[[^]]*\]' | head -1)
HALLUC=$(echo "$COMPLIANCE" | grep -o '"hallucinatedRegulations":\[[^]]*\]' | head -1)
RESP=$(echo "$COMPLIANCE" | grep -o '"response":"[^"]*"' | head -1 | cut -c1-200)

echo "   Tools: ${TOOLS:-N/A}"
echo "   Verified: ${VERIFIED:-N/A}"
echo "   Hallucinated: ${HALLUC:-N/A}"
echo "   Response: ${RESP:-N/A}"

if [ -n "$TOOLS" ]; then
  echo "✅ 合规检查完成 (GPU推理成功)"
else
  echo "⚠️ 合规检查响应异常: ${COMPLIANCE:0:300}"
fi

echo ""

# 2.3 Assets
echo "--- 2.3 资产台账 ---"
ASSETS=$(curl -s --max-time 10 http://localhost:5001/api/Inspection/assets \
  -H "Authorization: Bearer $TOKEN")
# Count items
ITEM_COUNT=$(echo "$ASSETS" | grep -o '"name"' | wc -l)
echo "   资产总数: $ITEM_COUNT"
# Show first 3
echo "$ASSETS" | grep -o '"name":"[^"]*","cas":"[^"]*"' | head -3 | while read line; do
  NAME=$(echo "$line" | sed 's/"name":"\([^"]*\)".*/\1/')
  CAS=$(echo "$line" | sed 's/.*"cas":"\([^"]*\)".*/\1/')
  echo "   - $NAME (CAS: $CAS)"
done

echo ""

# 2.4 Summary
echo "--- 2.4 合规摘要 ---"
SUMMARY=$(curl -s --max-time 10 http://localhost:5001/api/Compliance/summary \
  -H "Authorization: Bearer $TOKEN")
TOTAL=$(echo "$SUMMARY" | grep -o '"totalAssets":[0-9]*' | grep -o '[0-9]*')
COMPLIANT=$(echo "$SUMMARY" | grep -o '"compliant":[0-9]*' | grep -o '[0-9]*')
NONCOMP=$(echo "$SUMMARY" | grep -o '"nonCompliant":[0-9]*' | grep -o '[0-9]*')
echo "   Total Assets: ${TOTAL:-?}"
echo "   Compliant: ${COMPLIANT:-?}"
echo "   Non-Compliant: ${NONCOMP:-?}"

echo ""
echo "=========================================="
echo "  测试完成"
echo "=========================================="
