#!/bin/bash
LOG="/tmp/quick_test.log"
echo "=== Quick Test: 双层门卫+Handler闭环 ===" > $LOG

# 获取 token
TOKEN=$(curl -s -X POST http://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

test_q() {
  local label="$1"; local query="$2"
  echo "" >> $LOG
  echo "[$label] $query" >> $LOG
  RESP=$(curl -s -X POST http://localhost:5001/api/compliance/check \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    -d "{\"query\":\"$query\"}" 2>/dev/null)
  echo "$RESP" | python3 -c "
import sys,json
r=json.load(sys.stdin)
resp=r.get('response','')
if 'DICTIONARY_HIT' in resp or 'DATABASE_HIT' in resp:
    print('✅ RULE_ENGINE:', resp[:150])
elif '无法给出' in resp:
    print('❌ NO_RESULT')
else:
    print('❓ UNKNOWN:', resp[:150])
" >> $LOG
}

# S1: Tier1 物质名 + 陌生动词  
test_q "S1-陌生动词" "苯和乙醇可以搁一块储存吗？"

# S2: Tier1 物质名 + 口语化
test_q "S2-口语化" "丙酮可以和烧碱堆一起吗？"

# S3: 完全无关
test_q "S3-蜘蛛侠" "超人厉害还是蝙蝠侠厉害？"

# S4: 原始场景 (应有规则引擎结果)
test_q "S4-原始" "苯和丙酮可以存放在一起吗？"

# API log
echo "" >> $LOG
echo "=== 关键日志 ===" >> $LOG
grep -E 'LLM降级|规则引擎|StorageCompat|HazardCategory|生成失败|熔断器' /tmp/api2.log | tail -10 >> $LOG

cat $LOG
