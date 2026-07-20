# Function Calling 模型评测 BUG 记录

> **文档版本**：v1.1（深度扩展版 — 2026-06-24）
> **关联修复文档**：[P0-P1修复详细技术文档](troubleshooting/P0-P1修复详细技术文档.md) | [RAG工程Bug修复笔记](troubleshooting/RAG工程Bug修复笔记_2026-05-26.md)

> **日期**：2026-06-03  
> **来源**：`full-20260603.log` 第 6~8 次会话评测执行  
> **涉及文件**：`LlmService.cs`, `Program.cs`, `AgentDialog.cs`, `ChemicalComplianceTools.cs`  
> **严重等级**：🔴 影响生产安全判定

---

## 一、BUG 总览

| # | BUG | 严重等级 | 影响范围 |
|---|-----|:------:|---------|
| 1 | 超时后 `LastFunctionCalls` 残留（幽灵工具数据） | 🔴 严重 | 评测指标虚高 + 生产环境 Reflection 验证层数据污染 |
| 2 | `firstAttemptToolResults` 超时时永远为 null | 🔴 严重 | 重试降级策略（内联工具结果）完全失效 |
| 3 | `CheckConclusion` 仅做关键词浅层匹配 | 🔴 严重 | 虚假合规判定（false positive/negative） |
| 4 | 非流式 + RAG 全链路单次调用必超时 | 🟡 中等 | 每条用例需 3 次重试共 ~15 分钟 |
| 5 | 重试间隔仅 2 秒，Ollama 服务端未释放 | 🟡 中等 | 加剧后续重试超时 |

---

## 二、BUG 详细分析

### BUG #1：超时后 `LastFunctionCalls` 残留 — "幽灵工具数据"

**位置**：[`LlmService.cs` L308-L388](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L308-L388)  
**触发条件**：非流式调用在第1次 attempt 中，SK 完成工具调用但 LLM 文本生成阶段超时。

#### 时间线

```
T0: LastFunctionCalls.Clear()                    ← 第1次 attempt 开始，清空
T1: InvokePromptAsync 开始                       ← SK Auto FC 模式
T2: CheckHazardCategory 被调用，RAG 检索完成     ← FunctionCallDiagnosticsFilter 写入 LastFunctionCalls
T3: LLM 基于 RAG 结果生成最终文本                 ← 大 Prompt + CPU 推理 → 超过 5 分钟
T4: OperationCanceledException 抛出              ← 第1次超时
T5: 进入 catch 块，重试第2次 (FC=null)           ← LastFunctionCalls 未被清空！
T6: 第2次又超时
T7: 第3次重试 (FC=null) 完成                     ← 纯 LLM 文本生成，无工具调用
T8: ExecuteEvalFastAsync 返回
```

#### 评测器检查时的数据错位

评测器（[`Program.cs` L567-L570](file:///d:/桌面/agent/项目/Agent1/Agent1/Program.cs#L567-L570)）读取的 `LastFunctionCalls`：

```csharp
if (llmSvc != null && llmSvc.LastFunctionCalls.Count > 0)
{
    var calledTools = llmSvc.LastFunctionCalls.Select(fc => fc.FunctionName).ToList();
    // ← 这里读到的是 T2 时刻（第1次 attempt）的 Stale 数据！
```

而 `response` 是 T7 时刻（第3次 attempt，无工具调用）的纯 LLM 生成文本。

**结果**：评测器认为"工具触发了 → 结论基于工具数据"，但实际 LLM 回复与工具调用**完全脱钩**。

#### 生产安全风险

`LastFunctionCalls` 和 `LastToolResults` 同时被生产环境的 [ReflectionVerifier.cs](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/ReflectionVerifier.cs) 读取用于反思验证。如果同样发生超时并被污染：

- 验证层在 **错误/空洞的工具数据** 上做业务事实核查
- 可能通过一个虚假的合规结论（"未发现直接冲突 → 可以储存"）
- 造成真实的生产安全风险

---

### BUG #2：`firstAttemptToolResults` 超时时永远为 null

**位置**：[`LlmService.cs` L334-L341](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L334-L341)  
**触发条件**：第1次 attempt 超时。

#### 代码流程

```csharp
// attempt == 1
LastFunctionCalls.Clear();                              // ← 清空
var kernelResult = await _kernel.InvokePromptAsync(     // ← 抛出 OperationCanceledException
    prompt, new KernelArguments(settings), cancellationToken: cts.Token);
firstAttemptToolResults = new List<FunctionCallRecord>(LastFunctionCalls);  // ← 永远执行不到
return kernelResult.ToString();                         // ← 永远执行不到
```

因为 `InvokePromptAsync` 抛出异常 (L336)，L340 永远无法执行，`firstAttemptToolResults` 保持为 `null`。

#### 连锁影响：重试降级策略失效

重试分支（[L344-L368](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L344-L368)）：

```csharp
// attempt >= 2
if (firstAttemptToolResults != null && firstAttemptToolResults.Count > 0)
{
    // ← 永远进不来！firstAttemptToolResults 始终是 null
    retryPrompt = prompt + "\n\n【已获取的工具调用结果，请直接据此回答】\n" + toolResultsText;
}
// 所以 retryPrompt 始终 = 原始 prompt，没有任何工具结果
// 重试变得和首次调用几乎一样重，同样会超时
```

**设计意图 vs 实际效果**：

| | 设计意图 | 实际效果 |
|---|---------|---------|
| 第1次 | FC+RAG 全链路 | FC+RAG 全链路 → ⏰超时 |
| 第2次 | FC禁用 + 工具结果内联 → 轻量文本生成 | FC禁用 + 无工具结果 → 仍超时 |
| 第3次 | 同第2次 | 同第2次 → 偶尔完成（纯 LLM 编造） |

#### 修复方向

在 `catch (OperationCanceledException)` 块中捕获工具结果：

```csharp
catch (OperationCanceledException ex)
{
    // 即使超时，如果工具已被调用，保留结果供重试内联
    if (LastFunctionCalls.Count > 0 && firstAttemptToolResults == null)
    {
        firstAttemptToolResults = new List<FunctionCallRecord>(LastFunctionCalls);
    }
    // ...
}
```

---

### BUG #3：`CheckConclusion` 关键词浅层匹配 — "虚假安全带"

**位置**：[`Program.cs` L715-L724](file:///d:/桌面/agent/项目/Agent1/Agent1/Program.cs#L715-L724)  
**触发条件**：所有评测用例的结论检查。

#### 当前实现

```csharp
static bool CheckConclusion(string? response, EvalConclusion? expected)
{
    if (expected.is_compliant)
        return respLower.Contains("合规") || respLower.Contains("允许") || respLower.Contains("可以");
    else
        return respLower.Contains("不合规") || respLower.Contains("不允许") || respLower.Contains("禁止") || respLower.Contains("严禁");
}
```

#### 假阳性场景 1："可以"的多义性

工具 fallback 回复：
> "「过氧化氢」与「丙酮」在常见禁忌表中未发现直接冲突，因此通常**可以**同库储存。不过，建议您查阅标准全文..."

- 预期：`is_compliant=false`（氧化剂 + 易燃液体，严禁同库）
- `CheckConclusion` 看到 **"可以"** → 误判 `is_compliant=true`
- **后果**：将危险的储存方案判定为合规

#### 假阳性场景 2：LLM 编造的合规表述

超时后第3次重试（无工具数据），LLM 凭空生成：
> "根据 GB 30000 系列标准，过氧化氢属于氧化性液体，**合规**储存要求..."

- 预期：`is_compliant=true`（仅查询类别，不涉及合规判断）
- `CheckConclusion` 看到 **"合规"** → 判定通过
- 实际上这个结论是 LLM **没有依据**编造的

#### 假阴性场景：否定词的上下文依赖

> "如果未采取隔离措施，则**不允许**同库储存"

- 预期：`is_compliant=true`（有条件合规）
- `CheckConclusion` 看到 **"不允许"** → 误判 `is_compliant=false`

#### 修复方向

将结论检查从 LLM 文本匹配改为**工具返回值的结构化判定**：

1. 工具返回结构化 JSON 而非自然语言文本
2. 评测器直接读取工具返回值中的 `is_compliant` 字段
3. 仅在工具未触发时，才用 LLM 文本做兜底判断

---

### BUG #4：非流式 + RAG → 单次调用必超时

**位置**：[`LlmService.cs` L308-L388](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L308-L388)  
**触发条件**：`qwen3:8b-eval` 非流式模式 + RAG 启用。

#### 超时链路

```
InvokePromptAsync (FC=Auto)
  ↓
CheckHazardCategory 触发
  ↓
RAG: BM25 检索 (12条) + 向量检索 (12条) + 混合融合 → 耗时 ~2-3秒
  ↓
RAG 结果格式化 (Markdown, 3条结果 × ~200字/条) → 注入到 Prompt
  ↓
LLM 分析 + 生成最终文本 → Prompt 膨胀到 ~1500 tokens
  ↓
CPU 推理 (qwen3:8b, num_ctx=4096) → 单条生成时间 >5 分钟
  ↓
⏰ OperationCanceledException
```

#### 为什么第3次有时能成功

第3次重试时 FC 已禁用，LLM 收到的是原始 Prompt（无 RAG 结果嵌入），token 数少500-800，推理时间相应缩短，刚好在5分钟边界内完成。但生成的内容与工具数据无关。

#### 修复方向

1. **短期**：降低 `num_ctx` 至 2048，减少单次推理计算量
2. **短期**：RAG 结果精简（取 top 1 而非 top 3，截断内容至200字）
3. **长期**：换用 GPU 推理或更大参数模型（以空间换时间）

---

### BUG #5：重试间隔过短（2秒）

**位置**：[`LlmService.cs` L22](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L22) + [L375](file:///d:/桌面/agent/项目/Agent1/Agent1/Services/LlmService.cs#L375)

```csharp
private const int RetryDelayMs = 1000;
// ...
await Task.Delay(RetryDelayMs * 2);  // = 2000ms
```

客户端 `CancellationTokenSource.Cancel()` 只取消了 .NET 侧的 HTTP 请求，**Ollama 服务端的推理线程并未中止**。2 秒后重试时：
- 服务端仍在处理前一个请求
- 新请求进入 Ollama 的请求队列排队
- 排队时间 + 推理时间 > 5 分钟 → 再次超时

#### 修复方向

超时后等待 **10-15 秒**，给 Ollama 足够时间清理上一个请求。

---

## 三、问题关联图

```
┌──────────────────────────────────────────────────────────────────┐
│                    qwen3:8b-eval 非流式评测                          │
│                                                                  │
│  第1次 attempt: FC+RAG 全链路                                     │
│     │                                                             │
│     ├──→ RAG 检索成功 │ 工具调用成功 (写入 LastFunctionCalls)       │
│     │                                                             │
│     └──→ LLM 文本生成超时 ───→ OperationCanceledException          │
│              │                                                    │
│              ├──→ BUG #1: LastFunctionCalls 未清空 (Stale)        │
│              ├──→ BUG #2: firstAttemptToolResults = null          │
│              └──→ BUG #5: 仅等2秒就重试                            │
│                                                                  │
│  第2次 attempt: FC=null, 无工具结果内联 (因BUG #2)                  │
│     └──→ 依然超时                                                 │
│                                                                  │
│  第3次 attempt: FC=null, 纯 LLM 生成                               │
│     └──→ 偶尔完成 (Prompt更短, 推理更快)                            │
│              │                                                    │
│              └──→ 返回无依据的文本                                 │
│                                                                  │
│  评测器检查:                                                       │
│     │                                                             │
│     ├── 读到 BUG #1 的 Stale LastFunctionCalls → "工具触发✅"     │
│     ├── 读到第3次 attempt 的纯LLM文本                              │
│     └── BUG #3: CheckConclusion 关键词匹配 → "结论✅"             │
│                                                                  │
│  ⚠️ 评测结果: 工具✅ 参数✅ 结论✅                                  │
│  ⚠️ 实际:    工具超时 | 文本无依据 | 结论可能是错的                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 四、修复优先级

| 优先级 | BUG | 修复方案 | 预计工作量 |
|:---:|-----|---------|:---:|
| **P0** | #1 Stale Tool Data | 每次 attempt 前清空 `LastFunctionCalls`，或将记录按 attempt 隔离 | 0.5h |
| **P0** | #2 firstAttemptToolResults | `catch` 块中捕获并保存工具结果 | 0.5h |
| **P0** | #3 CheckConclusion | 工具返回结构化 JSON + 评测器按字段匹配 | 2h |
| **P1** | #4 非流式超时 | 降低 `num_ctx` + 精简 RAG 结果 + 增加超时边界 | 1h |
| **P1** | #5 重试间隔 | `RetryDelayMs` 延长至 10000ms | 5min |

---

## 五、经验教训

1. **评测框架不能信任被评测对象的状态**。`LastFunctionCalls` 是 LLM 服务层的可变状态，评测器不应直接依赖它来进行工具触发判断。

2. **关键词匹配是"虚假的安全感"**。在合规审核这种高风险场景中，"可以"/"合规" 的多义性足以让整个评测指标丧失参考价值。

3. **超时不是失败，而是"部分成功"**。工具已调用、RAG 已检索，只是最后一步文本生成没完成。这种半成功状态需要特殊处理，而不是当作普通的"失败重试"。

4. **重试设计需要考虑服务端状态**。客户端取消请求 ≠ 服务端停止推理，重试间隔必须给服务端足够的清理时间。

---

## 六、修复实施记录 (v1 · 2026-06-03)

> **状态**：第一次修复方案已落地，待评测验证。不代表一定成功，但作为宝贵的总结经验记录下来。

### 6.1 修改文件清单

| 文件 | 修改内容 | 涉及 BUG |
|------|---------|:---:|
| `Agent1/Services/LlmService.cs` | `RetryDelayMs` 1000→5000 | #5 |
| `Agent1/Services/LlmService.cs` | `InvokeNonStreamingWithRetryAsync` — attempt==1 超时 5→8 分钟 | #4 |
| `Agent1/Services/LlmService.cs` | `InvokeNonStreamingWithRetryAsync` — catch 块捕获 `firstAttemptToolResults` | #2 |
| `Agent1/Services/LlmService.cs` | `InvokeNonStreamingWithRetryAsync` — catch 块清空 `LastFunctionCalls` + 成功路径恢复 | #1 |
| `Agent1/Program.cs` | 新增 `using System.Text.RegularExpressions` | #3 |
| `Agent1/Program.cs` | `CheckConclusion` 重写：优先解析 `[判定:is_compliant=...]` 标签 + 改良关键词排除误报 | #3 |
| `Agent1/ChemicalComplianceTools.cs` | `CheckStorageCompatibilityFallback` 返回值追加 `[判定:is_compliant=...]` 标签 | #3 |

### 6.2 各修复详细变更

#### P0-2: firstAttemptToolResults 超时捕获

**文件**：`LlmService.cs` → `catch (OperationCanceledException ex)` 块（L370-L376）

**新增代码**：
```csharp
// [P0-2 FIX] 即使超时，工具也可能已成功调用，捕获结果供重试内联
if (attempt == 1 && LastFunctionCalls.Count > 0 && firstAttemptToolResults == null)
{
    firstAttemptToolResults = new List<FunctionCallRecord>(LastFunctionCalls);
    Console.WriteLine($"   💾 已保存 {firstAttemptToolResults.Count} 个工具结果供重试用");
}
```

**预期效果**：第1次 attempt 超时后，第2-3次重试的 prompt 中将包含工具调用结果内联段落，使降级策略真正生效。

**不确定性**：`OperationCanceledException` 抛出时 `LastFunctionCalls` 是否已完整填充取决于 SK 内部的 `FunctionInvocationFilter` 是否已完成写入。若异常先于 filter 触发，捕获的列表可能不完整。

---

#### P0-1: LastFunctionCalls 清空+恢复

**文件**：`LlmService.cs` → `catch (OperationCanceledException ex)` 和 `catch (Exception ex)` 块 + attempt≥2 成功路径

**新增代码**（catch 块）：
```csharp
// [P0-1 FIX] 清空残留工具记录，防止评测器读到 stale 数据
LastFunctionCalls.Clear();
```

**新增代码**（attempt≥2 成功返回前）：
```csharp
// [P0-1 FIX] 恢复工具调用记录，表明最终回答基于内联的工具数据
if (firstAttemptToolResults != null && firstAttemptToolResults.Count > 0)
    LastFunctionCalls = firstAttemptToolResults;
```

**预期效果**：
- 若无内联工具数据 → `LastFunctionCalls` 为空 → 评测器正确判定"无工具调用"
- 若有内联工具数据 → `LastFunctionCalls` 恢复为 attempt 1 的工具记录 → 评测器正确判定"工具已触发"

**语义变更**：`LastFunctionCalls` 从"调用是否触发工具"变为"最终回答基于哪个 attempt 的工具数据"。`ReflectionVerifier.cs` 读 `LastFunctionCalls` 时可能受影响，若 Reflection 也走非流式路径需确认兼容性。

---

#### P0-3: CheckConclusion 结构化判定

**文件**：`Program.cs` L714-L755 (原L714-L724)

**新逻辑三级优先级**：

```
Level 1: 解析 [判定:is_compliant=true/false] 标签 → 直接比对
Level 2 (fallback): 改良关键词匹配
  ├── is_compliant=true: 含正向词(合规/允许/可以) AND 不含排除词(不建议/建议查阅/未发现直接冲突)
  └── is_compliant=false: 含负向词(禁止/严禁/禁忌) AND 不含条件句标志(如果/则/当)
```

**文件**：`ChemicalComplianceTools.cs` → `CheckStorageCompatibilityFallback`

两个返回值路径末尾追加：
- 禁忌路径：`[判定:is_compliant=false]`
- 未冲突路径：`[判定:is_compliant=true]`

**风险提醒**：
1. `[判定:is_compliant=true]` 标签会出现在 LLM 看到的工具返回值中。若 LLM 在生成最终结论时原样复制此标签附近的文本，可能影响结论措辞的自然度。
2. RAG 检索路径（`FormatRagResult`）不添加标签，因为知识库原文不含合规判定。当 RAG 可用时，`CheckConclusion` 只能回退到 Level 2 关键词匹配。
3. 改良版关键词排除规则（`hasCaveat`/`hasConditional`）基于观察到的误报模式提炼，可能有未覆盖的边界情况。

---

#### P1-1: 第1次 attempt 超时容限

**文件**：`LlmService.cs` L317

```csharp
// Before:
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

// After:
var timeoutMinutes = attempt == 1 ? 8 : 5;
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
```

**预期效果**：第1次 attempt 从 5 分钟延长至 8 分钟，给 FC+RAG 全链路更多完成时间。第2-3次保持 5 分钟（FC 已禁用，Prompt 更短）。

**待验证**：8 分钟在 CPU 推理环境下是否足够完成全链路。若仍超时，需结合 P0-2 确保降级策略生效。

---

#### P1-2: 重试间隔延长

**文件**：`LlmService.cs` L22

```csharp
// Before:
private const int RetryDelayMs = 1000;

// After:
private const int RetryDelayMs = 5000;  // 实际 delay = 10000ms
```

**预期效果**：每次重试间隔从 2 秒延长到 10 秒，给 Ollama 服务端足够时间清理被取消的请求。50条用例总计增加约 8 分钟延迟。

---

### 6.3 待验证项

| # | 验证内容 | 验证方式 |
|---|---------|---------|
| 1 | P0-2: 降级策略是否生效（重试 prompt 含工具结果） | 日志中应出现 `💾 已保存 N 个工具结果供重试用` |
| 2 | P0-1: 评测器不再读到 stale LastFunctionCalls | 评测日志中 `actual_tools` 与 `actual_response` 应来自同一 attempt |
| 3 | P0-3: D004（过氧化氢+丙酮）正确判定 `is_compliant=false` | 评测结果 `conclusion_match=true` |
| 4 | P1-1: 第1次 attempt 在 8 分钟内完成率是否提升 | 观测 `⏰ 请求超时` 日志减少 |
| 5 | P0-1: ReflectionVerifier 兼容性 | Reflection 模式下不出现空引用或异常 |
| 6 | P0-3: LLM 措辞是否被 `[判定:...]` 标签影响 | 抽查 LLM 输出文本自然度 |

### 6.4 回滚方案

若修复引入新问题：
1. `git diff` 确认本次变更仅限于上述 3 个文件
2. `git checkout -- Agent1/Services/LlmService.cs Agent1/Program.cs Agent1/ChemicalComplianceTools.cs` 一键回滚
3. 若需保留部分修复，可单独回退某个文件

---

## 七、修复实施记录 (v1.1 · 2026-06-03) — 标签覆盖补充 + 思考模式控制

> **状态**：v1 修复方案补充，增加标签全覆盖 + Qwen3 Thinking 模式 API 级控制

### 7.1 修改文件清单

| 文件 | 修改内容 | 涉及 |
|------|---------|:---:|
| `Agent1/ChemicalComplianceTools.cs` | `CheckHazardCategoryFallback` → `[判定:is_compliant=unknown]` | 标签覆盖 |
| `Agent1/ChemicalComplianceTools.cs` | `GetSafetyDistanceFallback` → `[判定:is_compliant=待核实]` | 标签覆盖 |
| `Agent1/ChemicalComplianceTools.cs` | `FormatRagResult` → `[判定:is_compliant=依据原文]` | 标签覆盖 |
| `Agent1/appsettings.json` | `EvalFastPrompt` 要求 LLM 输出 `[判定:is_compliant=true/false]` | 标签覆盖 |
| `Agent1/Program.cs` | `CheckConclusion` 匹配扩展至 5 种标签 | 标签覆盖 |
| `Agent1/Services/LlmService.cs` | 新增 `EnableThinking` 属性 + `OllamaThinkingHandler` + `InjectThinkingHandler` | Thinking 控制 |
| `Agent1/Services/LlmService.cs` | `InvokeStreamWithRetryAsync` 临时启用 Thinking | Thinking 控制 |

### 7.2 标签覆盖全景

```
工具返回值路径                    → 标签                          → CheckConclusion 行为
─────────────────────────────────────────────────────────────────────────────
CheckStorageCompatibilityFallback → [判定:is_compliant=true]     → 直接比对
  (未冲突)                          [判定:is_compliant=false]    → 直接比对
  (禁忌)
CheckHazardCategoryFallback       → [判定:is_compliant=unknown]  → return false (不比对)
GetSafetyDistanceFallback         → [判定:is_compliant=待核实]    → return false (不比对)
FormatRagResult (RAG检索成功)      → [判定:is_compliant=依据原文]   → return false (不比对)
LLM 最终回答 (EvalFastPrompt要求)  → [判定:is_compliant=true/false] → 直接比对
纯LLM文本(无标签)                  → 回退关键词匹配（改良版）        → 关键词匹配
```

### 7.3 Qwen3 Thinking 模式控制

#### 问题
Qwen3 默认启用 Thinking 模式，生成大量 `<think>...</think>` token，在 CPU 推理下严重加剧超时。

#### 解决方案
1. `OllamaThinkingHandler`（DelegatingHandler）：拦截 SK → Ollama 的 `/api/chat` POST 请求，在 JSON body 中注入 `"enable_thinking": false/true`
2. `EnableThinking` 属性：默认为 `false`（FC/评测/对话用非Thinking快速响应）
3. `InvokeStreamWithRetryAsync` 临时切换为 `true`（ReAct/Reflection/CoT 用深度推理）
4. `InjectThinkingHandler`：通过反射访问 SK 内部 `OllamaChatCompletionService._client._httpClient` 并替换 handler

#### 注入方式
由于 SK 1.74.0-alpha 的 `AddOllamaChatCompletion` 不支持自定义 HttpClient，通过反射链路 `OllamaChatCompletionService → _client (OllamaMessageClient) → _httpClient` 注入 handler。若反射失败（如 SK 版本升级后字段名变更），会输出警告并降级为不注入（不影响程序运行）。

#### Thinking 模式策略表

| 调用路径 | 方法 | Thinking | 原因 |
|---------|------|:---:|------|
| FC / 评测 | `InvokeNonStreamingWithRetryAsync` | OFF | 速度与可控性优先 |
| ReAct / Reflection / CoT | `InvokeStreamWithRetryAsync` | ON | 深度推理需要思考 |
| 直接对话 | `InvokeStreamAsync` | OFF | 速度优先 |

### 7.4 新增待验证项

| # | 验证内容 | 验证方式 |
|---|---------|---------|
| 7 | Thinking handler 注入是否成功 | 启动日志出现 `✅ [Thinking] OllamaThinkingHandler 已注入` |
| 8 | FC/评测场景响应速度是否提升（无 `<think>` token） | 对比修复前后单条用例耗时 |
| 9 | ReAct/Reflection 场景 Thinking 是否正常开启 | 模块输出中可见 `<think>` 推理过程 |
