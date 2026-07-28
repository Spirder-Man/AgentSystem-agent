#!/bin/bash
# 测试规则引擎接管 — LLM 不可用时确定性降级
LOG="/root/autodl-tmp/logs/api-e2e-rule-v2.log"
echo "=== 规则引擎接管测试 (Bug-033 修复后) ===" > $LOG
echo "测试时间: $(date)" >> $LOG
echo "" >> $LOG

# 1. 确认 LLM 不可用
echo "--- 1. LLM 状态 ---" >> $LOG
curl -s -o /dev/null -w "llama:8080 HTTP %{http_code}\n" http://localhost:8080/health 2>&1 >> $LOG
echo "" >> $LOG

# 2. 获取 token
echo "--- 2. 获取 Token ---" >> $LOG
LOGIN_RESP=$(curl -s -X POST http://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}')
echo "Login: $LOGIN_RESP" >> $LOG
TOKEN=$(echo "$LOGIN_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])" 2>/dev/null)
echo "Token长度: ${#TOKEN}" >> $LOG
echo "" >> $LOG

# 3. 发送化工合规查询 (LLM 不可用 → 规则引擎接管)
echo "--- 3. 合规检查: 苯和丙酮 ---" >> $LOG
RESP=$(curl -s -X POST http://localhost:5001/api/compliance/check \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"苯和丙酮可以存放在同一库房吗？"}')
echo "Response: $RESP" | head -c 2000 >> $LOG
echo "" >> $LOG
echo "" >> $LOG

# 4. 提取关键日志
echo "--- 4. API 日志关键行 ---" >> $LOG
grep -E '规则引擎|降级|LLM降级|ChemicalSignalGate|Fallback|熔断器|生成失败|DecoupledPipeline' /tmp/api.log 2>/dev/null | tail -30 >> $LOG

echo "" >> $LOG
echo "=== 测试完成 ===" >> $LOG
cat $LOG
