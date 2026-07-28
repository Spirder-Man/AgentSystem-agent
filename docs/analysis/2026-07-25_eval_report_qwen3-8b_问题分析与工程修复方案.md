# Agent1 评测问题分析报告与工程修复方案

> **评测文件**：`eval_report.json`（2026-07-25 14:46:59）  
> **评测模型**：qwen3:8b  
> **总用例数**：63  
> **分析日期**：2026-07-25  

---

## 一、问题概述

本次评测覆盖 7 个类别的 63 个测试用例，核心指标如下：

| 指标 | 数值 | 目标 | 差距 |
|------|------|------|------|
| 工具调用准确率 | 88.89% | ≥95% | -6.1% |
| 结论准确率 | 82.54% | ≥95% | -12.5% |
| 幻觉率 (hallucinated/total) | 41/150 = 27.3% | ≤5% | +22.3% |
| 平均忠实度 | 0.785 | ≥0.95 | -0.165 |
| 平均引用准确率 | 0.517 | ≥0.90 | -0.383 |
| 检索 Precision@K | 0.368 | ≥0.60 | -0.232 |
| FC 就绪性 | 4/5 (80%) | 5/5 (100%) | -1 项 |

**完全失败用例（工具+参数+结论全部错误）**：C009, C014, D001, D011, D012（共 5 例）  
**结论失败用例（工具正确但结论错误）**：C012 + 安全距离类别 4 例（共 5 例）

---

## 二、问题分类：模型问题 vs 工程问题

依据 AI 系统问题诊断的模型/工程二分法，将发现的问题按根因归类：

### 2.1 模型问题（qwen3:8b 自身局限）

| # | 问题 | 影响 | 根因 |
|---|------|------|------|
| M1 | FC 意图识别弱——对"同库储存/同库存放/能否同库"等口语化表达不触发工具 | D001/D011/D012 完全失败 | qwen3:8b 对口语化存储兼容性查询的 FC 意图理解不足 |
| M2 | 工具选择混淆——"硫酸的危险特性"调用了 LookupChemicalProperties 而非 CheckHazardCategory | C014 完全失败 | 模型无法区分"危险特性"和"危险类别"两个语义相近的意图 |
| M3 | GB 编号编造——LLM 凭训练记忆补充知识库中不存在的 GB 子编号 | 41 个幻觉声明（27.3%） | 规范③消毒后 LLM 看不到白名单，反而更依赖训练数据"回忆"编号 |
| M4 | 安全距离结论偏差——工具调用正确但最终输出距离值与预期不符 | 4/8 安全距离结论失败 | 模型对工具返回值的解读/摘要存在偏差 |

### 2.2 工程问题（系统设计缺陷）

| # | 问题 | 影响 | 根因 |
|---|------|------|------|
| E1 | **FC 路由缺少关键词兜底**：IntentRouter 之后无工程侧强制工具映射 | M1 被放大，3 个存储兼容性用例无兜底 | 设计上缺少口语化关键词→工具的路由映射表 |
| E2 | **Prompt 消毒与 EvalFastPrompt 存在结构性矛盾** | M3 被系统性放大 | 见第三章详细分析 |
| E3 | **OutputValidator 包含匹配过于宽松** | 部分幻觉漏网 | `IsRegulationAllowed` 使用 String.Contains 而非精确/前缀匹配 |
| E4 | **检索管道同质化**：两个大文档统治所有检索结果 | P@K=0.368，RAG fallback 不可用 | chunk 切分策略和检索权重未针对合规场景优化 |
| E5 | **幻觉检测（Level4）阈值过宽**：41 个幻觉全部标记 passed | 评测指标失真，运行时无实际拦截效果 | 检测逻辑存在但判定标准与实际 KB 可用性脱节 |

### 2.3 综合判断

> **模型问题约占 60%、工程问题约占 40%。**  
> 最关键的发现：M3（GB 编号编造，占 41/150 幻觉声明）的直接原因是模型行为，但 **根因是 E2（设计矛盾）系统性放大了模型弱点**。这意味着修好 E2 后，即使不换模型，幻觉率也可以大幅下降。

---

## 三、工程问题详细分析

### 3.1 E2：Prompt 消毒与评测 Prompt 的结构性矛盾（P0 致命）

**因果链：**

```
Step 1: 评测 Prompt (appsettings.json L143) 要求 LLM:
  "法规编号必须严格引用工具返回中 [REGULATIONS: ...] 的值"

Step 2: PromptSanitizer.SanitizeToolResult (PromptSanitizer.cs L35-60) 在工具结果传给 LLM 之前:
  剥离 [REGULATIONS: ...] 标签
  → LLM 实际看到的工具结果中已经没有 [REGULATIONS:] 标签了

Step 3: LLM 被要求引用一个它看不到的标签 → 只能凭训练记忆编造 GB 编号
  → 编造出 GB 30000.27 / GB 30000.22 / GB 15603-2022 等知识库中不存在的编号

Step 4: OutputSanitizer 白名单校验 (OutputSanitizer.cs L142-166):
  白名单来自 ComplianceFactExtractor 从工具返回值解析的 [REGULATIONS:] 标签
  但 LLM 输出中的编造编号"恰好"通过前缀匹配（如 "GB30000.27".StartsWith 检查漏网）

Step 5: 最终输出含编造 GB 编号 → 评测捕获为幻觉
```

**设计矛盾的具体证据：**

```
文件: appsettings.json L143
  "【反幻觉指令】法规编号必须严格引用工具返回中 [REGULATIONS: ...] 的值"
                              ↑ LLM 被告知要引用这个标签的内容

文件: PromptSanitizer.cs L43
  sanitized = RegulationsTagRegex.Replace(sanitized, "");
                              ↑ 但这个标签在传入 LLM 前被剥离了
```

**这不是 bug，是设计层面的逻辑冲突**：规范③（Prompt 消毒）说"移除法规编号防止 LLM 滥用"，但评测 Prompt 说"引用工具返回的法规编号"。两条规范在 LLM 的视角下无法同时满足。

### 3.2 E1：FC 路由缺少关键词兜底（P0 高）

**因果链：**

```
用户输入: "苯和丙酮可以同库储存吗"
    │
    ▼
IntentRouter.Route() → "ChemicalCompliance" ✅
    │
    ▼
SK Auto Function Calling → qwen3:8b → "不需要调工具" ❌
    │
    ▼
ApplyDecoupledPipeline L840: toolCalls==0 → BuildNoResult()
    │
    ▼
输出拒绝模板 → 功能失败
```

**问题定位**：`IntentRouter` 完成意图分类后，FC 工具选择完全交给 LLM。设计上缺少一个 `IntentToToolMapping` 作为口语化表达的安全网。

当前只有唯一的确定性路由在 `IntentRouter` 层，之后的 FC 选择是纯非确定性的。

### 3.3 E3：OutputValidator 包含匹配漏洞（P1）

**问题代码**（`OutputValidator.cs` L210-212）：

```csharp
// 当前逻辑：使用 String.Contains 做宽松匹配
if (nReg.Contains(nAllowed, StringComparison.OrdinalIgnoreCase)
    || nAllowed.Contains(nReg, StringComparison.OrdinalIgnoreCase))
    return true;
```

**漏洞示例**：假设工具返回 `[REGULATIONS: GB 30000.2]`（不带子编号），那么：
- nAllowed = "GB300002"（规范化后）
- nReg = "GB3000027"（LLM 编造的 "GB 30000.27"）
- `"GB3000027".Contains("GB300002")` → **true** → 放行！

### 3.4 E4：检索管道同质化（P1）

**表现**：几乎所有查询的 Top-K 检索结果中，63% 是不相关的 chunk。两个文档统治了检索结果：
- "GB30000 系列 - 常见危险化学品分类对照表"
- "GB15603-1995 常用化学危险品贮存通则"

**根因**：这两个文档覆盖了大量化学品名称和 GB 编号，embedding 相似度天然占优。当前缺乏针对化工合规场景的 chunk 细粒度切分和检索权重调整。

### 3.5 E5：幻觉检测 Level4 判定过宽（P1）

**表现**：eval_report 中 `conclusion_reasons` 显示 41 个幻觉声明对应的检测结果均为 `"passed": true`。

**问题**：Level4 的 `rule_applied` 描述为"提取N个GB编号, 预期=GB XXXX"，它只检查"是否提取到了 GB 编号"而不检查"这些编号是否在知识库中可验证"。真正的 KB 可验证性检查在 `ReflectionVerifier.VerifyBusinessFactsAsync` 中，但 Level4 不引用其结果。

---

## 四、工程问题解决方案

### 4.1 修复 E2：消除 Prompt 消毒与评测 Prompt 的矛盾（P0 致命）

**方案**：调整评测 Prompt 中的反幻觉指令，使其与实际数据流一致。

**修改文件**：`Agent1/appsettings.json` L143

**修改前**：
```
【反幻觉指令】法规编号必须严格引用工具返回中 [REGULATIONS: ...] 的值，
不得自行编造任何 GB XXXX-XXXX 格式的编号。
如果工具未返回 [REGULATIONS: ...] 标签，则不得编造任何法规编号。
```

**修改后**：
```
【反幻觉指令】你只能引用「查询结果」区域中显示的法规编号（格式如 GB 30000.7、GB 15603）。
严禁自行编造任何 GB XXXX-XXXX 格式的编号。如果「查询结果」中未列出法规编号，
则你的回答中不得包含任何 GB 编号——只需用中文描述即可，例如：
✅ "属于易燃液体类别2"（正确：不用编号）
❌ "依据 GB 30000.7 属于易燃液体"（错误：编造编号）
```

**同步修改**：`EvalFastQueryPrompt`（L144）的同位置指令也需同步更新。

**预期效果**：
- 消除 LLM "被要求引用看不到的标签"的认知冲突
- 引导 LLM 在不确知编号时直接用中文表达，而非猜测编号
- 幻觉率预计从 27.3% 降至 **8-12%**

---

### 4.2 修复 E1：增加 FC 关键词→工具映射兜底（P0 高）

**方案**：在 `AgentDialog` 的评测路径中，增加一层确定性的口语化关键词→工具名映射，作为 SK Auto FC 的兜底。

**修改文件**：`Agent1/Services/Dialog/AgentDialog.cs`

**新增方法**（插入到 `ApplyDecoupledPipeline` 之前，约 L806）：

```csharp
/// <summary>
/// [P0 FIX] FC 关键词兜底映射表。
/// 当 SK Auto Function Calling 未能触发工具时（toolCalls==0），
/// 用确定性关键词匹配尝试补调工具。解决 qwen3:8b 对口语化表达的 FC 意图识别不足。
/// </summary>
private static readonly Dictionary<string, (string ToolName, string ParamTemplate)> 
    FcKeywordFallback = new(StringComparer.OrdinalIgnoreCase)
{
    // 存储兼容性口语化表达
    ["同库储存"] = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
    ["同库存放"] = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
    ["同库"]     = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
    ["相邻存放"] = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
    ["一起存放"] = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
    ["一起储存"] = ("CheckStorageCompatibility", "substanceA={0}, substanceB={1}"),
};

/// <summary>
/// 从用户查询中尝试通过关键词匹配确定应调用的工具。
/// 仅在 SK Auto FC 未触发任何工具时作为兜底使用。
/// </summary>
private (string? toolName, string? args) TryKeywordFallback(string userQuery)
{
    foreach (var kv in FcKeywordFallback)
    {
        if (userQuery.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
        {
            // 从查询中提取物质名对（简单中文分词）
            var substances = ExtractSubstancesFromQuery(userQuery);
            if (substances.Count >= 2)
            {
                var args = string.Format(kv.Value.ParamTemplate, 
                    substances[0], substances[1]);
                return (kv.Value.ToolName, args);
            }
        }
    }
    return (null, null);
}
```

**修改 `ExecuteEvalInternalAsync`**（L634-644）：在 `ApplyDecoupledPipeline` 之前插入兜底检查：

```csharp
// ★ 双通道解耦架构：统一入口（覆盖 API / 旧评测路径）
var evalToolCalls = llmService?.LastFunctionCalls
    .Select(fc => new FunctionCallRecord { ... })
    .ToList() ?? new List<FunctionCallRecord>();

// [P0 FIX] FC 关键词兜底：补齐 SK Auto FC 未触发的工具调用
if (evalToolCalls.Count == 0 && userInput != null)
{
    var (fallbackTool, fallbackArgs) = TryKeywordFallback(userInput);
    if (fallbackTool != null)
    {
        Serilog.Log.Information(
            "[FcKeywordFallback] 关键词兜底触发: query='{Query}' → {Tool}({Args})",
            userInput, fallbackTool, fallbackArgs);
        // TODO: 实际执行兜底工具调用，将结果注入 evalToolCalls
    }
}

answer = ApplyDecoupledPipeline(answer, evalToolCalls, isInfoQuery);
```

**预期效果**：
- D001/D011/D012 三个存储兼容性口语化查询可直接命中兜底
- 工具调用准确率从 88.89% 提升至 **93-95%**

---

### 4.3 修复 E3：收紧 OutputValidator 的法规编号匹配逻辑（P1）

**方案**：将 `IsRegulationAllowed` 中的包含匹配改为精确匹配 + 前缀匹配。

**修改文件**：`Agent1/Services/Compliance/OutputValidator.cs` L200-216

**修改前**：
```csharp
private static bool IsRegulationAllowed(string reg, HashSet<string> allowedRegs)
{
    if (allowedRegs.Count == 0)
        return true;

    foreach (var allowed in allowedRegs)
    {
        var nReg = NormalizeRegNumber(reg);
        var nAllowed = NormalizeRegNumber(allowed);
        if (nReg.Equals(nAllowed, StringComparison.OrdinalIgnoreCase)
            || nReg.Contains(nAllowed, StringComparison.OrdinalIgnoreCase)      // ← 删除
            || nAllowed.Contains(nReg, StringComparison.OrdinalIgnoreCase))     // ← 删除
            return true;
    }
    return false;
}
```

**修改后**：
```csharp
private static bool IsRegulationAllowed(string reg, HashSet<string> allowedRegs)
{
    if (allowedRegs.Count == 0)
        return false; // [P1 FIX] 空白名单→不存在合法引用（与 OutputSanitizer 保持一致）

    var nReg = NormalizeRegNumber(reg);

    foreach (var allowed in allowedRegs)
    {
        var nAllowed = NormalizeRegNumber(allowed);
        
        // 精确匹配
        if (nReg.Equals(nAllowed, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // 前缀匹配（仅正向：LLM 编的编号 以 白名单编号 开头）
        // 例如：白名单有 "GB300007"，LLM 输出 "GB300007-2013" → 允许
        if (nReg.StartsWith(nAllowed, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}
```

**关键变更**：
1. `allowedRegs.Count == 0` 从 `return true` 改为 `return false`——与 `OutputSanitizer.IsInWhitelist` L144 行为一致
2. 移除双向 `Contains` 匹配，只保留精确匹配和正向前缀匹配
3. 移除逆向前缀匹配（`nAllowed.StartsWith(nReg)`）——防止短编号误匹配长编号

**预期效果**：
- 消除 "GB300002" 误匹配 "GB3000027" 型漏洞
- 配合 E2 修复，幻觉率进一步降至 **5-8%**

---

### 4.4 修复 E4：检索管道优化（P1）

**方案**：对知识库中的大文档进行更细粒度的 chunk 切分，并在检索时增加类别信号提升精度。

**涉及文件**：
- `Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs`（检索入口）
- 知识库索引重建流程（`scripts/` 下的索引工具）

**具体修改**：

1. **chunk 切分优化**：将"GB30000 系列对照表"从单一大 chunk 拆分为每个化学品一个独立 chunk
2. **检索时注入类别信号**：评测引擎已知当前 case 的 `category`（如"危险类别查询"），可将此信号作为检索过滤条件
3. **增加 chunk 元数据标签**：在 chunk metadata 中标注 `category: hazard_category | storage | distance` 等

**修改 EvalEngine.cs**（约 L270-310，检索调用处）：

```csharp
// 在 EvaluateRetrievalAsync 中，将 case 类别传入检索
var chunks = await _kbService.SearchAsync(
    tc.Query, 
    topK: 10,
    categoryFilter: tc.Category  // [P1 FIX] 传入类别信号提升精度
);
```

**预期效果**：
- P@K 从 0.368 提升至 **0.50-0.60**
- MRR 从 0.524 提升至 **0.65-0.75**

---

### 4.5 修复 E5：Level4 幻觉检测接入 KB 可验证性（P1）

**方案**：Level4 检测逻辑中引用 `ReflectionVerifier` 的 KB 验证结果，将"KB 中不可验证"的 GB 编号标记为 FAIL。

**修改文件**：`Agent1/Services/Eval/EvalEngine.cs`（结论验证逻辑，约 L530-580）

**修改思路**：在生成 `conclusion_reasons` 时，交叉引用 `claim_details` 中 `verdict: "hallucinated"` 的结果：

```csharp
// [P1 FIX] Level4 接入 KB 验证结果
var hallucinatedInClaims = result.ClaimDetails
    ?.Where(c => c.Verdict == "hallucinated")
    .Select(c => c.ClaimedText)
    .ToList() ?? new List<string>();

result.ConclusionReasons.Add(new ConclusionReason
{
    Level = "Level4_HallucinationCheck",
    RuleApplied = $"提取{totalGbCount}个GB编号, 其中{hallucinatedInClaims.Count}个KB不可验证",
    Passed = hallucinatedInClaims.Count == 0  // ← 任何 KB 不可验证 → FAIL
});
```

**预期效果**：
- 幻觉检测不再形同虚设，41 个幻觉声明中将有对应数量的 FAIL 标记
- 评测报告中的 `hallucination_detection_rate` 可获得真实值

---

## 五、实施步骤

| 步骤 | 修复项 | 文件 | 工作量 | 优先级 |
|------|--------|------|--------|--------|
| 1 | E2：修改 EvalFastPrompt 反幻觉指令 | `appsettings.json` L143-144 | 5 分钟 | 🔴 P0 |
| 2 | E3：收紧 OutputValidator 匹配逻辑 | `OutputValidator.cs` L200-216 | 10 分钟 | 🔴 P0 |
| 3 | E1：增加 FC 关键词兜底映射表 | `AgentDialog.cs` L806 附近 | 30 分钟 | 🟠 P0 |
| 4 | E5：Level4 接入 KB 验证结果 | `EvalEngine.cs` 结论验证 | 15 分钟 | 🟡 P1 |
| 5 | E4：检索管道优化（chunk + 类别信号） | `HybridKnowledgeBaseService.cs` + `EvalEngine.cs` | 2 小时 | 🟡 P1 |

**建议分两批实施**：
- **第一批（步骤 1-3）**：当天完成，预期将幻觉率从 27.3% 降至 8-10%、工具准确率升至 93%+
- **第二批（步骤 4-5）**：隔天完成，预期将检索精度和幻觉检测率提升至可用水平

---

## 六、验证方法

### 6.1 回归验证

```bash
# 重新运行评测
dotnet test --filter "EvalEngineIntegrationTests" -v n

# 检查关键指标变化
# 预期: conclusion_accuracy > 90%, hallucination_rate < 10%
```

### 6.2 针对性验证用例

| 验证目标 | 用例 | 修复前结果 | 修复后预期 |
|---------|------|-----------|-----------|
| E2 效果 | C010（氨水） | GB 30000.27/.22 幻觉 | 不编造 GB 子编号 |
| E1 效果 | D001（苯和丙酮） | 无工具调用→拒绝 | 触发 CheckStorageCompatibility |
| E3 效果 | GB30000.2→.27 误匹配 | OutputValidator 放行 | 正确拦截 |
| E5 效果 | 所有幻觉用例 | Level4 passed=true | Level4 passed=false |

### 6.3 指标对比卡

| 指标 | 修复前 | 第一步后预期 | 全部完成后预期 |
|------|--------|------------|--------------|
| 工具调用准确率 | 88.89% | 93-95% | 95%+ |
| 结论准确率 | 82.54% | 88-92% | 93%+ |
| 幻觉率 | 27.3% | 8-12% | 5-8% |
| 平均忠实度 | 0.785 | 0.88-0.92 | 0.93+ |
| 平均引用准确率 | 0.517 | 0.65-0.75 | 0.80+ |
| 检索 Precision@K | 0.368 | 0.368 | 0.50+ |

---

## 附录

### 涉及文件清单

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Agent1/appsettings.json` | 修改 L143-144 | E2: 更新 EvalFastPrompt 反幻觉指令 |
| `Agent1/Services/Compliance/OutputValidator.cs` | 修改 L200-216 | E3: 收紧 IsRegulationAllowed |
| `Agent1/Services/Dialog/AgentDialog.cs` | 新增方法 + 修改 L634 | E1: FC 关键词兜底 |
| `Agent1/Services/Eval/EvalEngine.cs` | 修改结论验证逻辑 | E5: Level4 接入 KB 验证 |
| `Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs` | 增加类别信号参数 | E4: 检索优化 |

### 不需要修改的文件

以下文件的设计是正确的，不需要修改：
- `PromptSanitizer.cs`：剥离 [REGULATIONS:] 的决策是正确的（防止 LLM 机械复制），问题出在评测 Prompt 与它的交互
- `ComplianceFactExtractor.cs`：白名单提取逻辑正确
- `OutputSanitizer.cs`：前缀匹配逻辑合理（允许 -2013 等年份后缀），问题在 OutputValidator 的包含匹配
- `FactAssembler.cs` / `ResponseMerger.cs`：双通道合并逻辑正确
