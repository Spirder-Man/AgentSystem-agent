# Agent1 Bug 知识库

> **目的**：结构化记录每个 Bug 的根因/修复/影响模块，实现跨批次知识累积，避免同类问题反复出现。
> **使用方式**：每次修复 Bug 后按模板录入；每次分析日志前浏览「系统弱点了然表」确定重点监控项。
> **文档版本**：v1.5 | **创建日期**：2026-06-30 | **最后更新**：2026-07-03（新增思维链路记录机制 + Bug-019 回填示例）

---

## 系统弱点了然表（每次测试前必查）

> 这些是已知的系统性弱点，测试时应重点监控。

| # | 弱点 | 已知表现 | 监控信号（grep 关键词） | 关联 Bug |
|:---:|------|------|------|:---:|
| W1 | **LLM 流式输出失控** | 死循环、重复输出、括号级联 | `⚠️ [截断]` / `生成错误` / 单日志行数>500 | [Bug-011](#bug-011) [Bug-012](#bug-012) |
| W2 | **GB 标准编号幻觉** | Qwen3:8b 输出错误 GB 子编号 | `Citation Accuracy` < 70% / `✗ GB` | [Bug-013](#bug-013) |
| W3 | **UTF-8 编码一致性** | Windows GB2312 → 中文乱码(锟斤拷) | `.log` 文件前3行含乱码字符 | [Bug-014](#bug-014) |
| W4 | **FC 工具调用跳过** | LLM 绕过工具直接编造答案 | `[SK诊断] 本轮共调用 0 个工具` | [Bug-001](#bug-001) [Bug-015](#bug-015) |
| W5 | **Embedding 服务稳定性** | 501 / 模型加载超时 | `向量请求失败` / `NotImplemented` | — |
| W6 | **数据库事务一致性** | tx 参数重复 / 并发冲突 | `NpgsqlException` / SQLite 锁 | [Bug-009](#bug-009) |
| W7 | **菜单导航路径漂移** | 菜单结构变动后脚本输入失配 | 交互死锁 / 超时（多个测试同时） | — |
| W8 | **评测格式匹配脆弱** | 模型输出格式变化导致评分归零 | `Faithfulness=0` / `格式不匹配` | [Bug-010](#bug-010) |
| W9 | **KV Cache 跨请求累积（LCP slot 复用）** | FC 退化、重试死亡螺旋、重复输出 | `LLM 调用耗时 >100s` / `生成错误: ...canceled` / 单 case 重复输出 >30 行 / llama-server slot 复用日志 | [Bug-015](#bug-015) [Bug-016](#bug-016) |
| W10 | **FC Required() 策略误用于业务评测** | 死循环、同一工具被调用 40+ 次、评测卡死 | `[工具诊断]` 同一工具名连续出现 >5 次 / 单 case 耗时 >120s | [Bug-018](#bug-018) |
| W11 | **HyDE + FC 递归死循环（静默卡死）** | 进程存活但无输出，FC 重试环路不崩溃 | `[SK诊断]` FC 交替出现 Required/None / 单 case 日志行数 >1800 / 同一工具名交替出现 | [Bug-019](#bug-019) |

---

## Bug 目录（按时间倒序）

| ID | 日期 | 等级 | 类别 | 标题 |
|:---:|------|:---:|------|------|
| [Bug-019](#bug-019) | 07-03 | P0 | LLM配置 | HyDE + FunctionChoiceBehavior.Required() 导致 FC 递归死循环（F005 静默卡死） |
| [Bug-018](#bug-018) | 07-02 | P0 | LLM配置 | FunctionChoiceBehavior.Required() 导致评测死循环 |
| [Bug-017](#bug-017) | 07-01 | P0 | C#语法 | 字符串插值 Guid 格式说明符歧义导致全量 FormatException |
| [Bug-015](#bug-015) | 06-30 | P0 | LLM配置 | KV Cache 溢出导致 FC 退化（C008 重现） |
| [Bug-016](#bug-016) | 06-30 | P0 | 重试螺旋 | KV Cache 不足 + 重试反馈环 → 评测死亡循环（C015） |
| [Bug-014](#bug-014) | 06-30 | P0 | 编码 | UTF-8 控制台编码三层修复（锟斤拷乱码） |
| [Bug-013](#bug-013) | 06-30 | P1 | LLM幻觉 | Qwen3:8b GB 标准编号幻觉 |
| [Bug-012](#bug-012) | 06-30 | P0 | 流式输出 | T6 RAG 数据校验步骤死循环 |
| [Bug-011](#bug-011) | 06-30 | P0 | 流式输出 | T5 Reflection 自纠错死循环 |
| [Bug-010](#bug-010) | 06-29 | P1 | 评测 | T13 评分性能+忠实度格式匹配双修复 |
| [Bug-009](#bug-009) | 06-29 | P0 | 数据库 | InsertSubstance 首条记录 tx 参数重复 |
| [Bug-008](#bug-008) | 06-29 | P0 | 数据库 | 数据库事务未正确提交（P0-7） |
| [Bug-007](#bug-007) | 06-16 | P0 | 工具函数 | 正则表达式 `>=s*` 安全距离提取失效 |
| [Bug-006](#bug-006) | 06-16 | P0 | 并发 | Timer(async void) 并发同步冲突 |
| [Bug-005](#bug-005) | 06-16 | P1 | 检索 | BM25 `_avgDocLength` 除零 |
| [Bug-004](#bug-004) | 06-16 | P1 | 检索 | ReflectionVerifier 检索异常错误标记 |
| [Bug-003](#bug-003) | 06-16 | P2 | 评测 | ConclusionVerifier 双格式匹配缺失 |
| [Bug-002](#bug-002) | 06-16 | P0 | 缓存 | RagCache 并发安全问题 |
| [Bug-001](#bug-001) | 06-16 | P0 | 检索 | RRF 去重键非确定性 |

---

## Bug 详情

### Bug-015：KV Cache 溢出导致 FC 退化（C008 重现）{#bug-015}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🔴 P0 — 评测集后半段全部失效 |
| **发现场景** | T13 合规评测集重跑 — C008 时 LLM 绕过 Function Calling，重复输出同一答案 90+ 次（83.9s，4155 tokens），后续 C010-C014 持续恶化 |
| **影响模块** | `scripts/zh-diag.sh` L228, `Agent1/Services/AI/LlmService.cs` L117-L126 |
| **根因** | **真正机制：LCP（最长公共前缀）slot 复用导致 KV Cache 跨请求累积**。llama-server 默认 `-sps 0.5`（50% 前缀匹配即复用 slot）+ `cache_prompt: true`（默认缓存 prompt KV）。63 条评测 case 共享相同 system prompt 前缀 → 被分配到同一个 slot → slot 的 KV Cache 跨请求累积（case1 + case2 + ... + case63）→ 总 token 超过 `-c 8192` → llama.cpp 截断最早的 tokens（含 system prompt 中的 FC 格式说明）→ 模型无法生成正确的 FC JSON → SK 的 `FunctionChoiceBehavior.Required()` 一直等待 → 2min 超时 → 模型开始自由输出文本（重复循环）。**证据来源**：llama.cpp GitHub Discussion #13606。注意：`-c 8192` 仅是触发条件，真正的架构缺陷是服务端的 slot 复用机制，不是单次请求太大 |
| **修复** | **三层防御架构（T13 无状态架构）**：① **服务端**：`zh-diag.sh` 增加 `-sps 0.0`（禁用 LCP slot 匹配，每个请求独立 KV Cache）+ `-c 32768`（4 倍扩容）+ `--cache-type-k q8_0 --cache-type-v q8_0`（KV Cache 量化）+ `-fa`（Flash Attention）；② **客户端**：新增 `TokenBudgetManager.cs`（Token 预算检查 + Prompt 裁剪）；③ **评测层**：`EvalEngine` 按意图裁剪工具集（info_query=3 工具、compliance=5 工具）+ `LlmService` 注入 `cache_prompt: false` + AgentDialog 每次创建独立 Session |
| **修复提交** | 本次 |
| **关联 Bug** | [Bug-016](#bug-016) 同一根因不同表现 |
| **验证方法** | T13 重跑全 63 条完成无中断；`grep "未调用任何工具" T13*.log` 无新增匹配 |
| **教训** | llama.cpp `-c` 参数必须 ≥ 最大 prompt 长度（system + tools + user + tool_results）× 安全系数 1.5 = 约 15000-20000 tokens，默认 8192 仅适合简单对话。Q4_K_M 量化模型在 KV Cache 不足时 Function Calling 能力急剧衰减 |

---

### Bug-016：KV Cache 不足 + 重试反馈环 → 评测死亡循环（C015）{#bug-016}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🔴 P0 — C015 永久卡死，评测无法完成 |
| **发现场景** | T13 合规评测集重跑 — C015「苯乙烯+过氧化物储存兼容性」陷入死循环：LLM 调用 1012s→1272s→2249s→2427s，每次超时后 SK 重试将前次工具结果注入 Prompt → 上下文进一步膨胀 → 越重试越慢 |
| **影响模块** | `scripts/zh-diag.sh` L228, `Agent1/Services/AI/LlmService.cs` L110（2min 超时）, L358-L402（重试机制）, `Agent1/Services/Dialog/AgentDialog.cs` L425-L453（EvalFast 调用链） |
| **根因** | **LCP slot 复用导致 KV Cache 跨请求累积 + 重试反馈环**：① llama-server `-sps 0.5` 默认 slot 复用 → 63 case 的 KV Cache 累积在同一 slot → 超出 `-c 8192` → 模型无法在受限上下文中生成 FC JSON → 超时；② SK 重试时 `FunctionCallDiagnosticsFilter` 将前次成功的工具调用结果注入 Prompt（`已保存 N 个工具结果供重试用`）→ Prompt 更大 → 更容易超时 → 再次重试 → Prompt 进一步膨胀（日志中从 1 个工具结果增长到 3 个，RAG 检索结果从 3 条增长到包含完整法规全文）→ 最终 LLM 调用耗时达 2427s 仍失败 |
| **修复** | 同 Bug-015 — 三层防御架构（`-sps 0.0` 禁用 slot 复用 + TokenBudgetManager + Per-Case 工具裁剪 + `cache_prompt: false`）从根源阻断反馈环 |
| **修复提交** | 本次 |
| **关联 Bug** | [Bug-015](#bug-015) 同一根因，[Bug-011](#bug-011) [Bug-012](#bug-012) 同属 LLM 输出质量族 |
| **验证方法** | C015 苯乙烯+过氧化物查询正常完成，单 case 耗时 < 60s；日志中无 `生成错误: ...canceled` 或 `保存 N 个工具结果供重试用` |
| **教训** | 重试机制在资源不足场景下可能成为"毒药"：每次重试增加上下文重量反而降低成功率。必须从根因（上下文容量）修复，不能仅靠重试。熔断器应检测"重试成功率下降"信号并提前中断 |

---

### Bug-014：UTF-8 控制台编码三层修复（锟斤拷乱码）{#bug-014}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🔴 P0 — 全系统日志可读性受损 |
| **发现场景** | CPU 降级模式全量测试 — 所有 `.log` 文件中文输出变为「锟斤拷」乱码 |
| **影响模块** | Agent1/Program.cs, Agent1.Api/Program.cs, Agent1.Tests/ModuleInitializer.cs, 所有 Console/Serilog 输出 |
| **根因** | Windows 中文版默认 `Console.OutputEncoding = GB2312(CP936)`，所有 UTF-8 输出被误解码。三层传播：① Console.WriteLine → ② Serilog Console Sink → ③ 子进程（dotnet test / TRX Logger）继承错误代码页 |
| **修复** | 三层强制 UTF-8：① `Program.cs` 启动时 `Console.OutputEncoding = UTF8`；② `Agent1.Api/Program.cs` 同上；③ `Agent1.Tests/ModuleInitializer.cs` 使用 `[ModuleInitializer]` 钩子在测试程序集加载时设置 |
| **修复提交** | `bff7ee21`, `779a8b05` |
| **关联 Bug** | 无前序 |
| **验证方法** | 检查 `.log` 文件前 3 行不含「锟斤拷」等乱码字符；`grep "锟斤拷" *.log` 返回空 |
| **教训** | UTF-8 编码问题必须从入口层（Program.cs）强制，不能仅靠 Serilog 配置（TextWriter 已经被 Console.Out 的编码器污染） |

---

### Bug-013：Qwen3:8b GB 标准编号幻觉 {#bug-013}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🟡 P1 — 合规结论准确性受损 |
| **发现场景** | T13 合规评测集 — 多个用例 GB 编号错误：硝酸铵→GB 30000.14（应为.15+.2）、丙酮→GB3025（缺零）、甲苯→GB300026（多余零） |
| **影响模块** | ReflectionVerifier.cs, EvalEngine.cs, ChemicalSubstanceDatabase.cs |
| **根因** | Qwen3:8b 从预训练知识生成 GB 编号而非查询数据库，导致：① 格式错误（缺零/多余零）② 子编号映射错误（14→15）③ 完全编造不存在的编号 |
| **修复** | 新增 `ReflectionVerifier.ValidateGbNumberHallucinations()` — 正则提取 GB 30000.XX → 格式纠错(GB3025→GB 30000.25) → 数据库交叉验证 → 集成到自纠错 Prompt 和 EvalEngine 忠实度评分 |
| **修复提交** | `10fb29ae` |
| **关联 Bug** | 无前序，与 [Bug-011/012](#bug-011) 同属 LLM 输出质量问题族 |
| **验证方法** | T13 评测 Citation Accuracy 是否提升（目标 > 80%）；`grep "✗ GB\|→ GB" T13*.log` |
| **教训** | Qwen3:8b 对精确编号（GB 30000 子编号）的幻觉率高，必须代码级后校验而非信任 LLM 输出 |

---

### Bug-012：T6 RAG 数据校验步骤死循环 {#bug-012}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🔴 P0 — 单次请求耗尽 82.2s + 大量 token |
| **发现场景** | 远程 AutoDL 容器回归测试 — RAG Step 5 校验"是否通过"时，模型反复输出"不通过，回答中引用了知识库中不存在的法规条款..."200+ 次 |
| **影响模块** | LlmService.cs (InvokeStreamAsync), RAG.cs, RAGModule.cs |
| **根因** | `InvokeStreamAsync` 仅检测"是否合规"语义重复（窄检测），未能识别校验语句的结构性重复。Qwen3:8b 在校验失败时陷入「输出否定判断 → 继续重复」循环 |
| **修复** | 替换为 3 维度通用死循环检测：① 连续相同行 >8 次 → 截断；② 字符级联（`)`/`）`/`】`）>12 个 → 截断；③ 总输出 >5000 字符 → 硬截断 |
| **修复提交** | `10fb29ae` |
| **关联 Bug** | [Bug-011](#bug-011) 同一根因（流式输出无通用截断），[Bug-013](#bug-013) 同属 LLM 输出质量族 |
| **验证方法** | T6 日志中无 `⚠️ [截断]` 且无 `⚠️ [慢请求告警]`；耗时 < 60s |
| **教训** | 死循环检测必须通用化（检测输出模式而非语义），不同 Prompt 会触发不同形式的重复 |

---

### Bug-011：T5 Reflection 自纠错死循环 {#bug-011}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-30 |
| **严重等级** | 🔴 P0 — 修正文本重复数百次 + 括号级联 |
| **发现场景** | 远程 AutoDL 容器回归测试 — Reflection 模块"代码级事实核查"阶段，修正内容被反复输出数百次，每行末尾 `)` 从 `))` 增长到 `))))))))......` |
| **影响模块** | LlmService.cs (InvokeStreamAsync), RunReflectionStreamTools.cs, ReflectionModule.cs |
| **根因** | 与 [Bug-012](#bug-012) 相同：`InvokeStreamAsync` 窄检测只匹配"是否合规"，未检测 Reflection 修正文本的重复。Qwen3:8b 在输出修正建议时陷入「输出修正 → 追加右括号 → 重复」的级联循环 |
| **修复** | 同上 3 维度通用死循环检测（D1 重复行 + D2 字符级联 + D3 总长度硬截断） |
| **修复提交** | `10fb29ae` |
| **关联 Bug** | [Bug-012](#bug-012) 同一根因 |
| **验证方法** | T5 日志中无 `⚠️ [截断] 检测到字符重复级联` 信号；Reflection 修正输出行数 < 20 |
| **教训** | 字符级联（`)`/`）`/`】`）是一种独特的死循环模式，与语义重复不同，需要独立检测维度 |

---

### Bug-010：T13 评分性能+忠实度格式匹配双修复 {#bug-010}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-29 |
| **严重等级** | 🟡 P1 — 评测分数失真 |
| **发现场景** | T13 合规评测集 — 模型输出格式变化后，`Faithfulness` 评分正则匹配失败，导致分数归零 |
| **影响模块** | EvalEngine.cs, EvalModels.cs |
| **根因** | ① 忠实度评分正则只能匹配旧格式，模型换行/空格变化后匹配失效；② 50 条评测耗时超过固定超时（180s）导致部分用例截断 |
| **修复** | ① 格式匹配增强（兼容新旧格式）；② 新增 T13 心跳检测机制 + 安全上限 3600s |
| **修复提交** | `99bdfa49`, `8de454ec`, `519b3faa` |
| **关联 Bug** | — |
| **验证方法** | T13 Faithfulness 分数合理（非 0 或 1.0 极端值）；50 条全部完成无截断 |
| **教训** | 评测正则必须宽松匹配（兼容模型输出格式漂移），固定超时不适合条数变化的评测集 |

---

### Bug-009：InsertSubstance 首条记录 tx 参数重复 {#bug-009}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-29 |
| **严重等级** | 🔴 P0 — 数据库写入失败 |
| **发现场景** | 数据库初始化 — 首条化学品记录插入时 tx 参数被重复传递 |
| **影响模块** | ChemicalDatabaseService.cs |
| **根因** | `InsertSubstance(conn, tx, tx, ...)` tx 参数在调用中重复两次，导致参数绑定错误 |
| **修复** | 修正为 `InsertSubstance(conn, tx, ...)` |
| **修复提交** | `0021ee38` |
| **关联 Bug** | [Bug-008](#bug-008) 同属数据库事务问题族 |
| **验证方法** | 数据库初始化无异常；58 种危化品全部入库 |
| **教训** | 多参数方法调用是手工错误高发区，应考虑使用具名参数或 Builder 模式 |

---

### Bug-008：数据库事务未正确提交（P0-7）{#bug-008}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-29 |
| **严重等级** | 🔴 P0 — 数据丢失 |
| **发现场景** | 全项目代码扫描 — 多处 SQLite 事务未正确提交，导致数据写入丢失 |
| **影响模块** | ChemicalDatabaseService.cs, DatabaseService.cs |
| **根因** | `SqliteCommand` 在显式事务中执行时未传入 `transaction` 参数，导致命令在事务外执行 |
| **修复** | 所有显式事务中的 `SqliteCommand` 统一传入 `transaction` 参数 |
| **修复提交** | `1a0cf36f` |
| **关联 Bug** | [Bug-009](#bug-009) 同属数据库事务问题族 |
| **验证方法** | 数据库写入后立即读取验证；事务完整性测试通过 |
| **教训** | SQLite 显式事务中必须显式传入 transaction 参数，否则静默失败（不抛异常但数据丢失） |

---

### Bug-007：正则表达式 `>=s*` 安全距离提取失效 {#bug-007}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🔴 P0 — 安全距离功能完全失效 |
| **发现场景** | 全项目代码扫描 P0-1 |
| **影响模块** | ChemicalComplianceTools.cs L334 |
| **根因** | 正则表达式漏写反斜杠：`>=s*` 应为 `>=\s*`，导致所有安全距离提取返回空 |
| **修复** | `>=s*` → `>=\s*` |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 安全距离查询返回非空值 |
| **教训** | 正则表达式应使用 `[GeneratedRegex]` 编译时校验 + 单元测试覆盖 |

---

### Bug-006：Timer(async void) 并发同步冲突 {#bug-006}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🔴 P0 — GPU 向量索引并发崩溃 |
| **发现场景** | 全项目代码扫描 P0-3 |
| **影响模块** | GpuVectorIndexService.cs L242 |
| **根因** | `Timer(async void)` 无法跟踪异步操作完成状态，高并发下多个回调同时修改共享数据结构 |
| **修复** | 替换为 `PeriodicTimer` + `Task.Run`，确保每次只有一个重建任务在执行 |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 高并发向量检索场景无崩溃；GPU 索引重建期间查询不中断 |
| **教训** | 禁止使用 `async void`（除事件处理器外），`Timer` 应替换为 `PeriodicTimer` |

---

### Bug-005：BM25 `_avgDocLength` 除零 {#bug-005}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🟡 P1 — 空知识库时 BM25 崩溃 |
| **发现场景** | 全项目代码扫描 P1-5 |
| **影响模块** | KnowledgeBaseService.cs L190 |
| **根因** | 知识库为空时 `_avgDocLength = 0`，BM25 公式中除零导致 `NaN` 得分 |
| **修复** | `Math.Max(1.0, _avgDocLength)` 除零保护 |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 空知识库环境（首次安装）不崩溃 |
| **教训** | 所有除法运算必须有除零保护，尤其是动态计算的平均值变量 |

---

### Bug-004：ReflectionVerifier 检索异常错误标记 {#bug-004}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🟡 P1 — Reflection 误判幻觉 |
| **发现场景** | 全项目代码扫描 P1-9 |
| **影响模块** | ReflectionVerifier.cs L165 |
| **根因** | 知识库检索异常时（如网络超时），代码错误地设置 `FoundInSource=true`，导致"幻觉"内容被误标为"已核实" |
| **修复** | 检索异常时标记 `[KB检索异常]` 而非 `FoundInSource=true` |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 模拟 KB 服务不可用时，Reflection 输出含 `[KB检索异常]` 标记 |
| **教训** | 异常路径的默认值必须安全（fail-safe），不能默认"成功" |

---

### Bug-003：ConclusionVerifier 双格式匹配缺失 {#bug-003}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🔵 P2 — 评测容错性不足 |
| **发现场景** | 全项目代码扫描 P2-12 |
| **影响模块** | ConclusionVerifier.cs L79 |
| **根因** | 仅匹配 `【合规判断】` 格式，不匹配模型输出的替代格式 `[判定:is_compliant=...]` |
| **修复** | 同时匹配 `【合规判断】` 和 `[判定:is_compliant=...]` 两种输出格式 |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | [Bug-010](#bug-010) 同属评测格式匹配问题族 |
| **验证方法** | 两种格式的模型输出均能正确解析 |
| **教训** | 评测正则必须防御性设计，模型输出格式会随着 Prompt 微调而漂移 |

---

### Bug-002：RagCache 并发安全问题 {#bug-002}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🔴 P0 — 高并发下数据损坏 |
| **发现场景** | 全项目代码扫描 P0-2 |
| **影响模块** | ChemicalComplianceTools.cs L49 |
| **根因** | `RagCache` 使用普通 `Dictionary`，多线程并发读写导致数据损坏 |
| **修复** | 升级为 `ConcurrentDictionary` + TTL + LRU 淘汰 |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 压力测试下缓存命中率正常，无异常 |
| **教训** | 任何可能被多线程访问的共享数据结构必须使用并发安全类型 |

---

### Bug-001：RRF 去重键非确定性 {#bug-001}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-06-16 |
| **严重等级** | 🔴 P0 — 检索结果不稳定 |
| **发现场景** | 全项目代码扫描 P0-4 |
| **影响模块** | HybridKnowledgeBaseService.cs L527 |
| **根因** | RRF 融合去重键使用 `Guid.NewGuid()`（每次调用生成不同值），导致同一文档不能被正确去重，检索结果不稳定 |
| **修复** | 改为确定性去重键 `GetDedupKey()`（基于文档路径+chunk索引） |
| **修复提交** | P0 级 Bug 系统性修复批次 |
| **关联 Bug** | — |
| **验证方法** | 同一查询多次执行返回相同结果集 |
| **教训** | 去重/幂等操作中的标识符必须是确定性的（基于内容语义），不能用随机值 |

---

### Bug-018：FunctionChoiceBehavior.Required() 导致评测死循环 {#bug-018}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-07-02 |
| **严重等级** | 🔴 P0 — 评测卡死，单条用例永不完结 |
| **发现场景** | T13 评测集 Bug-017 修复后重跑 — F005（乙醇+硝酸综合审核）时 LLM 持续调用 CheckStorageCompatibility 40+ 次 |
| **影响模块** | `Agent1/Services/AI/LlmService.cs` L615 |
| **根因** | `FunctionChoiceBehavior.Required()` 每轮 HTTP 请求都强制 LLM 调用工具。SK 内部 FC 循环：工具返回结果后自动发起下一轮 HTTP 请求 → Required() 再次生效 → LLM 被迫再次调用工具 → 无限循环。LLM 已生成正确答案但被 Required() 剥夺了"自主终止权"。三层死循环检测（D1重复行/D2字符级联/D3总字符截断）均未拦住，因为每轮文本不同、中文无括号级联、截断触发太晚。 |
| **修复** | `FunctionChoiceBehavior.Required()` → `FunctionChoiceBehavior.Auto()`，业务评测由 LLM 自主决定是否调用工具。Required() 仅保留在 FC 就绪性检查（`InvokeNonStreamingWithRetryAsync` L467）。 |
| **修复提交** | 待提交 |
| **关联 Bug** | [Bug-017](#bug-017)（Bug-017 隐藏了 Bug-018，修完前者才暴露后者） |
| **验证方法** | T13 全量评测跑通，无单一工具连续调用 >5 次 |
| **教训** | 评测有两种模式需要分离：FC 就绪检查（需要 Required() 强制验证管线）vs 业务评测（需要 Auto() 让 LLM 自主决策）。同一个策略不能同时用于两个目的不同的场景。 |
| **追问深度** | 3 层（代码配置 → SK 内部 FC 循环 → 设计冲突） |

---

### Bug-019：HyDE + FunctionChoiceBehavior.Required() 导致 FC 递归死循环（F005 静默卡死）{#bug-019}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-07-03 |
| **严重等级** | 🔴 P0 — 静默卡死，进程存活但无进度，比 Bug-018 更隐蔽 |
| **发现场景** | T13 心跳监控日志（t13_heartbeat.log）— F005（乙醇+硝酸综合审核）卡死在 `CheckStorageCompatibility → HyDE文档检索 → CheckStorageCompatibility` 无限递归，单 case 超 1800+ 行日志无任何结论输出 |
| **影响模块** | `Agent1/Services/AI/LlmService.cs` L124（FC 硬编码）、`Agent1/Services/AI/ILlmService.cs`（接口缺 FC 参数）、`Agent1/Services/Knowledge/HybridKnowledgeBaseService.cs`（HyDE 调用方）、`Agent1/Services/Dialog/RunReflectionStreamTools.cs`（Reflection 调用方） |
| **根因** | **L1 现象**：进程存活(PID 2929)但不产出结论，同一 case 中 CheckStorageCompatibility 被反复调用；**L2 调用链**：主管线 FC=Required → CheckStorageCompatibility DB 未命中 → HyDeRetrieveIfAvailableAsync → HydeRetrieveAsync → InvokeStreamWithRetryAsync(hydePrompt) → InvokeStreamAsync 硬编码 `FunctionChoiceBehavior.Required()` → HyDE 提示词「生成法规摘要…乙醇硝酸同库储存」被强制调用工具 → 再次触发 CheckStorageCompatibility → 无限递归。Bug-018 是显性死循环（同一工具 40+ 次、会超时），Bug C 是**静默卡死**（进程永远不死、监控告警不触发、每轮工具名交替变化）；**L3 设计冲突**：Git 历史 commit `82dae1dd` FC 统一化时主动移除了 FC 灵活性，HyDE 被引入时 (`40bcb0fa`) 被动继承了 Required()，形成「FC 统一 + HyDE 特殊需求」的设计冲突。 |
| **修复** | **FC 策略参数化架构重构**（4 文件修改）：① `ILlmService.cs` 新增 3 个带 `FunctionChoiceBehavior? fcBehavior = null` 参数的重载，默认 null → Required() 向后兼容；② `LlmService.cs` 核心实现：`InvokeStreamAsync` / `InvokeStreamWithRetryAsync` / `InvokeNonStreamingWithRetryAsync` 全部 FC 参数化，新增 effectiveFc = fcBehavior ?? Required()；③ `HybridKnowledgeBaseService.cs` HyDE 两条路径（流式+降级）改为 `FunctionChoiceBehavior.None()` 从根源阻断递归；④ `RunReflectionStreamTools.cs` 分层策略：Step1→Auto()（允许但不强制）、Step4/5/降级→None()（手动 ParseToolCalls 模式不依赖 SK FC） |
| **修复提交** | 待提交 |
| **关联 Bug** | [Bug-018](#bug-018)（同一 FC 配置族的显性表现）、[Bug-016](#bug-016)（同属静默卡死但根因是 KV Cache） |
| **验证方法** | T13 心跳监控中 F005 无 1800+ 行重试日志；`grep "Required\|None" t13*.log` FC 策略分布符合预期（主路径 Required、HyDE 路径 None、Reflection 路径 Auto/None） |
| **教训** | ① 静默卡死比显性死循环更危险：进程存活、监控不告警、难以发现；② FC 策略是**语义级参数**，不同调用目的（工具辅助检索 vs 管线强制调用）对 FC 有不同需求，API 设计时必须参数化不能硬编码；③ 统一化重构时需检查所有 call site 是否有差异化需求，统一 ≠ 同质。 |
| **追问深度** | 3 层（L1 现象 → L2 调用链递归机制 → L3 设计冲突与 Git 历史演进） |

## 思维链路（对话复盘）

> 记录本次 Bug 分析的关键思考节点，使后续复盘时不依赖记忆即可还原推理过程。

| # | 节点类型 | 触发问题 / AI 追问 | 关键发现 | 决策 | 否决的路径 |
|:---:|------|------|------|------|------|
| N1 | 现象确认 | t13_heartbeat.log 中 F005 卡死在 1800+ 行循环 | 进程存活(PID 2929)但不产出结论，CheckStorageCompatibility 被反复调用 | 确认 Bug C 真实存在，进入 L1 分析 | — |
| N2 | L1→L2 追问 | 为什么 HyDE 提示词会触发 CheckStorageCompatibility？ | 调用链：主管线 FC=Required → CheckStorageCompatibility → HyDE → InvokeStreamAsync 硬编码 Required() → 递归 | 深入原理层追踪完整调用链而非修症状 | 停在 L1 修症状（加超时/截断）无法根治递归根因 |
| N3 | 横向对比 | Bug-016 / Bug-018 和 Bug C 有什么异同？ | 同属 FC 配置族：Bug-018 是显性死循环（同一工具 40+ 次、会超时），Bug C 是静默卡死（进程永远不死、工具名交替变化） | 确认 Bug C 比 Bug-018 更隐蔽更危险 | — |
| N4 | L2→L3 追问 | 为什么 InvokeStreamAsync 硬编码 Required()？Git 历史上谁改的？ | commit `82dae1dd` FC 统一化主动移除灵活性，HyDE (`40bcb0fa`) 被动继承 Required()，Bug B 修复 (`0a246d3d`) 只改了评测路径 | 暴露设计冲突：FC 统一 ≠ 所有场景需求相同 | 不追溯 Git 历史就无法理解"为什么当时没发现这个递归问题" |
| N5 | 方案分支 | 方案 A（FC 策略参数化）还是方案 B（HyDE 路径硬编码改 None）？ | A 向后兼容（默认 Required）、所有调用方可控；B 简单但有回归风险（Reflection 路径也被影响） | 选 A：FC 策略参数化架构重构（4 文件修改） | B：硬编码改 None，无法精细控制 Reflection 各步骤的 FC 策略 |
| N6 | 实现决策 | Reflection Step1 用 Auto() 还是 None()？ | Reflection 使用手动 ParseToolCalls 模式，不依赖 SK Auto FC；Step1 需要工具辅助推理 | Step1→Auto（允许但不强制），Step4/5/降级→None（纯审查/修正任务） | 全部 None：Step1 可能跳过工具调用失去推理依据；全部 Auto：与手动 ParseToolCalls 可能冲突 |

---

### Bug-017：字符串插值 Guid 格式说明符歧义导致全量 FormatException {#bug-017}

| 字段 | 内容 |
|------|------|
| **发现日期** | 2026-07-01 |
| **严重等级** | 🔴 P0 — 63 条评测全部崩溃，tool_call_rate=0 |
| **发现场景** | T13 合规评测集执行 — 每条用例第一行即抛出 FormatException，FC 就绪性检查却通过（因为走的是不同代码路径） |
| **影响模块** | `Agent1/Services/Dialog/AgentDialog.cs` L519 |
| **根因** | C# 字符串插值中 `{Guid.NewGuid():N[..6]}` 的 `:` 是格式分隔符，冒号后 `N[..6]` 被编译器整体当作 `Guid.ToString("N[..6]")` 的格式参数。`"N[..6]"` 不是合法格式字符串 → FormatException。正确意图是 `.ToString("N")` 去横杠 + `[..6]` 取前 6 位，但两个操作不能挤在 `:` 后面。 |
| **修复** | 将 `.ToString("N")` 和 `[..6]` 移到插值表达式 `{}` 外部：`Guid.NewGuid().ToString("N")[..6]` |
| **修复提交** | 待提交 |
| **关联 Bug** | [Bug-018](#bug-018)（修复暴露出后续的死循环问题） |
| **验证方法** | 编译通过，63 条评测不再抛 FormatException |
| **教训** | C# `$` 插值中 `:` 之后进入"格式说明符模式"，不再是 C# 表达式上下文。范围运算符 `[..]` 在 `:` 之后不被识别。需要范围切片时，表达式必须在 `{}` 外部完成。 |
| **追问深度** | 4 层（现象 → C# 插值解析规则 → Roslyn 编译器中表示 → C# 语言设计决策） |

---

## Bug 分类统计

| 类别 | Bug 数 | 典型表现 |
|------|:---:|------|
| LLM 流式输出质量 | 2 | 死循环、重复输出 |
| LLM 幻觉 | 1 | GB 编号错误 |
| LLM 配置 | 4 | FC 退化、重试死亡螺旋、Required() 死循环、HyDE+FC 递归 |
| 编码/乱码 | 1 | UTF-8 → 锟斤拷 |
| 数据库事务 | 2 | tx 重复 / 未提交 |
| 并发安全 | 2 | Dictionary / async void |
| 检索算法 | 2 | RRF 去重 / BM25 除零 |
| 工具函数 | 1 | 正则表达式失效 |
| 评测系统 | 2 | 格式匹配 / 评分归零 |
| 异常处理 | 1 | 检索异常错误标记 |
| C# 语法 | 1 | 字符串插值格式说明符歧义 |
| **合计** | **19** | |

---

## 思维链路记录（为什么要有这个？）

> 🔴 **核心问题**：传统 Bug 记录只保留"结果"（根因是什么、怎么修的），丢失了"过程"（怎么一步步想到的）。下次复盘时，你只看到结论，看不到推理——相当于每次都从头推演，无法站在上次的肩膀上。
>
> **解决方案**：每个 Bug 增加「思维链路」字段，记录关键思考节点、分支决策、否决过的路径。使复盘时不依赖记忆即可还原完整推理过程。

**思维链路格式**：

```markdown
## 思维链路（对话复盘）

> 记录本次 Bug 分析的关键思考节点，使后续复盘时不依赖记忆即可还原推理过程。

| # | 节点类型 | 触发问题 / AI 追问 | 关键发现 | 决策 | 否决的路径 |
|:---:|------|------|------|------|------|
| N1 | 现象确认 | 用户发现 XX 日志异常 | 确认 Bug 表现范围 | 进入 L1 分析层 | — |
| N2 | L1→L2 追问 | 为什么 XX 会导致 YY？ | 调用链追踪到 ZZ | 深入原理层而非修症状 | 停在这个 Bug 修症状就完了 |
| N3 | 横向对比 | 和 Bug-NN 有什么异同？ | 同族不同触发条件 | 对比分析找共性 | — |
| N4 | 方案分支 | 方案 A 还是方案 B？ | A 向后兼容、B 简单但风险大 | 选 A | B：硬编码改 None，影响 Reflection 路径 |
| N5 | 实现决策 | 某路径用 Auto 还是 None？ | 手动 ParseToolCalls 不依赖 SK FC | 分层策略 | 全部统一：丢失场景差异 |
```

**节点类型分类**：

| 类型 | 含义 | 示例 |
|------|------|------|
| 现象确认 | 从日志/监控中确认 Bug 真实存在 | "t13_heartbeat.log 中 F005 卡死在 1800+ 行循环" |
| L1→L2 追问 | 从表面现象向下追问到调用链机制 | "为什么 HyDE 提示词会触发 CheckStorageCompatibility？" |
| L2→L3 追问 | 从调用链追问到设计冲突/历史演进 | "为什么 InvokeStreamAsync 硬编码 Required()？Git 历史上谁改的？" |
| 横向对比 | 与同类 Bug 对比找共性和差异 | "Bug-018 vs Bug-019：都是 FC 配置问题，为什么表现不同？" |
| 方案分支 | 在多个方案之间做选择 | "参数化 vs 硬编码改 None：哪个更安全？" |
| 实现决策 | 具体实现层面的选择 | "Reflection Step1 用 Auto 还是 None？" |

> 💡 **一句话记忆**：Bug 记录的价值不在于"这次修好了什么"，而在于"下次能多快定位到同类问题"。思维链路是下次定位的加速器。

---

## 录入模板（新 Bug 使用）

```markdown
### Bug-XXX：{标题} {#bug-xxx}

| 字段 | 内容 |
|------|------|
| **发现日期** | YYYY-MM-DD |
| **严重等级** | 🔴 P0 / 🟡 P1 / 🔵 P2 |
| **发现场景** | 如何发现的（测试用例 / 代码扫描 / 用户反馈） |
| **影响模块** | 涉及的文件（精确到行号） |
| **根因** | 为什么发生（代码逻辑/环境/模型行为） |
| **修复** | 怎么修的（方案 + 关键代码变更） |
| **修复提交** | commit hash |
| **关联 Bug** | 相关 Bug ID |
| **验证方法** | 如何确认已修复 |
| **教训** | 沉淀的工程原则 |
| **追问深度** | 追问了几层、各层触达了什么（L1现象→L2原理→L3设计冲突→...） |
| **思维链路** | 见上方「思维链路记录」格式，用表格记录关键思考节点和分支决策 |
```
