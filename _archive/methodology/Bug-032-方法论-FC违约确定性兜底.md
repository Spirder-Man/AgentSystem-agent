# Bug-032 方法论：FC=Required 违约时确定性兜底

> **来源**：Agent1 v4.3 仪表盘自动合规扫描中 LLM 通用废话透传问题
> **日期**：2026-07-18 | **Bug 编号**：[Bug-032](file:///d:/桌面/agent/项目/Agent1/docs/project/Bug知识库.md#bug-032)
> **关联 Skill**：`engineering-deep-learning-methodology.md`

---

## 一、问题定义

Qwen3-8B 在 FC=Required 模式下偶尔忽略 Function Calling 指令，退化为通用聊天模式，输出含 emoji、时间戳注释的废话。这些废话通过 `ApplyDecoupledPipeline` 的 else 分支与拒绝模板合并后展示给用户。

## 二、核心方法论跃迁

### 2.1 从"模式匹配"到"确定性信号"

| 方案 | 可靠性 | 问题 |
|------|--------|------|
| 正则匹配 LLM 输出（emoji、时间戳） | 低 | 猫捉老鼠——模型换个说法就绕过 |
| 检查 `toolCalls.Count == 0` | 高 | 框架层结构化数据，二进制信号，不存在模糊地带 |

**关键洞察**：FC=Required 下 toolCalls==0 是 Semantic Kernel 框架层的违约信号，不是 LLM 文本。用框架层结构化数据做判断比用 LLM 输出内容做模式匹配可靠得多。

### 2.2 从"修一个 Bug"到"系统性缺陷审计"

初始发现问题后，没有立即修复。而是：

1. 先全量审计 6 条业务路径的输出质量门覆盖情况
2. 发现 ChemicalCompliance（有门但有缺口）、Emergency/RegulatoryAudit/KnowledgeGraph（零门）
3. 逐项排查 7 个影响维度后才执行修复

### 2.3 模型问题 vs 工程问题双轴分析

不做单一归因。建立双轴框架：

- **模型问题（40%）**：Qwen3-8B FC 遵循率非 100%，小模型 FC 能力有限
- **工程问题（60%）**：OutputSanitizer 职责太窄（只管 GB 不管废话），ApplyDecoupledPipeline else 分支不该合并不可信内容

判定不是用来推卸责任，而是用来**决定修复方向**：既然模型不可靠，就用工程确定性兜底。

---

## 三、修复架构决策

### 3.1 核心逻辑

```
FC=Required + toolCalls.Count == 0
        ↓
  LLM 输出 100% 不可信（绕过工具验证）
        ↓
  丢弃全部 LLM 文本
        ↓
  返回 FactAssembler.BuildNoResult()
  "基于现有资料无法给出确定结论，建议联系安环部门人工确认。"
```

### 3.2 影响面矩阵（7 维度逐项排查）

| 维度 | 影响 | 判定 |
|------|------|------|
| toolCalls==0 是否有合法场景？ | FC=Required 下永远不存在，违约即异常 | ✅ 安全 |
| 其他业务链路 | Emergency/RegulatoryAudit/KnowledgeGraph 走 GeneralChatAsync，不受影响 | ✅ 无影响 |
| 降级链路 | 熔断器/规则引擎是正交的三条独立防线 | ✅ 无干扰 |
| OutputValidator | toolCalls==0 时本就跳过，不变 | ✅ 无变化 |
| 会话/记忆 | 拒绝文本作为上下文注入下次推理 | ✅ 正向 |
| Metrics | 需新增 `agent1_fc_contract_violation_total` 计数器 | ⚠️ 已补齐 |
| 未来 FC 策略变更 | 若 FC 从 Required 改为 Auto，toolCalls==0 变为合法 | ⚠️ 68 行防御性注释已标注 |

### 3.3 耦合声明（防御性注释）

```csharp
// ⚠️ 耦合声明：
//   此分支的正确性依赖 FC=Required 契约。如果未来将
//   ExecuteChemicalComplianceAsync 的 FC 策略改为 Auto/None，
//   必须同步修改此分支逻辑 — toolCalls==0 在 Auto 模式下是正常行为，
//   不应触发拒绝。建议在此分支上方增加 FC 策略检查。
```

---

## 四、可复用的工程原则

1. **输出质量门不应以 LLM 行为正常为前提**：质量门应独立运行，以框架层结构化信号为判断依据
2. **异常路径走"安全侧"**：宁可信息不足，不可信息错误。`Merge(拒绝, 垃圾)` = 垃圾
3. **影响面分析不是可选的**：一个 else 分支修改可能触发连锁反应，必须逐维度排查
4. **松耦合点需要防御性注释**：标注契约依赖，防止未来维护者误改
5. **二进制信号 > 启发式猜测**：能用框架层数据就不用 LLM 输出做判断

---

## 五、思维链路速查

| # | 节点 | 关键决策 |
|:---:|------|------|
| N1 | 现象确认 | 日志显示 `[SK诊断] ⚠️ 本轮未调用任何工具` |
| N2 | L1→L2 | 追踪到 ApplyDecoupledPipeline else 分支的合并逻辑 |
| N3 | 横向对比 | 全量审计 6 条路径，3 条零质量门 |
| N4 | 架构哲学 | 模型 40% vs 工程 60%，决定用工程确定性兜底 |
| N5 | 方案分支 | 放弃模式匹配 → 转向 toolCalls 确定性信号 |
| N6 | 影响面分析 | 7 维度排查后执行修复 |
| N7 | 实现决策 | 先修 ChemicalCompliance(P0)，Emergency 等后续 |

> 完整思维链路见 [Bug知识库 Bug-032](file:///d:/桌面/agent/项目/Agent1/docs/project/Bug知识库.md#bug-032)
