#!/bin/bash
# ==========================================
# llama.cpp 日志实时中文翻译工具
# 用法: bash llama-log-zh.sh [日志文件路径]
# 默认: 自动找最新的 llama 日志
# ==========================================

LOG_FILE="${1:-$(ls -t /root/autodl-tmp/logs/llama-*.log 2>/dev/null | head -1)}"

if [ -z "$LOG_FILE" ] || [ ! -f "$LOG_FILE" ]; then
  echo "未找到 llama 日志文件"
  echo "用法: bash $0 [日志文件路径]"
  exit 1
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  llama.cpp 日志实时中文解读"
echo "  日志文件: $LOG_FILE"
echo "  Ctrl+C 退出"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

tail -f "$LOG_FILE" | while IFS= read -r line; do
  # 提取时间戳（格式: HH.MM.SSS.mmm）
  ts=$(echo "$line" | grep -oP '^\d+\.\d+\.\d+\.\d+' | head -1)

  # --- 新请求进入 ---
  if echo "$line" | grep -q "operator().*Chat format"; then
    format=$(echo "$line" | grep -oP 'Chat format: \K\S+')
    echo -e "\n\033[1;36m[$ts] >>> 新请求进入 (格式: $format)\033[0m"

  # --- 选择推理槽位 ---
  elif echo "$line" | grep -q "selected slot"; then
    slot_id=$(echo "$line" | grep -oP 'id\s+\K\d+')
    echo -e "\033[0;33m[$ts]  |  分配推理槽位 #$slot_id\033[0m"

  # --- 开始处理 ---
  elif echo "$line" | grep -q "launch_slot_"; then
    slot_id=$(echo "$line" | grep -oP 'id\s+\K\d+')
    task_id=$(echo "$line" | grep -oP 'task \K\d+')
    echo -e "\033[0;32m[$ts]  |  槽位 #$slot_id 开始推理 (任务 $task_id)\033[0m"

  # --- Prompt 缓存命中 ---
  elif echo "$line" | grep -q "found better prompt"; then
    keep=$(echo "$line" | grep -oP 'f_keep = \K[0-9.]+')
    sim=$(echo "$line" | grep -oP 'sim = \K[0-9.]+')
    pct=$(awk "BEGIN{printf \"%.0f\", $keep * 100}" 2>/dev/null)
    echo -e "\033[0;35m[$ts]  |  命中 Prompt 缓存 (复用 ${pct}%, 相似度 $sim)\033[0m"

  # --- 推理速度统计 ---
  elif echo "$line" | grep -q "print_timing.*eval time" && ! echo "$line" | grep -q "prompt eval"; then
    tokens=$(echo "$line" | grep -oP '\|\s+eval time.*?/\s*\K\d+')
    speed=$(echo "$line" | grep -oP '(\d+\.\d+) tokens per second' | grep -oP '^\d+\.\d+')
    ms=$(echo "$line" | grep -oP 'eval time =\s+\K[0-9.]+')
    echo -e "\033[0;37m[$ts]  |  生成: ${tokens} tokens, 速度 ${speed} t/s, 耗时 ${ms}ms\033[0m"

  # --- Prompt 评估速度 ---
  elif echo "$line" | grep -q "prompt eval time"; then
    tokens=$(echo "$line" | grep -oP 'prompt eval time.*?/\s*\K\d+')
    speed=$(echo "$line" | grep -oP '(\d+\.\d+) tokens per second' | grep -oP '^\d+\.\d+')
    echo -e "\033[0;37m[$ts]  |  理解输入: ${tokens} tokens, 速度 ${speed} t/s\033[0m"

  # --- 总耗时 ---
  elif echo "$line" | grep -q "total time"; then
    total_ms=$(echo "$line" | grep -oP 'total time =\s+\K[0-9.]+')
    total_tokens=$(echo "$line" | grep -oP 'total time.*?/\s*\K\d+')
    total_s=$(awk "BEGIN{printf \"%.1f\", $total_ms / 1000}" 2>/dev/null)
    echo -e "\033[1;37m[$ts]  |  总计: ${total_tokens} tokens, 用时 ${total_s}s\033[0m"

  # --- 完成释放 ---
  elif echo "$line" | grep -q "slot.*release"; then
    slot_id=$(echo "$line" | grep -oP 'id\s+\K\d+')
    n_tokens=$(echo "$line" | grep -oP 'n_tokens = \K\d+')
    echo -e "\033[0;32m[$ts]  |  槽位 #$slot_id 完成 (上下文 $n_tokens tokens)\033[0m"

  # --- 所有槽位空闲 ---
  elif echo "$line" | grep -q "all slots are idle"; then
    echo -e "\033[1;36m[$ts] <<< 推理完成, 等待下一个请求...\033[0m\n"

  # --- 推理预算激活（思考模式） ---
  elif echo "$line" | grep -q "reasoning-budget.*activated"; then
    budget=$(echo "$line" | grep -oP 'budget=\K\d+')
    echo -e "\033[0;35m[$ts]  |  深度思考模式已激活\033[0m"

  # --- KV Cache 状态 ---
  elif echo "$line" | grep -q "cache state:"; then
    prompts=$(echo "$line" | grep -oP '\d+ prompts')
    size=$(echo "$line" | grep -oP '[0-9.]+ MiB' | head -1)
    echo -e "\033[0;34m[$ts]  |  缓存状态: $prompts, 占用 $size\033[0m"

  # --- Prompt 缓存更新耗时 ---
  elif echo "$line" | grep -q "prompt cache update took"; then
    ms=$(echo "$line" | grep -oP 'took \K[0-9.]+')
    echo -e "\033[0;34m[$ts]  |  缓存更新耗时: ${ms}ms\033[0m"

  # --- 错误/警告 ---
  elif echo "$line" | grep -qP '^\S+\s+E\s'; then
    msg=$(echo "$line" | sed 's/^[^ ]* E //')
    echo -e "\033[1;31m[$ts] !!! 错误: $msg\033[0m"

  elif echo "$line" | grep -qP '^\S+\s+W\s'; then
    # 跳过常见的 prompt_save 警告（正常行为）
    if ! echo "$line" | grep -q "prompt_save"; then
      msg=$(echo "$line" | sed 's/^[^ ]* W //')
      echo -e "\033[1;33m[$ts] >>> 警告: $msg\033[0m"
    fi
  fi
done
