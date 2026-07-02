#!/bin/bash
# ═══════════════════════════════════════════════════════════
# test-status.sh — 测试状态文件更新器
# 由 auto_test_v2.sh 在每项测试完成后调用
# 本地监控端通过 SSH 读取 STATUS_FILE 获取实时进度
#
# 用法:
#   source scripts/test-status.sh              # 初始化
#   test_status_init <output_dir>              # 设置状态文件路径
#   test_status_update <id> <name> <result> <elapsed>  # 更新进度
#   test_status_finish <pass> <fail> <skip>    # 标记完成
# ═══════════════════════════════════════════════════════════

STATUS_FILE=""
TEST_COUNT=0
PASS_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0
START_TIME=""

test_status_init() {
    local out_dir="$1"
    STATUS_FILE="$out_dir/status.json"
    START_TIME=$(date +%s)
    # 写入初始状态
    cat > "$STATUS_FILE" << EOF
{
  "started_at": "$(date -Iseconds)",
  "status": "running",
  "total": 0,
  "passed": 0,
  "failed": 0,
  "skipped": 0,
  "elapsed_s": 0,
  "current_test": null,
  "results": []
}
EOF
}

test_status_update() {
    local id="$1" name="$2" result="$3" elapsed="$4"
    local now=$(date +%s)
    local total_elapsed=$((now - START_TIME))
    TEST_COUNT=$((TEST_COUNT + 1))

    # 根据结果递增计数器
    case "$result" in
        pass|PASS) PASS_COUNT=$((PASS_COUNT + 1)) ;;
        fail|FAIL) FAIL_COUNT=$((FAIL_COUNT + 1)) ;;
        skip|SKIP) SKIP_COUNT=$((SKIP_COUNT + 1)) ;;
    esac

    # 用 jq 更新 JSON（如果 jq 不可用，用简单 sed）
    if command -v jq &>/dev/null; then
        jq --arg id "$id" \
           --arg name "$name" \
           --arg result "$result" \
           --arg elapsed "$elapsed" \
           --argjson total "$TEST_COUNT" \
           --argjson passed "$PASS_COUNT" \
           --argjson failed "$FAIL_COUNT" \
           --argjson skipped "$SKIP_COUNT" \
           --argjson elapsed_s "$total_elapsed" \
           '.total = $total |
            .passed = $passed |
            .failed = $failed |
            .skipped = $skipped |
            .elapsed_s = $elapsed_s |
            .current_test = {"id": $id, "name": $name, "result": $result, "elapsed_s": $elapsed} |
            .results += [{"id": $id, "name": $name, "result": $result, "elapsed_s": $elapsed}]' \
            "$STATUS_FILE" > "${STATUS_FILE}.tmp" && mv "${STATUS_FILE}.tmp" "$STATUS_FILE"
    else
        # 无 jq 的降级方案：追加以行分隔的纯文本
        echo "$result|$id|$name|${elapsed}s|${total_elapsed}s" >> "${STATUS_FILE}.lines"
    fi
}

test_status_finish() {
    local pass="$1" fail="$2" skip="$3"
    local now=$(date +%s)
    local total_elapsed=$((now - START_TIME))

    if command -v jq &>/dev/null; then
        jq --argjson pass "$pass" \
           --argjson fail "$fail" \
           --argjson skip "$skip" \
           --argjson elapsed_s "$total_elapsed" \
           '.status = "completed" |
            .passed = $pass |
            .failed = $fail |
            .skipped = $skip |
            .elapsed_s = $elapsed_s |
            .current_test = null' \
            "$STATUS_FILE" > "${STATUS_FILE}.tmp" && mv "${STATUS_FILE}.tmp" "$STATUS_FILE"
    else
        echo "COMPLETED|pass=$pass|fail=$fail|skip=$skip|elapsed=${total_elapsed}s" >> "${STATUS_FILE}.lines"
    fi
}
