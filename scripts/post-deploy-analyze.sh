#!/bin/bash
# ============================================================
# Post-deploy Log Analysis — 六维度日志深度分析
#
# 定位：post-deploy-eval.sh 的后半段 — 日志收集 + 六维分析
# 原则：
#   - 从 eval_reports/ 读取 summary.json，自动归类分析
#   - 收集 API/llama.cpp 服务日志，按时间窗口切片
#   - 按 D1-D6 六维度框架生成结构化分析报告
#   - 报告输出到 eval_reports/YYYYMMDD_HHMM/analysis.md
#
# 用法：bash scripts/post-deploy-analyze.sh [report_dir]
#       不传参数则分析最新的 eval_reports 目录
#
# Cron 集成（接在 post-deploy-eval.sh 之后）：
#   0 3 * * * ADMIN_PWD=xxx bash scripts/post-deploy-eval.sh && bash scripts/post-deploy-analyze.sh
# ============================================================

set -uo pipefail

# ── 配置 ──
PROJECT_DIR="${PROJECT_DIR:-/root/autodl-tmp/agent-system}"
EVAL_DIR="${PROJECT_DIR}/eval_reports"
LOG_DIR="${LOG_DIR:-/root/autodl-tmp/logs}"
API_LOG="${LOG_DIR}/agent1-api.log"
LLAMA_LLM_LOG="${LOG_DIR}/llama-server.log"
LLAMA_EMBED_LOG="${LOG_DIR}/llama-embed.log"
CURRENT_MODEL_VERSION="${MODEL_VERSION:-qwen3-8b-q4_k_m}"

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
ANALYSIS_ID="analysis-$(date +%Y%m%d-%H%M)"

# ── 确定分析目标目录 ──
if [ $# -ge 1 ]; then
  REPORT_DIR="$1"
else
  REPORT_DIR=$(ls -dt "${EVAL_DIR}"/20* 2>/dev/null | head -1)
fi

if [ -z "$REPORT_DIR" ] || [ ! -d "$REPORT_DIR" ]; then
  echo "[FATAL] No eval report directory found. Run post-deploy-eval.sh first."
  exit 1
fi

SUMMARY_FILE="${REPORT_DIR}/summary.json"
if [ ! -f "$SUMMARY_FILE" ]; then
  echo "[FATAL] summary.json not found in ${REPORT_DIR}"
  exit 1
fi

ANALYSIS_FILE="${REPORT_DIR}/analysis.md"
LOG_SLICE_DIR="${REPORT_DIR}/log_slices"
mkdir -p "$LOG_SLICE_DIR"

echo "╔══════════════════════════════════════════╗"
echo "║  Post-deploy Log Analyzer (D1-D6)        ║"
echo "║  Report: ${REPORT_DIR}                    ║"
echo "║  Time:   ${TIMESTAMP}                     ║"
echo "╚══════════════════════════════════════════╝"

# ═══════════════════════════════════════
# Step 5: 日志收集
# ═══════════════════════════════════════
echo ""
echo "=== [5/7] Collecting service logs ==="

# 5a. 从 summary.json 提取评测时间窗口
EVAL_TS=$(jq -r '.timestamp // empty' "$SUMMARY_FILE" 2>/dev/null)
if [ -n "$EVAL_TS" ]; then
  # 转换为日志 grep 用的日期格式
  EVAL_DATE=$(echo "$EVAL_TS" | cut -d' ' -f1 | tr -d '-')
  EVAL_HOUR=$(echo "$EVAL_TS" | cut -d' ' -f2 | cut -d: -f1)
  echo "  Eval timestamp: ${EVAL_TS} → searching logs around ${EVAL_DATE} ${EVAL_HOUR}:00"
else
  EVAL_DATE=$(date +%Y%m%d)
  EVAL_HOUR=$(date +%H)
  echo "  [WARN] No timestamp in summary.json, using current: ${EVAL_DATE} ${EVAL_HOUR}:00"
fi

# 5b. 切片 API 日志（评测前后 2 小时窗口）
API_PATTERN="${EVAL_DATE:0:4}-${EVAL_DATE:4:2}-${EVAL_DATE:6:2}"
if [ -f "$API_LOG" ]; then
  grep "$API_PATTERN" "$API_LOG" 2>/dev/null | tail -500 > "${LOG_SLICE_DIR}/api-eval-window.log" || true
  API_LINES=$(wc -l < "${LOG_SLICE_DIR}/api-eval-window.log" 2>/dev/null || echo "0")
  echo "  API log slice: ${API_LINES} lines"
else
  echo "  [WARN] API log not found: ${API_LOG}"
fi

# 5c. 切片 llama.cpp LLM 日志
if [ -f "$LLAMA_LLM_LOG" ]; then
  tail -200 "$LLAMA_LLM_LOG" > "${LOG_SLICE_DIR}/llama-llm-tail.log" 2>/dev/null || true
  echo "  llama.cpp LLM log: tail 200 lines"
else
  echo "  [WARN] llama.cpp LLM log not found: ${LLAMA_LLM_LOG}"
fi

# 5d. 切片 llama.cpp Embed 日志
if [ -f "$LLAMA_EMBED_LOG" ]; then
  tail -100 "$LLAMA_EMBED_LOG" > "${LOG_SLICE_DIR}/llama-embed-tail.log" 2>/dev/null || true
  echo "  llama.cpp Embed log: tail 100 lines"
fi

# ═══════════════════════════════════════
# 数据提取函数（供后续分析复用）
# ═══════════════════════════════════════

# 提取 summary.json 核心指标
MODEL=$(jq -r '.model // "unknown"' "$SUMMARY_FILE")
TOTAL=$(jq -r '.total // 0' "$SUMMARY_FILE")
TOOL_RATE=$(jq -r '.toolCallRate // 0' "$SUMMARY_FILE")
PARAM_ACC=$(jq -r '.parameterAccuracy // 0' "$SUMMARY_FILE")
CONCLUSION_ACC=$(jq -r '.conclusionAccuracy // 0' "$SUMMARY_FILE")
CASES_COUNT=$(jq -r '.casesCount // 0' "$SUMMARY_FILE")
CASES_WITH_ERRORS=$(jq -r '.casesWithErrors // 0' "$SUMMARY_FILE")
PREV_CONCLUSION=$(jq -r '.conclusion_accuracy.previous // 0' "${REPORT_DIR}/comparison.json" 2>/dev/null || echo "N/A")
ALERTS=$(jq -r '.alerts // ""' "$SUMMARY_FILE")

# 按工具分类统计
TOOL_DIST=$(jq -r '[.cases[]? | .expectedTools[0] // "unknown"] | group_by(.) | map({tool: .[0], count: length}) | sort_by(-.count) | .[] | "\(.tool): \(.count)"' "$SUMMARY_FILE" 2>/dev/null)

# 按失败模式分类
FAILED_CASES=$(jq -r '[.cases[]? | select(.conclusionMatch == false or .toolMatch == false)] | group_by(if .toolMatch == false then "A-未触发工具" elif .paramMatch == false then "B-参数错误" else "C-结论错误" end) | map({mode: .[0].toolMatch | if . == false then "A-未触发工具" else "C-结论错误" end, count: length, queries: [.[].query]})' "$SUMMARY_FILE" 2>/dev/null)

# 性能数据
TOTAL_TOOL_CALLS=$(jq -r '[.cases[]? | select(.toolMatch == true)] | length' "$SUMMARY_FILE" 2>/dev/null)
NO_TOOL_CALLS=$(jq -r '[.cases[]? | select(.toolMatch == false)] | length' "$SUMMARY_FILE" 2>/dev/null)

# API 日志中的关键信号提取
if [ -f "${LOG_SLICE_DIR}/api-eval-window.log" ]; then
  API_ERRORS=$(grep -c "ERR\|FATAL\|❌" "${LOG_SLICE_DIR}/api-eval-window.log" 2>/dev/null || echo "0")
  API_WARNS=$(grep -c "WRN\|⚠️" "${LOG_SLICE_DIR}/api-eval-window.log" 2>/dev/null || echo "0")
  PIPELINE_LINES=$(grep -c "\[Pipeline\]" "${LOG_SLICE_DIR}/api-eval-window.log" 2>/dev/null || echo "0")
  FC_VIOLATIONS=$(grep -c "本轮未调用任何工具\|工具调用=0" "${LOG_SLICE_DIR}/api-eval-window.log" 2>/dev/null || echo "0")
else
  API_ERRORS="N/A"
  API_WARNS="N/A"
  PIPELINE_LINES="N/A"
  FC_VIOLATIONS="N/A"
fi

# ═══════════════════════════════════════
# Step 6: 六维度分析
# ═══════════════════════════════════════
echo ""
echo "=== [6/7] Running D1-D6 analysis ==="

# ── 生成分析报告 ──
cat > "$ANALYSIS_FILE" << 'REPORT_HEADER'
# 远程评测六维度深度分析报告

> **自动生成** | 维度框架: D1-D6 | 方法论: engineering-deep-learning-methodology.md

---

## 元数据

REPORT_HEADER

cat >> "$ANALYSIS_FILE" << EOF
| 字段 | 值 |
|------|-----|
| 分析 ID | ${ANALYSIS_ID} |
| 模型版本 | ${CURRENT_MODEL_VERSION} |
| 评测模型 | ${MODEL} |
| 评测时间 | ${EVAL_TS} |
| 报告目录 | ${REPORT_DIR} |
| 源文件 | summary.json + API日志 + llama.cpp日志 |

---

## D1 — 格式解析：评测数据概览

> **核心问题**：这次评测输出了什么？

### 总体指标

| 指标 | 值 | 说明 |
|------|-----|------|
| 评测用例数 | ${TOTAL} | 化工合规全量评测集 |
| 工具触发率 | $(python3 -c "print(round(${TOOL_RATE}, 1))" 2>/dev/null || echo "${TOOL_RATE}")% | 正确触发预期工具的比例 |
| 参数准确率 | $(python3 -c "print(round(${PARAM_ACC}, 1))" 2>/dev/null || echo "${PARAM_ACC}")% | 工具参数正确的比例 |
| 结论准确率 | $(python3 -c "print(round(${CONCLUSION_ACC}, 1))" 2>/dev/null || echo "${CONCLUSION_ACC}")% | 最终结论正确的比例 |
| 异常用例 | ${CASES_WITH_ERRORS} | 执行过程中出错的数量 |
| 上次结论准确率 | ${PREV_CONCLUSION} | baseline 对比基准 |

### 工具调用分布

\`\`\`
${TOOL_DIST}
\`\`\`

### 数据来源说明

| 日志文件 | 行数 | 用途 |
|----------|------|------|
| summary.json | — | 评测指标的权威来源 |
| api-eval-window.log | ${API_LINES:-N/A} | API 服务在评测窗口内的日志 |
| llama-llm-tail.log | 200 | llama.cpp LLM 推理服务最近日志 |
| llama-embed-tail.log | 100 | llama.cpp Embedding 服务最近日志 |

---

## D2 — 业务映射：失败模式分类

> **核心问题**：哪些业务场景出错了？对应哪段代码？

EOF

# 按失败模式分类输出
echo "### 失败用例分类" >> "$ANALYSIS_FILE"

jq -r '
def mode_label:
  if .toolMatch == false then "A-未触发工具 (IntentRouter/FC)"
  elif .paramMatch == false then "B-参数错误 (FactExtractor/Assembler)"
  elif .conclusionMatch == false then "C-结论错误 (CheckConclusion/Verifier)"
  else "D-其他" end;

[.cases[]? | select(.toolMatch == false or .paramMatch == false or .conclusionMatch == false)]
| group_by(mode_label)
| sort_by(-length)
| .[] |
"\n#### \(.[0] | mode_label) (\(length) 例)\n",
(.[] | "- `\(.query)` → 期望: `\(.expectedTools[0])`, 实际: `\(.actualTools[0])`")
' "$SUMMARY_FILE" 2>/dev/null >> "$ANALYSIS_FILE"

# 业务模块映射
cat >> "$ANALYSIS_FILE" << 'EOF'

### 失败 → 代码映射表

| 失败模式 | 对应代码模块 | 排查入口 |
|----------|-------------|---------|
| A-未触发工具 | IntentRouter.cs / SK Auto FC | 关键词表 + FunctionCalling 配置 |
| B-参数错误 | FactExtractor.cs / FactAssembler.cs | 事实提取 → 参数组装链路 |
| C-结论错误 | EvalEngine.CheckConclusion() / ReflectionVerifier.cs | GB编号校验 + 结论比对逻辑 |
| D-其他 | AgentDialog.cs / Pipeline 入口 | TraceId 全链路追踪 |

EOF

# ── D3 数据链路 ──
cat >> "$ANALYSIS_FILE" << EOF

---

## D3 — 数据链路：评测流水线追踪

> **核心问题**：数据从哪来、经过哪些步骤、到哪去？

### 评测执行链路

\`\`\`
用户请求 (HTTP POST /api/Eval/run)
  │
  ├─ [1] AuthController.Login → JWT Token
  ├─ [2] EvalController.RunEval → TaskId=${ANALYSIS_ID}
  │     └─ EvalEngine.RunComplianceEvalAsync()
  │         ├─ 遍历 64 条 ComplianceEvalCase
  │         ├─ 每条: AgentDialog.ExecuteAsync()
  │         │   ├─ IntentRouter → 意图分类
  │         │   ├─ SK Auto Function Calling → 工具调用
  │         │   ├─ RAG 检索 (BM25 + Vector + RRF)
  │         │   └─ LLM 推理 (llama.cpp :8080)
  │         └─ ReflectionVerifier → GB 校验 + 结论比对
  │
  └─ [3] summary.json → eval_reports/{date}/
\`\`\`

### API 日志信号

| 信号 | 数量 | 含义 |
|------|------|------|
| Pipeline 日志行 | ${PIPELINE_LINES} | 评测期间处理的请求数 |
| FC 违约 (工具调用=0) | ${FC_VIOLATIONS} | LLM 绕过 Function Calling 直接输出 |
| 错误日志 (ERR/FATAL) | ${API_ERRORS} | 系统级异常 |
| 警告日志 (WRN) | ${API_WARNS} | 降级/非致命异常 |

EOF

# ── D4 设计意图 ──
cat >> "$ANALYSIS_FILE" << 'EOF'

---

## D4 — 设计意图：架构观察

> **核心问题**：为什么当前架构会出现这些结果？设计决策的边界在哪里？

### 当前架构约束

| 约束 | 影响 | 评测体现 |
|------|------|---------|
| Qwen3-8B Q4_K_M 量化 | 推理能力上限 | 结论准确率 baseline |
| IntentRouter 基于关键词 | 召回率 vs 精确率权衡 | A 类失败（未触发工具） |
| FC=Required 但 LLM 不完美遵循 | 工具调用率 < 100% | A 类失败 |
| ReflectionVerifier GB 校验 | 幻觉拦截 | 幻觉率 0% |
| RAG BM25+Vector+RRF 混合检索 | 知识召回质量 | 结论准确率 |

### 架构演进建议

> 💡 以下为 AI 辅助分析占位，需人工确认：

- [ ] **工具触发率优化**：如果 A 类失败 > 5 例，检查 IntentRouter 关键词覆盖率 + FC Prompt 设计
- [ ] **结论准确率优化**：如果 C 类失败 > 3 例，检查 RAG 召回质量 + Reflection 纠错链路
- [ ] **性能基线**：如果单例耗时 > 30s，检查 Prompt 长度 + 模型并发配置

EOF

# ── D5 异常识别 ──
cat >> "$ANALYSIS_FILE" << EOF

---

## D5 — 异常识别：风险信号检测

> **核心问题**：有没有不对劲的？哪些需要立即处理？

### 异常等级分类

| 等级 | 信号 | 当前状态 | 响应 |
|:---:|------|:---:|------|
| **P0** | API 不可用 (Connection refused) | $([ "$API_ERRORS" != "N/A" ] && [ "$API_ERRORS" -gt 0 ] && echo "⚠️ ${API_ERRORS} ERR" || echo "✅ 无") | 立即修复 |
| **P1** | 结论准确率骤降 > 5% | $([ "$PREV_CONCLUSION" != "N/A" ] && python3 -c "exit(0 if ${PREV_CONCLUSION} - ${CONCLUSION_ACC} > 5 else 1)" 2>/dev/null && echo "⚠️ 退化" || echo "✅ 稳定") | 本次周期内修复 |
| **P1** | 工具调用率 < 80% | $([ "$(python3 -c "print(1 if ${TOOL_RATE} < 80 else 0)" 2>/dev/null || echo 0)" = "1" ] && echo "⚠️ ${TOOL_RATE}%" || echo "✅ ${TOOL_RATE}%") | 排查 FC 链路 |
| **P2** | FC 违约次数异常 | $([ "$FC_VIOLATIONS" != "N/A" ] && [ "$FC_VIOLATIONS" -gt 3 ] && echo "⚠️ ${FC_VIOLATIONS} 次" || echo "✅ 正常") | 记录，下周期修复 |
| **P3** | 非致命警告 | $([ "$API_WARNS" != "N/A" ] && [ "$API_WARNS" -gt 5 ] && echo "⚠️ ${API_WARNS} WRN" || echo "✅ 正常") | 无需处理 |

### 告警摘要

$([ -n "$ALERTS" ] && echo "⚠️ ${ALERTS}" || echo "✅ 无质量告警")

EOF

# ── D6 性能分析 ──
cat >> "$ANALYSIS_FILE" << EOF

---

## D6 — 性能分析：耗时与瓶颈

> **核心问题**：快不快？瓶颈在哪？

### 硬件基线

| 组件 | 配置 |
|------|------|
| GPU | NVIDIA RTX 3090 (24GB VRAM) |
| 推理引擎 | llama.cpp (CUDA, -ngl 99) |
| 模型 | Qwen3-8B Q4_K_M (量化) |
| Embedding | nomic-embed-text-v1.5 |

### 评测耗时分析

- 总用例数: ${TOTAL}
- 工具触发: ${TOTAL_TOOL_CALLS} 例（涉及 GPU 推理）
- 未触发工具: ${NO_TOOL_CALLS} 例（纯文本推理）
- 预估单例 LLM 耗时: ~10-15s (RTX 3090 + Qwen3-8B Q4_K_M)

### 优化建议

> 💡 以下为 AI 辅助分析占位：

- [ ] 如果 Pipeline 耗时 > 30s/例，检查 Prompt 长度是否冗余
- [ ] 如果 Embedding 耗时异常，检查 llama-embed 显存配置
- [ ] 建立耗时趋势基线（需 ≥ 3 次评测数据）

EOF

# ── 附录：失败用例详情 ──
echo "" >> "$ANALYSIS_FILE"
echo "---" >> "$ANALYSIS_FILE"
echo "" >> "$ANALYSIS_FILE"
echo "## 附录 A：全量失败用例详情" >> "$ANALYSIS_FILE"
echo "" >> "$ANALYSIS_FILE"

jq -r '
[.cases[]? | select(.toolMatch == false or .paramMatch == false or .conclusionMatch == false)]
| if length == 0 then
    "✅ 所有用例全部通过"
  else
    .[] | 
    "### \(.query)\n\n" +
    "| 维度 | 状态 |\n" +
    "|------|------|\n" +
    "| 工具匹配 | \(if .toolMatch then \"✅\" else \"❌\" end) |\n" +
    "| 参数匹配 | \(if .paramMatch then \"✅\" else \"❌\" end) |\n" +
    "| 结论匹配 | \(if .conclusionMatch then \"✅\" else \"❌\" end) |\n" +
    "| 期望工具 | `\(.expectedTools[0])` |\n" +
    "| 实际调用 | `\(.actualTools[0])` |\n" +
    (if .error then "| 错误信息 | \(.error) |\n" else "" end) +
    "\n"
  end
' "$SUMMARY_FILE" 2>/dev/null >> "$ANALYSIS_FILE"

# ── 附录：日志切片路径 ──
cat >> "$ANALYSIS_FILE" << EOF

---

## 附录 B：日志切片路径对照

| 日志 | 远程路径 | 本地下载命令 |
|------|---------|-------------|
| 分析报告 | ${REPORT_DIR}/analysis.md | 本文档 |
| 评测摘要 | ${REPORT_DIR}/summary.json | base64 下载 |
| API 日志切片 | ${REPORT_DIR}/log_slices/api-eval-window.log | base64 下载 |
| llama LLM 日志 | ${REPORT_DIR}/log_slices/llama-llm-tail.log | base64 下载 |
| llama Embed 日志 | ${REPORT_DIR}/log_slices/llama-embed-tail.log | base64 下载 |

---

> 📖 **关联技能**: [remote-log-analysis.md](../.agents/skills/remote-log-analysis.md)
> 📖 **方法论文档**: [engineering-deep-learning-methodology.md](../.agents/skills/engineering-deep-learning-methodology.md)
> 📖 **测试总纲**: [测试总纲.md](../docs/testing/测试总纲.md)
> 📖 **Bug知识库**: [Bug知识库.md](../docs/project/Bug知识库.md)

> ⚠️ **D2-D4 中的 AI 辅助分析占位项需人工确认后填写**，完整六维度分析见 remote-log-analysis 技能的一问一答交互流程。

EOF

# ═══════════════════════════════════════
# Step 7: 输出摘要
# ═══════════════════════════════════════
echo ""
echo "=== [7/7] Analysis complete ==="
echo "  Report: ${ANALYSIS_FILE}"
echo "  Log slices: ${LOG_SLICE_DIR}/"
echo ""
echo "╔══════════════════════════════════════════╗"
echo "║  Download with SshRunner base64:         ║"
echo "║  SshRunner.exe HOST PORT root PWD \"cat ${ANALYSIS_FILE}\"  ║"
echo "╚══════════════════════════════════════════╝"
