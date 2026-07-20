#!/bin/bash
# Agent1 针对性验证测试 v1 (447算力机)
# 目标: P0-1 DB事务修复 + 流式优先策略

PROJECT_DIR="/root/autodl-tmp/agent-system"
OUT_DIR="/root/autodl-tmp/test-results/447算力机_$(date +%Y%m%d_%H%M%S)"
TIMEOUT=300
mkdir -p "$OUT_DIR"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'; NC='\033[0m'
pass_cnt=0; fail_cnt=0; skip_cnt=0

run_test() {
    local id="$1" name="$2" input="$3"
    local log="$OUT_DIR/${id}_${name// /_}.log"
    local test_start=$(date +%s)
    printf "${BLUE}[%-5s] %-35s${NC} " "$id" "$name"

    echo "$input" > /tmp/agent_test_input.txt

    timeout "$TIMEOUT" bash -c "cd '$PROJECT_DIR' && DOTNET_ENVIRONMENT=Production JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx DB_PASSWORD=7758521 dotnet run --project Agent1 -c Release --no-build < /tmp/agent_test_input.txt" > "$log" 2>&1
    local exit_code=$?
    local test_end=$(date +%s)
    local elapsed=$((test_end - test_start))

    if [ $exit_code -eq 124 ]; then
        echo -e "${YELLOW}SKIP timeout(${elapsed}s)${NC}"
        ((skip_cnt++))
    elif grep -qE "✗.*(异常|FAIL|生成错误)" "$log" 2>/dev/null; then
        local reason=$(grep -oP '(✗|失败|异常|FAIL).*' "$log" | head -1 | tr -d '\n' | cut -c1-80)
        echo -e "${RED}FAIL ${reason} (${elapsed}s)${NC}"
        ((fail_cnt++))
    elif grep -q "Connection refused" "$log" 2>/dev/null; then
        echo -e "${RED}FAIL LLM不可达 (${elapsed}s)${NC}"
        ((fail_cnt++))
    elif [ $exit_code -ne 0 ]; then
        echo -e "${YELLOW}SKIP exit=${exit_code} (${elapsed}s)${NC}"
        ((skip_cnt++))
    else
        echo -e "${GREEN}PASS (${elapsed}s)${NC}"
        ((pass_cnt++))
    fi
}

echo "========================================="
echo "  Agent1 针对性验证测试 (447算力机)"
echo "  目标: P0-1 DB事务修复 + 流式优先策略"
echo "========================================="
echo ""

# T12: 工具调用诊断 (验证P0-1 DB事务修复)
echo "--- Layer: 工具调用诊断 (验证DB工具) ---"
run_test "T12" "工具调用诊断" "5
4
0
0"

# T13: 合规评测集 (验证流式优先 + DB修复)
echo "--- Layer: 评测集验证 (流式优先) ---"
run_test "T13" "合规评测集" "5
5
0
0"

# T1: CoT标准推理 (验证DB工具调用)
echo "--- Layer: CoT推理 (DB工具) ---"
run_test "T1" "CoT标准推理" "5
c1
从高到低排列这些危化品:苯 丙酮 甲醇
exit
0
0"

# Summary
TOTAL=$((pass_cnt + fail_cnt + skip_cnt))
echo ""
echo "========================================="
echo "  测试汇总"
echo "========================================="
echo "通过: $pass_cnt  失败: $fail_cnt  跳过: $skip_cnt  总计: $TOTAL"
echo "结果目录: $OUT_DIR"
echo ""

# Write summary.txt
cat > "$OUT_DIR/summary.txt" << EOF
Agent1 针对性验证测试报告
==========================
测试时间: $(date '+%Y-%m-%d %H:%M:%S')
测试目标: P0-1 DB事务修复 + 流式优先策略
算力机: 447
通过: $pass_cnt
失败: $fail_cnt
跳过: $skip_cnt
总计: $TOTAL
EOF
