#!/bin/bash
# 双层门卫闭环验证 — 5 个边界场景全覆盖
LOG="/root/autodl-tmp/logs/api-e2e-double-gate.log"
echo "=== 双层门卫闭环验证 ===" > $LOG
echo "测试时间: $(date)" >> $LOG
echo "代码版本: $(cd /root/autodl-tmp/agent-system && git log --oneline -1)" >> $LOG
echo "" >> $LOG

# 确认 LLM 不可用
echo "--- LLM 状态 ---" >> $LOG
curl -s -o /dev/null -w "llama:8080 HTTP %{http_code}\n" http://localhost:8080/health 2>&1 >> $LOG
echo "" >> $LOG

# 获取 token
LOGIN_RESP=$(curl -s -X POST http://localhost:5001/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}')
TOKEN=$(echo "$LOGIN_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])" 2>/dev/null)

# 测试函数
test_query() {
  local LABEL="$1"
  local QUERY="$2"
  local EXPECT="$3"
  echo "" >> $LOG
  echo "──────────────────────────────────────" >> $LOG
  echo "[$LABEL] $QUERY" >> $LOG
  echo "期望: $EXPECT" >> $LOG
  RESP=$(curl -s -X POST http://localhost:5001/api/compliance/check \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    -d "{\"query\":\"$QUERY\"}" 2>/dev/null)
  echo "Response: $RESP" | head -c 1500 >> $LOG
  echo "" >> $LOG
}

# ── 场景 1: Tier 1 物质名匹配 — 化学品名+陌生动词 ──
test_query "S1-Tier1-陌生动词" \
  "苯和乙醇能搁一块儿吗？" \
  "Tier1: 含'苯'→放行→规则引擎回答"

# ── 场景 2: Tier 1 物质名匹配 — 化学品名+无信号词 ──
test_query "S2-Tier1-无信号词" \
  "丙酮能和烧碱放一个仓库吗？" \
  "Tier1: 含'丙酮'→放行→规则引擎回答"

# ── 场景 3: Tier 2 信号词匹配 — 不提名具体物质 ──
test_query "S3-Tier2-通用查询" \
  "危化品储存规范有哪些要求？" \
  "Tier2: 含'储存'+'危化品'→放行→规则引擎回答"

# ── 场景 4: 两层都不满足 — 完全无关输入 ──
test_query "S4-拦截-无关" \
  "蜘蛛侠和蝙蝠侠谁厉害？" \
  "两层均失败→拦截→BuildNoResult"

# ── 场景 5: 原始 Bug 场景 — 化学品名+存放(已有信号词) ──
test_query "S5-原始场景" \
  "苯和丙酮可以存放在同一库房吗？" \
  "Tier1+Tier2 双命中→规则引擎回答"

# 提取关键日志
echo "" >> $LOG
echo "=== API 日志关键行 ===" >> $LOG
grep -E '规则引擎|降级|LLM降级|熔断器|生成失败|DecoupledPipeline|FC=Required' /tmp/api.log 2>/dev/null | tail -40 >> $LOG

echo "" >> $LOG
echo "=== 测试完成 ===" >> $LOG
cat $LOG
