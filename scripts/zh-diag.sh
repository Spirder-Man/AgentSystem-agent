#!/bin/bash
# ==========================================
#  Agent1 算力机中文诊断助手
#  用法: bash zh-diag.sh [check|log|explain]
# ==========================================

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

SECTION() { echo -e "\n${YELLOW}━━━ $1 ━━━${NC}"; }
OK()     { echo -e "  ${GREEN}✅ $1${NC}"; }
FAIL()   { echo -e "  ${RED}❌ $1${NC}"; }

# ==================== 一、全面体检 ====================
check_all() {
    echo "=========================================="
    echo "  Agent1 算力机中文诊断报告"
    echo "  $(date '+%Y-%m-%d %H:%M:%S')"
    echo "=========================================="

    # --- 1. 显存 ---
    SECTION "1. 显卡显存"
    VRAM=$(nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader 2>/dev/null)
    if [ -n "$VRAM" ]; then
        USED=$(echo "$VRAM" | awk -F',' '{print $1}' | tr -d ' ')
        TOTAL=$(echo "$VRAM" | awk -F',' '{print $2}' | tr -d ' ')
        echo "  已用: $USED / 总共: $TOTAL"
        USED_MB=$(echo "$USED" | sed 's/MiB//')
        TOTAL_MB=$(echo "$TOTAL" | sed 's/MiB//')
        FREE_MB=$((TOTAL_MB - USED_MB))
        echo "  可用显存: ${FREE_MB} MiB (约 $((FREE_MB/1024)) GB)"
        if [ $FREE_MB -lt 4000 ]; then
            FAIL "显存不足 4GB — 无法同时跑 LLM + Embedding 两个服务"
        else
            OK "显存充足"
        fi
    else
        FAIL "未检测到 NVIDIA 显卡 / nvidia-smi 不可用"
    fi

    # --- 2. llama-server 进程 ---
    SECTION "2. llama-server 服务状态"
    PROC_COUNT=$(pgrep -c "llama-server" 2>/dev/null || echo 0)
    if [ "$PROC_COUNT" -ge 1 ]; then
        OK "llama-server 正在运行 ($PROC_COUNT 个进程)"
        ps -o pid,comm,etime -p $(pgrep -d',' "llama-server") --no-headers 2>/dev/null | while read line; do
            echo "    $line"
        done
    else
        FAIL "llama-server 没有运行 — LLM 推理服务不可用"
    fi

    # --- 3. 端口 ---
    SECTION "3. 服务端口"
    for PORT in 8080 8081; do
        if curl -s --max-time 2 "http://localhost:$PORT/health" | grep -q "ok"; then
            if [ "$PORT" = "8080" ]; then
                OK "端口 8080 (LLM 推理) — 正常"
            else
                OK "端口 8081 (Embedding) — 正常"
            fi
        else
            if [ "$PORT" = "8080" ]; then
                FAIL "端口 8080 (LLM 推理) — 挂了，AI 对话功能不可用"
            else
                FAIL "端口 8081 (Embedding) — 挂了，知识库检索会报错"
            fi
        fi
    done

    # --- 4. 模型文件 ---
    SECTION "4. 模型文件"
    MODEL_DIR="/root/autodl-tmp/Models"
    if [ -d "$MODEL_DIR" ]; then
        for f in "$MODEL_DIR"/*.gguf; do
            [ -e "$f" ] || continue
            SIZE=$(ls -lh "$f" | awk '{print $5}')
            NAME=$(basename "$f")
            case "$NAME" in
                *Qwen*8B*Q4*)
                    echo "  推理模型: $NAME ($SIZE)"
                    if echo "$SIZE" | grep -q "G"; then
                        OK "文件大小正常 (应约 5.5G)"
                    else
                        FAIL "文件太小！正常应约 5.5GB，当前只有 $SIZE — 下载中断了"
                    fi
                    ;;
                *nomic*)
                    echo "  Embedding: $NAME ($SIZE)"
                    ;;
            esac
        done
    else
        FAIL "模型目录 $MODEL_DIR 不存在"
    fi

    # --- 5. PostgreSQL ---
    SECTION "5. PostgreSQL 数据库"
    if pg_isready -q 2>/dev/null; then
        OK "PostgreSQL 运行中"
    else
        FAIL "PostgreSQL 未运行 — 知识库、审计日志都不可用"
    fi

    # --- 6. 磁盘 ---
    SECTION "6. 磁盘空间"
    df -h /root/autodl-tmp | tail -1 | awk '{printf "  已用: %s / 总共: %s (使用率: %s)\n", $3, $2, $5}'

    echo ""
    echo "=========================================="
    echo "  诊断完成。带 ❌ 的条目需要处理。"
    echo "=========================================="
}

# ==================== 二、错误日志翻译 ====================
explain_error() {
    echo "=========================================="
    echo "  常见报错中文解释"
    echo "=========================================="
    echo ""
    echo "1. 'Connection refused (localhost:8080)'"
    echo "   → LLM 推理服务没启动或挂了"
    echo "   → 解决: 重新启动 llama-server"
    echo ""
    echo "2. 'tensor ... not within the file bounds, corrupted'"
    echo "   → 模型文件下载不完整（损坏了）"
    echo "   → 解决: 删掉重新下载，确认文件大小 > 5GB"
    echo ""
    echo "3. 'CUDA error: out of memory'"
    echo "   → 显卡显存不够用了"
    echo "   → 解决: 停掉一个服务，或给 Embedding 去掉 -ngl 参数用 CPU"
    echo ""
    echo "4. 'nvcc: command not found'"
    echo "   → 这只是 CUDA 编译器没装，不影响运行推理"
    echo "   → 不需要处理，只要 ldd 能看到 libcudart.so 就行"
    echo ""
    echo "5. 'No such file or directory'"
    echo "   → Linux 严格区分大小写！检查路径里的字母大小写"
    echo "   → 例如: Models ≠ models, Qwen ≠ qwen"
    echo ""
    echo "6. 'git clone ... RPC failed / early EOF'"
    echo "   → 网络问题，GitHub 下载中断"
    echo "   → 解决: 换 ghproxy 镜像，或本地下载 zip 上传"
    echo ""
    echo "7. 'failed to load model'"
    echo "   → 模型文件路径错了，或文件坏了"
    echo "   → 解决: 先 ls -lh 确认文件存在且大小正常"
    echo ""
}

# ==================== 三、快速修复 ====================
quick_fix() {
    echo "=========================================="
    echo "  自动修复（仅执行安全的操作）"
    echo "=========================================="
    echo ""

    # 检查并创建日志目录
    if [ ! -d "/root/autodl-tmp/logs" ]; then
        mkdir -p /root/autodl-tmp/logs
        echo "  ✅ 已创建日志目录 /root/autodl-tmp/logs"
    fi

    # 停掉可能卡死的旧进程
    OLD=$(pgrep -c "llama-server" 2>/dev/null || echo 0)
    if [ "$OLD" -gt 0 ]; then
        echo "  ⚠️  发现 $OLD 个 llama-server 残留进程（可能已卡死）"
        read -p "  是否停掉它们？[y/N] " yn
        if [ "$yn" = "y" ] || [ "$yn" = "Y" ]; then
            pkill -f "llama-server"
            echo "  ✅ 已停止"
        fi
    fi

    echo ""
    echo "  如需重新启动服务，运行:"
    echo "  bash zh-diag.sh start"
    echo ""
}

# ==================== 四、一键启动 ====================
start_services() {
    echo "=========================================="
    echo "  启动 llama.cpp 双服务"
    echo "=========================================="

    MODEL_DIR="/root/autodl-tmp/Models"
    LLAMA_DIR="/root/autodl-tmp/llama.cpp/build/bin"

    # 验证
    if [ ! -f "$LLAMA_DIR/llama-server" ]; then
        FAIL "llama-server 不存在: $LLAMA_DIR/llama-server"
        echo "  请先编译 llama.cpp"
        return 1
    fi

    LLM_MODEL="$MODEL_DIR/Qwen_Qwen3-8B-Q4_K_M.gguf"
    EMB_MODEL="$MODEL_DIR/nomic-embed-text-v1.5.f16.gguf"

    if [ ! -f "$LLM_MODEL" ]; then
        FAIL "推理模型不存在: $LLM_MODEL"
        return 1
    fi
    LLM_SIZE=$(stat -c%s "$LLM_MODEL" 2>/dev/null)
    if [ "$LLM_SIZE" -lt 5000000000 ]; then
        FAIL "推理模型文件太小 ($(echo "scale=1; $LLM_SIZE/1024/1024/1024" | bc)GB)，正常应约 5.5GB，文件损坏了"
        return 1
    fi

    if [ ! -f "$EMB_MODEL" ]; then
        FAIL "Embedding 模型不存在: $EMB_MODEL"
        return 1
    fi

    OK "模型文件验证通过"

    # 停旧进程
    pkill -f "llama-server" 2>/dev/null
    sleep 2

    mkdir -p /root/autodl-tmp/logs

    echo "  启动 LLM 推理服务 (8080)..."
    # KV Cache: -c 32768 = 32K 上下文窗口，支持 63 条连续评测不溢出
    # --cache-type-k q8_0 + --cache-type-v q8_0 = KV Cache 量化，VRAM 降至 ~2GB
    # -fa = Flash Attention，进一步减少显存占用
    # -sps 0.0 = 禁用 LCP 自动 slot 匹配，每个请求独立 KV Cache（防止跨请求累积）
    nohup "$LLAMA_DIR/llama-server" \
      -m "$LLM_MODEL" \
      --host 0.0.0.0 --port 8080 -ngl 99 -c 32768 \
      --cache-type-k q8_0 --cache-type-v q8_0 -fa \
      -sps 0.0 \
      > /root/autodl-tmp/logs/llama-server.log 2>&1 &
    echo "    PID: $!"

    echo "  启动 Embedding 服务 (8081)..."
    # Embedding 模型只需小上下文，保留 -c 2048
    nohup "$LLAMA_DIR/llama-server" \
      -m "$EMB_MODEL" \
      --host 0.0.0.0 --port 8081 --embeddings -c 2048 --batch-size 1024 \
      > /root/autodl-tmp/logs/llama-embed.log 2>&1 &
    echo "    PID: $!"

    echo "  等待模型加载 (约20秒)..."
    sleep 20

    # 验证
    if curl -s --max-time 3 http://localhost:8080/health | grep -q "ok"; then
        OK "8080 (LLM推理) 启动成功"
    else
        FAIL "8080 启动失败，查看日志: tail -50 /root/autodl-tmp/logs/llama-server.log"
    fi

    if curl -s --max-time 3 http://localhost:8081/health | grep -q "ok"; then
        OK "8081 (Embedding) 启动成功"
    else
        FAIL "8081 启动失败，查看日志: tail -50 /root/autodl-tmp/logs/llama-embed.log"
    fi

    echo ""
    echo "  现在可以回 Agent1 客户端正常对话了！"
    echo "=========================================="
}

# ==================== 入口 ====================
case "${1:-check}" in
    check)   check_all ;;
    log)     explain_error ;;
    explain) explain_error ;;
    fix)     quick_fix ;;
    start)   start_services ;;
    *)
        echo "用法: bash zh-diag.sh [命令]"
        echo ""
        echo "  check   全面体检（默认）— 检查显存/服务/模型/数据库"
        echo "  log     常见报错中文解释"
        echo "  fix     自动修复（安全操作）"
        echo "  start   一键启动 llama.cpp 双服务"
        ;;
esac
