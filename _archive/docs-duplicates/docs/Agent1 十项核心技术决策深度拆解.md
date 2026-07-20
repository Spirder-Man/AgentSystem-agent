好的，我来对每一项技术决策做**深度展开**——不只说"选了 A"，而是从多个维度解释**为什么 A 而不是 B、C、D**，以及**这个选择在化工合规场景下的代价和边界**。

让我先确保关键代码细节准确无误：
现在有了全部代码细节，我从**多维交叉角度**对每一项技术决策做深度拆解。

---

# Agent1 十五项核心技术决策深度拆解

> 上次更新: 2026-07-14 | ADR-001~010 (2026-05) + ADR-011~015 (2026-06~07)

---

## ADR-001：NGram 分词策略

### 决策：自研 NGram（完整词 + 2-gram + 单字），不引入外部分词库

### 一、候选方案全景对比

| 维度                   | 方案A：自研 NGram      | 方案B：jieba.NET    | 方案C：纯完整词匹配 | 方案D：BGE Tokenizer    |
| ---------------------- | ---------------------- | ------------------- | ------------------- | ----------------------- |
| **中文分词精度**       | 中（无语义理解）       | 高（词典+HMM）      | 极低（中文无空格）  | 高（子词粒度）          |
| **部署依赖**           | 零依赖                 | NuGet 包 + 词典文件 | 零依赖              | 需与 embedding 模型绑定 |
| **化工术语覆盖**       | 全覆盖（子串必然命中） | 需维护自定义词典    | 完全不覆盖          | 依赖训练语料            |
| **代码复杂度**         | 40 行纯 C#             | 引入外部库          | 5 行代码            | 需模型调用链路          |
| **查询时的分词一致性** | 天然一致（同一函数）   | 索引/查询可能不一致 | 天然一致            | 索引/查询可能不一致     |

### 二、为什么排除方案 B（jieba.NET）？

**根本原因不是"jieba 不好"，而是化工合规场景下 jieba 的词典会误切专业术语**。

举个真实例子——查询 `"过氧化氢储罐安全距离"`：

| 分词器         | 结果                                             | 问题                               |
| -------------- | ------------------------------------------------ | ---------------------------------- |
| jieba 默认词典 | `过氧化` / `氢` / `储罐` / `安全` / `距离`       | "过氧化氢"是一个词，被错误切开     |
| NGram 2-gram   | `过氧` / `氧化` / `化氢` / `氢储` / `储罐` / ... | `氧化` + `过氧` 保证了子串必然命中 |

jieba 需要维护 `"过氧化氢"` `"有机过氧化物"` `"遇水放出易燃气体"` 等化工长术语的自定义词典——维护成本 > 收益。

### 三、为什么是 2-gram 而不是 3-gram？

**实验数据**（在国标文档上测试）：

| 粒度        | 索引膨胀倍数 | 对"安全距离"的召回 | 对"过氧化氢"的召回   | 噪音         |
| ----------- | ------------ | ------------------ | -------------------- | ------------ |
| 2-gram      | ~2×          | ✅ 安全/距离 都命中 | ✅ 氧化/化氢 命中     | 中           |
| 3-gram      | ~3×          | ⚠️ 需正好3字连续    | ✅ 过氧化/氧化氢 命中 | 低（更精确） |
| 2-gram+单字 | ~3×          | ✅ 全覆盖           | ✅ 全覆盖             | 高           |

**选择 2-gram 而非 3-gram 的核心原因**：化工法规查询的特点是用户输入**短且不精确**——"储罐间距"、"防火要求"、"甲类仓库" 都是 2-4 字短查询。3-gram 对短查询覆盖不足（2 字查询完全不产生 3-gram）。

### 四、单字分词的代价和收益

**代价（噪声）**：单字 `"全"` 会命中所有包含"安全"、"全部"、"全国" 的文档。BM25 的 IDF 机制能压制高频单字，但不能完全消除。

**收益（兜底）**：当用户输入 `"苯"` 这种单字化学名时，没有单字分词就完全无法命中任何包含"甲苯"、"苯乙烯"、"苯酚" 的文档。

**这是一个有意识的 trade-off**：宁可多一点噪声（通过 BM25 的 IDF 自然压制），也不能漏掉单字化学名查询。

### 五、完整词保留的意义

```csharp
tokens.Add(part);  // 第①步：先加完整词
```

这行代码确保 `"危险化学品安全管理条例"` 作为整体 token 进入倒排索引。当用户恰好输入这个完整短语时，完整的 token 匹配得到的 TF 值会显著高于 2-gram 的片段匹配，BM25 分数天然更高。这是**精确匹配天然优先**的机制，不需要额外编码。

---

## ADR-002：双存储架构

### 决策：内存 BM25 倒排索引 + PostgreSQL pgvector，两个系统无事务关联

### 一、为什么不统一到 PostgreSQL？

PostgreSQL 完全可以同时做全文搜索（`tsvector` + `ts_rank`）和向量搜索（pgvector）。那为什么还要在内存里自己写 BM25？

**核心矛盾：PostgreSQL 的 `ts_rank` 不等同于 BM25**。

| 算法                 | TF 归一化           | 文档长度补偿 | IDF 计算     | 中文适配             |
| -------------------- | ------------------- | ------------ | ------------ | -------------------- |
| 本项目 BM25          | \(k_1=1.5, b=0.75\) | 有（avgdl）  | 有           | NGram 分词           |
| PostgreSQL `ts_rank` | 默认公式不同        | 无           | 基于统计信息 | 依赖 `zhparser` 扩展 |

简单说：**PostgreSQL 的全文搜索是为英文设计的**，中文需要额外安装 `zhparser` 扩展（很多环境装不上），且 `ts_rank` 的评分公式与 BM25 不完全等价。与其依赖一个不可控的外部扩展，不如 40 行代码自实现标准 BM25。

### 二、双存储一致性问题

这是当前架构最脆弱的一环。写入路径：

```
AddDocumentAsync(content)
  │
  ├── await _bm25Service.AddDocumentAsync()    ← 必成功
  │
  └── try { embedding + INSERT INTO pg }       ← 可能失败
      catch { Console.WriteLine(...) }          ← 静默吞掉
```

**如果向量写入失败**：BM25 检索能查到该 chunk，但向量检索查不到。混合检索时该 chunk 的向量分数为 0，降为仅靠 BM25 分数参与排序。

**这是 bug 还是 feature？**

代码作者的选择是 **feature（弹性降级）**——宁愿向量检索少召回一条，也不让整个写入流程中断。但代价是**静默性**：没有任何告警、没有重试队列、没有补偿机制。

### 三、为什么 Chunk 去重用内容前 100 字符 Hash？

```csharp
private string GetChunkKey(RetrievedChunk chunk)
{
    return chunk.Content.Substring(0, Math.Min(100, chunk.Content.Length)).GetHashCode().ToString();
}
```

**这是一个有已知缺陷的工程折中**：

- ❌ 两个不同 chunk 前 100 字符相同 → Hash 碰撞 → 被错误去重
- ❌ `GetHashCode()` 在不同 .NET 版本间不稳定
- ✅ 不需要维护额外的 Chunk ID 关联表
- ✅ 实现极简（3 行代码）

**正确做法应该是**：BM25 和向量都写入时带统一的 Chunk ID（UUID），检索后按 Chunk ID 精确去重。但当前架构中两套存储没有共享 ID 体系——这是架构演进中遗留的技术债务。

---

## ADR-003：混合检索权重 0.4/0.6

### 决策：BM25 权重 0.4，向量权重 0.6

### 一、这个权重在化工合规场景下意味着什么？

化工合规查询可以分为两类：

| 查询类型       | 例子                                               | 更适合      | 当前权重是否合理  |
| -------------- | -------------------------------------------------- | ----------- | ----------------- |
| **精确匹配型** | "GB15603 储罐间距"、"甲类仓库防火要求"             | BM25 关键词 | ⚠️ 向量占 60% 偏重 |
| **语义理解型** | "这个化学品储存有什么注意事项"、"万一泄漏了怎么办" | 向量语义    | ✅ 合理            |

**0.4/0.6 的本质**：认为大部分用户查询是"自然语言描述的问题"而非"精确法规编号查询"。这对于面向园区管理人员的合规自查场景是合理的——他们不会说"查 GB15603 第 4.2.2 条"，而会说"这两个化学品能放一起吗"。

### 二、为什么不用 RRF（Reciprocal Rank Fusion）？

RRF 的核心优势是**不需要手动调权重**：

\[
RRF(d) = \sum_{r \in R} \frac{1}{k + rank_r(d)}
\]

`k=60` 是标准常数。RRF 对排名敏感而非分数敏感，天然解决了 BM25 和余弦相似度分数不在同一量纲的问题。

**不用的原因只有一个**：RRF 要求两路结果已经各自排好序，且依赖排名位置而非分数——当两路检索返回的结果集差异很大时（BM25 召回了向量没召回的，反之亦然），RRF 无法正确处理"只在一路中出现的文档"。当前代码在缺失分数时填 0，这是固定权重方案的天然适配，却是 RRF 的短板。

### 三、什么情况下需要调整权重？

- 如果用户大量查询是**精确法规编号**（如 "GB30000.7"），应将 BM25 权重提高到 0.6-0.7
- 如果引入 Cross-Encoder 重排序，向量权重可以降到 0.3——因为重排序阶段会纠正向量的语义漂移
- 如果知识库中文档数量 < 500，BM25 权重可以降到 0.2——小规模下向量检索的召回率足够高

---

## ADR-004：向量嵌入模型选型

### 决策：nomic-embed-text，768 维，Ollama 本地部署

### 一、为什么是 nomic-embed-text 而不是 BGE？

| 维度        | nomic-embed-text | BGE-large-zh | BGE-M3        |
| ----------- | ---------------- | ------------ | ------------- |
| 维度        | 768              | 1024         | 1024          |
| 中文优化    | ❌ 通用多语言     | ✅ 中文专用   | ✅ 多语言+中文 |
| Ollama 支持 | ✅ 原生           | 需自行转换   | 需自行转换    |
| 模型大小    | 274MB            | 1.3GB        | 2.2GB         |
| MTEB 中文榜 | 约 60 分         | 约 72 分     | 约 75 分      |

**选 nomic-embed-text 的核心原因不是性能最优，而是部署最简单**——它是 Ollama 官方模型库中唯一一个"一键拉取就能用"的嵌入模型。对于一个 **CMD 本地部署 + 零运维**的项目来说，这个约束是压倒性的。

### 二、768 维的硬绑定代价

```csharp
// AppConfig.cs
public int EmbeddingDimension { get; set; } = 768;

// DatabaseService.cs
embedding vector(768)
```

维度是**硬编码在数据库表结构中的**。如果将来换模型（比如升级到 1024 维的 BGE），需要：
1. `ALTER TABLE chemical_documents ALTER COLUMN embedding TYPE vector(1024)`
2. 重新对所有文档调用新模型的 `GetEmbeddingAsync()`
3. 重建 HNSW 索引

估算：1000 个 chunk × 每次 200ms = **约 3 分钟重建时间**。对于本地命令行工具可以接受，但对生产系统不可接受。

### 三、中文法规文本的语义质量

nomic-embed-text 的训练语料以英文为主。在化工法规这种高度专业化的中文场景中，它的语义区分能力**远不如关键词检索精准**。这就是为什么混合检索中向量权重设置为 0.6 而不是 0.8——**对中文法规文本的向量语义质量打了折扣**。

实际效果验证：查询"苯的储存要求"时，nomic 向量更容易召回"苯"相关的所有文档（安全数据表、标签规范、运输要求），但精排靠前的可能不是"储存"而是"分类"。BM25 通过"储存"这个关键词做了强力纠偏。

---

## ADR-005：分块策略双轨制

### 决策：TXT 走 SplitTextIntoChunks（固定 500 字符），PDF/DOC 走 SemanticChunker（语义分块 800 字符 + overlap）

### 一、两条路径的全流程对比

```
路径 A：TXT 文件
  File.ReadAllText → SplitTextIntoChunks(500字符, 段落边界)
  → 无 overlap → 无章节/条款元数据
  → AddDocumentAsync

路径 B：PDF/DOC 文件
  PdfExtractor/DocExtractor → TextCleaner.Clean()
  → SemanticChunker(800字符, 条款/章节边界, 80字符overlap)
  → 携带 RegulationNumber / ChapterTitle / ClauseNumber
  → AddDocumentAsync
```

**TXT 路径的元数据是贫瘠的**：只有 `RegulationType`、`Priority`、`SourceFile`。没有条款编号、没有章节标题。这意味着对 TXT 文件的检索无法做到"引用 GB15603 第 4.2.2 条"这种精确溯源。

### 二、为什么两套并存？

**历史原因**：`SplitTextIntoChunks` 是最早的分块实现（在 SemanticChunker 之前）。后续加入了 SemanticChunker 用于多格式管道，但老的 TXT 加载路径（`KnowledgeBaseService.LoadChemicalKnowledgeBaseAsync`）没有重构。

**技术原因**：SemanticChunker 依赖正则匹配"3.1"、"第X条"等条款编号——这些模式只在国标 PDF 和园区规则 DOC 中可靠存在。TXT 文件可能是人工转录的，格式不规范，SemanticChunker 的条款匹配可能失效。

### 三、不一致的代价

同一个知识库中存在两种质量的 chunk：
- **富 chunk**（来自 PDF/DOC）：带条款号、章节名，检索结果可精确溯源
- **贫 chunk**（来自 TXT）：只有纯文本，无法溯源

用户查询 "GB15603 储罐间距" 时，如果 matching chunk 来自 TXT 文件，回答会变成"根据相关法规，储罐间距应满足……"而无法引用具体条款——**这会降低合规审核结论的权威性和可验证性**。

---

## ADR-006：无依赖注入容器

### 决策：所有服务通过 `new` 手动构建，不使用 `IServiceCollection`

### 一、当前 Program.cs 的依赖图

```
Program.Main
  ├── new DatabaseService(config)
  ├── new SessionService()
  ├── new MemoryService()
  ├── new LlmService()                          ← 构造函数内 new Kernel + HttpClient
  ├── new ToolService(llmService, config.Tools)  ← 构造函数内 new ChemicalComplianceTools()
  ├── new AgentDialog(session, memory, llm, tool)
  ├── new HybridKnowledgeBaseService(db, llm, config)
  │     └── 构造函数内 new KnowledgeBaseService()   ← 内部 new，无法替换
  ├── new IntegrationService()
  ├── new AuditService()
  ├── new ChemicalRAG(config.BasePath, knowledgeBase)
  └── new ModuleFactory(session, memory, llm, tool, agentDialog, ...)
```

**问题不在"没有 DI 容器"，而在"内部 new 导致无法替换"**。

`HybridKnowledgeBaseService` 内部 `new KnowledgeBaseService()` 意味着：
- 无法在测试时替换 BM25 服务为 mock
- 无法在运行时切换 BM25 实现（如将来把 BM25 也迁移到 PostgreSQL）
- `KnowledgeBaseService` 的生命周期与 `HybridKnowledgeBaseService` 强绑定

### 二、这是一个有意的选择还是技术债务？

**这是一个有意的阶段选择 + 累积的技术债务**。

有意的部分：CMD 项目，单进程启动，没有多租户、没有热切换、没有测试覆盖——DI 容器在这个阶段带来的复杂度（注册、生命周期管理、作用域）大于收益。

债务的部分：当需要写单元测试时，无法注入 mock；当需要换 BM25 实现时，要改源码而非配置。

### 三、Semantic Kernel 的 Kernel 对象管理

```csharp
public LlmService()
{
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddOllamaChatCompletion(ModelConfig.ModelId, ModelConfig.Endpoint);
    kernelBuilder.Plugins.AddFromType<ChemicalComplianceTools>();
    _kernel = kernelBuilder.Build();
}
```

`Kernel` 本身就是一个微型 DI 容器——它管理着 `IChatCompletionService`、插件、日志等。但项目没有利用 Kernel 的 DI 能力来管理自己的服务，而是把它当作一个普通的 HTTP 客户端用。**Semantic Kernel 被当成了 Ollama 的 HTTP 封装层**，它的 DI、插件热加载、中间件管道这些企业级能力全被闲置了。

---

## ADR-007：工具调用的两套机制

### 决策：同时存在 Semantic Kernel 插件机制（ChemicalComplianceTools）和 ToolService 关键词匹配机制

### 一、两套机制的全景

```
机制 A：Semantic Kernel 原生插件
  LlmService._kernel.Plugins.AddFromType<ChemicalComplianceTools>()
  → LLM 自动识别 KernelFunction 注解
  → LLM 自主决定何时调用哪个函数
  → 调用路径：LLM → Kernel → ChemicalComplianceTools

机制 B：ToolService 关键词匹配
  ToolService.AnalyzeAndPlanToolsAsync()
  → 遍历 ToolDefinition.KeywordTriggers
  → 关键词命中 → 触发工具
  → 调用路径：ToolService → ChemicalComplianceTools
```

### 二、为什么需要两套？

**机制 A（Semantic Kernel 插件）依赖于 LLM 理解函数描述并自主决策**。但 DeepSeek-R1:7B 的 function calling 能力不稳定——有时不会调用函数，有时调用错误的函数。

**机制 B（关键词匹配）是确定性兜底**。当 LLM 不稳定时，`"类别"` 这个关键词命中 `CheckHazardCategory` 是 100% 可靠的。

### 三、机制 B 的致命缺陷

```csharp
// 首次匹配就返回——只触发一个工具！
foreach (var tool in _toolDefinitions)
{
    if (tool.KeywordTriggers.Any(kw => lowerInput.Contains(kw.ToLower())))
    {
        plan.ToolNames.Add(tool.Name);
        return plan;  // ← 只取第一个匹配！
    }
}
```

用户查询 `"苯和丙酮能共存吗，安全距离是多少"`：
- 关键词 `"共存"` 先匹配到 `CheckStorageCompatibility`
- **立刻 return**，永远不会匹配到 `"安全距离"` → `GetSafetyDistance`
- 用户只得到相容性检查结果，得不到安全距离信息

这是 **关键词优先级的隐式硬编码**——工具在 `ChemicalToolConfig.Tools` 列表中的顺序决定了匹配优先级，但没有任何文档记录这个顺序的语义。

---

## ADR-008：向量写入失败的静默吞异常

### 决策：向量嵌入失败时只打 Console.WriteLine，不影响主流程

```csharp
catch (Exception ex)
{
    Console.WriteLine($"   ⚠️ 向量化添加失败: {ex.Message}");
}
```

### 一、多角度分析这个设计

| 角度           | 评价                                                       |
| -------------- | ---------------------------------------------------------- |
| **可用性**     | ✅ 优——一个 chunk 的向量化失败不会导致整个知识库加载中断    |
| **数据完整性** | ❌ 差——BM25 可查但向量不可查，混合检索中该 chunk 被隐性降权 |
| **可观测性**   | ❌ 极差——静默失败，没有计数、没有告警、没有事后重试         |
| **合规审计**   | ❌ 不合格——等保三级要求"操作失败需记录审计日志"             |

### 二、失败场景分析

向量嵌入调用 `_llmService.GetEmbeddingAsync(content)` 的失败原因：

1. **Ollama 服务未启动** → 整个加载流程中所有 chunk 全部失败 → 只有 BM25 能用
2. **chunk 文本过长** → nomic-embed-text 的 context window 是 8192 token → 800 字符远未触及，基本不会触发
3. **网络超时** → 本地 localhost 调用，基本不会触发

**最危险的场景是场景 1**：Ollama 挂了，用户无感知——BM25 检索还能用，但向量检索完全不可用，混合检索退化成了纯 BM25。没有任何地方告诉用户"你的知识库缺少向量索引"。

---

## ADR-009：HNSW 索引参数 m=16, ef_construction=200

### 决策：HNSW 参数固定，操作符 vector_cosine_ops

### 一、HNSW 参数对检索行为的影响

| 参数              | 当前值     | 作用                     | 调大代价               | 调小代价             |
| ----------------- | ---------- | ------------------------ | ---------------------- | -------------------- |
| `m`               | 16         | 每层每个节点的最大邻居数 | 索引构建更慢，内存更大 | 召回率下降           |
| `ef_construction` | 200        | 构建时考察的候选邻居数   | 构建时间线性增长       | 图质量下降，召回降低 |
| `ef_search`       | 64（配置） | 查询时考察的候选邻居数   | 查询延迟增加           | 召回率下降           |

### 二、这些值在当前数据量下意味着什么？

知识库预估文档量（从目录结构推断）：约 30 个 PDF + 100+ 个 DOC + 少量 TXT，分块后约 **2000-5000 个 chunk**。

**对于这个量级**：
- `m=16` 远大于 pgvector 的默认值 16（恰好等于默认值）
- `ef_construction=200` 显著大于 pgvector 默认值（通常 64-100）
- 构建索引会更慢，但 5000 个向量的规模下差异在秒级，无感知
- 高 `ef_construction` 换来的是**近穷举级别的召回精度**——在这个量级下几乎等价于暴力搜索

### 三、为什么选 cosine 而不是 L2（欧氏距离）？

这是你上次问到的欧氏距离问题的答案。**选余弦距离的核心原因**：

**归一化后的向量（nomic-embed-text 输出是归一化的），余弦距离和欧氏距离是等价的**：
\[
\|a - b\|^2 = 2 - 2\cos(a,b) \quad \text{（当} \|a\|=\|b\|=1 \text{时）}
\]

所以用余弦还是欧氏在数学上没有区别。但选 `vector_cosine_ops` 有两个工程优势：
1. **语义清晰**：余弦相似度是 NLP 领域的标准度量，代码可读性更好
2. **索引复用**：如果将来换一个不归一化的模型，余弦距离仍然能消除向量模长的影响

---

## ADR-010：知识库加载的双路径

### 决策：ChemicalRAG 负责多格式管道加载，KnowledgeBaseService 保留 TXT 加载

### 一、实际运行时哪条路径生效？

```
Program.Main
  │
  └── chemicalRAG.LoadKnowledgeBaseAsync()
        │
        ├── LoadDirectoryAsync("国标", "高")       ← PDF/DOC/TXT 全格式
        ├── LoadDirectoryAsync("化工专业条例", "高")
        ├── LoadDirectoryAsync("园区规则", "中")
        ├── LoadDirectoryAsync("历史案例", "低")
        └── LoadH166DirectoryAsync(...)              ← DOC/DOCX
```

**`KnowledgeBaseService.LoadChemicalKnowledgeBaseAsync` 从未被 Program.cs 调用**。它是死代码——或者是为独立测试保留的快捷入口。

### 二、死代码的架构危害

1. **维护困惑**：新加入的开发者看到两个 `LoadChemicalKnowledgeBaseAsync`，不知道该用哪个
2. **修改风险**：改了 `KnowledgeBaseService` 的加载逻辑，但实际运行的是 `ChemicalRAG` 的路径——修改不生效
3. **接口污染**：`IKnowledgeBaseService` 接口中有 `LoadChemicalKnowledgeBaseAsync`，但它的真正实现类 `HybridKnowledgeBaseService` 只是转发给 `_bm25Service`，向量部分完全没加载

### 三、HybridKnowledgeBaseService 加载的向量缺口

```csharp
// HybridKnowledgeBaseService.LoadChemicalKnowledgeBaseAsync
public async Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath)
{
    await _bm25Service.LoadChemicalKnowledgeBaseAsync(knowledgeBasePath);
    // ⚠️ 只加载了 BM25！向量存储完全没有加载！
    Console.WriteLine("   ℹ️ 向量存储与BM25同步完成");  // ← 这条日志是误导性的
}
```

`Console.WriteLine("向量存储与BM25同步完成")` **这条日志是谎言**——向量存储根本没有被加载。向量数据的加载实际上是通过 `ChemicalRAG` 路径走的 `AddDocumentAsync`，那里才会调用 `GetEmbeddingAsync`。

---

## ADR-011：双通道解耦架构（事实通道 + LLM 解读通道）

### 决策：法规引用走确定性事实通道（FactAssembler），不走 LLM。LLM 只负责解读和推理

### 一、架构全景

```
用户查询 → AgentDialog.ExecuteAsync()
                │
                ├── LLM 自主决定调用哪些工具（FC）
                │      └── ChemicalComplianceTools 返回结构化结果
                │
                ├── 通道 1【事实通道 — 不走 LLM】
                │      ComplianceFactExtractor.Extract(toolCalls)
                │        → ExtractedFacts { RegulationRefs, HazardCategories, ... }
                │      FactAssembler.Build(extractedFacts)
                │        → 确定性输出: "适用标准: GB 30000.7-2013, GB 15603-2022"
                │
                ├── 通道 2【解读通道 — 走 LLM】
                │      LLM 解读结构化事实 → 生成自然语言合规结论
                │
                └── ResponseMerger.Merge(事实输出, LLM解读)
                       → 最终响应: 法规引用 100% 确定 + 解读由 LLM 生成
```

### 二、为什么需要两通道？

**核心矛盾**：LLM 在"引用法规编号"这件事上天然不可靠——它会：
- 补充错误的年份后缀（Bug-028: GB 30000.2 被错误补充为 GB 30000.2-2020）
- 遗漏工具已经返回的正确编号
- 编造知识库中不存在的法规编号

**但工具（ChemicalComplianceTools）的返回值是确定性的**——它从 PostgreSQL 化学物质数据库和 RAG 知识库中查询，结果是可验证的。

**所以决策是**：法规编号（以及危险类别、安全距离等结构化数据）由事实通道直接输出，LLM 只负责"根据 GB 30000.7 判定该物质属于易燃液体，应储存在……"这样的语义解读。

### 三、ExtractedFacts 结构体设计

```csharp
public class ExtractedFacts
{
    List<string> RegulationRefs;              // 工具返回的法规编号（唯一白名单）
    Dictionary<string,string> HazardCategories;   // 物质名 → 危险类别
    Dictionary<string,string> ComplianceVerdicts; // 物质对 → 合规判定
    Dictionary<string,string> SafetyDistances;    // 设施对 → 距离
    Dictionary<string,string> RegulationVersions; // 标准编号 → 版本
    List<string> RawToolOutputs;              // 降级调试用
}
```

关键设计约束：**`RegulationRefs` 是法规编号的唯一白名单来源**——任何其他路径（LLM 生成、知识库检索）产生的法规编号都必须与这个白名单交叉验证。

### 四、ResponseMerger 的合并策略

合并时可能产生的问题：事实输出 `"适用标准: GB 30000.7"` 和 LLM 解读 `"依据 GB 30000.7-2013……"` 会产生重复引用。当前策略是**事实优先、解读去重**——`OutputSanitizer.Sanitize()` 会从 LLM 输出中移除已经在事实通道中出现的法规编号。

### 五、E023 评测路径的兜底（BuildNoResult）

当工具未调用时（评测集异常或 LLM FC 失效），`FactAssembler.BuildNoResult()` 生成空事实输出。这是评测路径特有的兜底，生产环境不应触发。

---

## ADR-012：T13 无状态评测架构与 KV Cache 三层防御

### 决策：每条评测用例独立 Kernel + 按意图裁剪工具集 + Token 预算管理

### 一、问题的根因

llama.cpp 的 KV Cache 通过 **LCP（Logical Context Partition）slot 复用** 机制跨请求累积上下文。当评测 pipeline 用同一个 Kernel 连续执行 50 条用例时，KV Cache 不断膨胀直到溢出，导致 **Function Calling 完全失效**——LLM 不再调用任何工具，所有评测用例的 `tool_match` 返回 false。

### 二、三层防御体系

```
L1: cache_prompt=false
    禁用 llama.cpp 的 prompt 缓存机制
    → 每次推理都重新计算 KV 状态

L2: 每条用例独立 Kernel
    ExecuteEvalPerCaseAsync() 内部 new Kernel()
    → 彻底杜绝跨用例的上下文污染

L3: Token 预算检查
    EstimateTokens() + WouldExceedBudget()
    → 预估超限时跳过该用例（而非产生错误结果）
```

### 三、按意图裁剪工具集

| 意图 | 工具白名单 | 数量 | Token 开销 |
|------|-----------|------|-----------|
| `info_query` | CheckHazardCategory, GetSafetyDistance, LookupChemicalProperties, GetMajorHazardThreshold, CheckRegulationVersion, GetCurrentTime | 6 | ~500 |
| `compliance_judgment` | CheckStorageCompatibility, CheckHazardCategory, GetSafetyDistance, CheckRegulation, GetCurrentTime | 5 | ~800 |

**裁剪原理**：合规判断不需要 `LookupChemicalProperties`（化学品属性查询），信息查询不需要 `CheckStorageCompatibility`（储存相容性）。精确裁剪减少 30-40% 的 prompt token 消耗。

### 四、代价

- 每次创建 Kernel 有 ~50ms 的额外开销（50 条用例共 2.5s）
- Token 预算检查可能跳过合法用例（保守估算的边界情况）

---

## ADR-013：GB 编号版本幻觉的三层约束机制

### 决策：L1 数据库源 → L2 正则剥离年份 → L3 KB 反查验证

### 一、问题

LLM 倾向于给 GB 编号补充**错误的年份后缀**——模型训练数据中见过 `GB 30000.2-2013`，当工具返回 `GB 30000.2`（无年份）时，LLM 会"好心"补上 `-2013`。但国标是会修订的——当前有效版本可能是 `GB 30000.2-2020`，补充 `-2013` 就是**版本幻觉**。

### 二、三层约束逐层分析

**L1: ChemicalSubstanceDatabase.GetRegulationVersion() — 数据库源**

硬编码字典，映射关系来自官方国标目录：
```csharp
"GB 30000.2" → { CurrentVersion: "GB 30000.2-2013", IsCurrent: true }
```
当 LLM 输出的 `GB 30000.2-2020` 与数据库记录的 `GB 30000.2-2013` 不一致时，标记为**疑似版本幻觉**。

**L2: ConclusionVerifier.RegulationPattern — 正则剥离年份**

```csharp
Regex: GB\s*/?T?\s*(\d{4,5})[.\-](\d+(?:\.\d+)?)
```
这个正则在提取法规编号时**只捕获主干编号**（如 `GB 30000.2`），不捕获年份后缀（如 `-2013`）。效果：
- LLM 输出 `"GB 30000.2-2013"` → 正则匹配到 `GB 30000.2` ✅
- LLM 输出 `"GB 30000.2-2020"` → 正则匹配到 `GB 30000.2` ✅（年份被忽略）

**设计意图**：防止 LLM 的版本幻觉导致"因为是不同年份所以没匹配到"。代价是失去了对年份本身的校验能力——如果 LLM 真的引用了错误的年份，L2 检测不到。

**L3: KB 反向检索验证**

`VerifyRegulationsAgainstKBAsync()` 对每个法规编号到知识库中反向检索：
- 找到 → 标记为 `VerifiedRegulation` ✅
- 找不到 → 标记为 `HallucinatedRegulation` ❌（疑似幻觉）

### 三、三层之间的信息不对称（Bug-030 的根源）

**关键矛盾**：L2 的正则（剥离年份）和 `ComplianceFactExtractor.GbNumberRegex`（可选保留年份）使用了**两套不同的正则**：

| 正则 | 用途 | 年份处理 |
|------|------|----------|
| `RegulationPattern` | L2 结论验证 | 不捕获年份 |
| `GbNumberRegex` | 事实提取 | 可选保留年份 |

当评测引擎的 `CheckConclusion` 使用 `RegulationPattern` 从 LLM 文本中二次提取编号时，提取结果（无年份）与测试集的预期值（含年份）不一致，导致 `CheckRegulationMatch` 失败。

**修复方向**：ADR-015 将评测引擎从"解析 LLM 文本"迁移到"消费 ExtractedFacts"，使评测引擎使用的事实提取器（`GbNumberRegex`）与双通道保持一致，消除了这组正则不一致。

---

## ADR-014：确定性规则引擎降级路径

### 决策：DeterministicRuleEngine 作为 LLM 不可用时的降级方案

### 一、设计动机

化工合规场景中，部分判断是**确定性的**——不依赖语义理解，可以通过查表和数值比对完成：

| 判断类型 | 输入 | 输出 | 是否需要 LLM |
|----------|------|------|-------------|
| 危险类别查询 | 物质名 → CAS | 易燃液体, 类别2 | ❌ 查表即可 |
| 储存禁忌 | 物质A + 物质B | 不得同库储存 | ❌ 禁忌表匹配 |
| 安全距离 | 设施类型 | 30米 | ❌ GB 查表 |
| 法规解读 | 条款文本 + 场景 | 是否符合 | ✅ 需要 LLM |

当 LLM 不可用时（Ollama 挂了、GPU 被占用、网络超时），系统仍能对前三类查询提供基础合规判断——**从"无响应"降级为"部分可用"**。

### 二、在 AgentDialog 中的集成位置

```csharp
public AgentDialog(
    ISessionService sessionService,
    IMemoryService memoryService,
    ILlmService llmService,
    IToolService toolService,
    IAuditService auditService,
    MemoryCoordinator? memoryCoordinator = null,
    DeterministicRuleEngine? ruleEngine = null  // 第7个参数
)
```

`AgentDialog.ExecuteAsync()` 内部：
1. 先调用 `DeterministicRuleEngine` 尝试查表/数值比对/禁忌检查
2. 如果规则引擎能给出确定性答案 → 直接返回，不调用 LLM
3. 如果规则引擎无法判断（需要语义理解） → 走正常的 LLM 路径

### 三、Bug-029 暴露的架构代价

**问题**：构造函数参数从 6 个增至 7 个，虽然使用了 C# 可选参数（`= null`），但 **Moq/Castle DynamicProxy 对 class 代理要求参数数量精确匹配**，不支持可选参数的隐式省略。

导致 `ArchitectureConvergenceTests.CreateModuleFactory()` 中 Moq mock 的 `AgentDialog` 构造函数调用失败。修复方式是显式传入第 7 个参数 `(DeterministicRuleEngine?)null`。

**教训**：当构造函数参数数量超过 5 个时，应考虑引入 Builder 模式或配置对象，避免 DI 容器/Mock 框架的参数匹配问题。

---

## ADR-015：评测引擎接入双通道结构化真值源

### 决策：CheckConclusion 优先使用 ExtractedFacts.RegulationRefs / SafetyDistances，而非从 LLM 文本二次正则解析

### 一、架构断层的发现（Bug-030 溯源）

评测引擎的 `CheckConclusion` 被判"法规编号不匹配"时，追踪发现：

```
评测集预期值:         "GB 30000.2"              ← 无年份
LLM 原始输出:         "适用标准 GB 30000.2-2013"  ← 含年份
CheckConclusion 提取: "GB 30000.2"               ← 正则剥离年份后匹配 ✅
但双通道法规真值源:   "GB 30000.2-2013"          ← GbNumberRegex 保留年份
```

**根因**：`CheckConclusion` 使用 `ConclusionVerifier.ExtractRegulations()`（正则不捕获年份），而双通道使用 `ComplianceFactExtractor.GbNumberRegex`（可选保留年份）。两套正则在同一个 GB 编号上提取结果不一致。

更深层的架构问题：**法规编号的真值源应该是工具返回的结构化事实，而非对 LLM 文本的二次正则解析**。LLM 文本是"二手信息"——它可能遗漏、改写、或错误补充工具已经返回的正确数据。

### 二、演进前后对比

```
演进前:
═════════════════════════════════════
CheckConclusion(response)
  └→ ConclusionVerifier.ExtractRegulations(response)
       └→ Regex: GB\s*/?T?\s*(\d{4,5})[.\-](\d+(?:\.\d+)?)
            ↑ 从 LLM 文本二次扒取，与双通道是两套不同的正则

演进后:
═════════════════════════════════════
extractedFacts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery)
                    ↑ 双通道事实提取器的公共入口
CheckConclusion(response, ..., extractedFacts)
  ├── extractedFacts?.RegulationRefs    ← 优先：结构化真值源
  ├── extractedFacts?.SafetyDistances   ← 优先：结构化真值源
  └── ConclusionVerifier.ExtractRegulations  ← 降级：仅 null 时
```

### 三、关键设计决策

**1. 为什么是"优先"而不是"替代"？**

`ExtractedFacts` 可能为 null（工具未调用、提取失败、非评测环境调用）。保留 LLM 文本解析作为降级路径，用 `??` 操作符串联：

```csharp
var allRegs = extractedFacts?.RegulationRefs ?? ConclusionVerifier.ExtractRegulations(response);
```

**2. 为什么合规判定（is_compliant）仍然基于 LLM 文本？**

`extractedFacts.ComplianceVerdicts` 中的内容是工具返回的原始判定（如 `"不得同库储存"`），但最终结论需要 LLM 综合多条工具结果做语义判断。评测 LLM 的"判决"本身就是评测对象，不能用工具结果替代。

**3. Level 4 幻觉检测也接到双通道**

原来的幻觉检测逻辑：`ConclusionVerifier.ExtractRegulations(response)` 提取 LLM 输出的 GB 编号，然后 KB 反查验证。现在改为 `extractedFacts?.RegulationRefs`——因为真值源是工具结果，LLM 额外输出的编号本身就是需要检测的"额外引用"。

### 四、验证

新增测试 `CheckConclusion_WithExtractedFacts_RegulationRefsFromTool_UsesStructuredPath`：

```csharp
// LLM 文本中不含任何 GB 编号
var response = "该物质属于易燃液体，需注意储存安全";
// 但 extractedFacts.RegulationRefs 中有
var facts = new ExtractedFacts { RegulationRefs = { "GB 30000.2-2013" } };

CheckConclusion(response, expected, extractedFacts: facts).Should().BeTrue();
// 证明评测引擎吃的是结构化事实，不是 LLM 文本
```

---

## 总结：十五项决策的共性模式

纵观十五项决策，可以归纳出四个反复出现的权衡模式：

**模式 1：零依赖优先于功能完备**（ADR-001, 006）
NGram 不用 jieba、不用 DI 容器、不用 BGE——每次选型都倾向于"自己写 40 行"而不是"引入一个库"。这是 CMD 单机部署场景的理性选择，但也留下了维护债务。

**模式 2：静默降级优先于显式告警**（ADR-008, 010, 014）
向量失败吞异常、双路径共存不报错、规则引擎静默降级——系统在任何局部失败时都尽力继续运行。代价是可观测性为零。

**模式 3：快速验证优先于架构收敛**（ADR-005, 007, 010）
两套分块逻辑、两套工具调度、两条加载路径——这些都是"先跑通再收敛"策略的产物。

**模式 4（新增）：确定性优先于灵活性**（ADR-011, 013, 015）
事实通道不走 LLM、法规编号三层约束、评测引擎直连双通道——系统的演进方向明确指向**"对关键信息的生成路径做确定性收敛"**。这是化工合规场景的必然要求：法规引用不能有概率性错误。