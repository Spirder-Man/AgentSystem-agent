# 化工园区危化品合规审核RAG系统技术原理深度解析

## 一、系统整体架构

```
Main() → 实例化核心服务 → 主循环 → RunChemicalRAGTest() → 加载知识库 → 检索
```

### 核心模块

1. **ChemicalRAG**（业务层）- 化工业务逻辑
2. **KnowledgeBaseService**（算法层）- BM25检索算法
3. **Document**（数据载体）- 文档内存表示

## 二、核心字段设计

### 2.1 ChemicalRAG

|字段|类型|设计价值|
|---|---|---|
|`_knowledgeBase`|`IKnowledgeBaseService`|依赖倒置原则(DIP)，低耦合|
|`_knowledgeBasePath`|`string`|路径配置化|

### 2.2 KnowledgeBaseService

|字段|类型|设计价值|
|---|---|---|
|`_documents`|`List<Document>`|内存索引，快速检索|
|`_termDocFreq`|`Dictionary`|**倒排索引**，O(1)查找|
|`_avgDocLength`|`double`|BM25参数|
|`K1`/`B`|`const double`|BM25超参数|

### 2.3 Document

|字段|设计价值|
|---|---|
|`Id`|文档唯一标识|
|`Content`|原始文本|
|`Tokens`|分词结果缓存|
|`TermFreq`|词频统计缓存|
|`Metadata`|扩展字段|

## 三、核心技术流程

### 3.1 知识库加载

1. 按「国标→园区规则→历史案例」顺序加载
2. 文件分块处理（500字符）
3. 构建倒排索引
4. 计算平均文档长度

### 3.2 两阶段检索

1. **BM25粗召回**：基于关键词相关性
2. **业务优先级重排序**：国标+3000分，园区规则+2000分，历史案例+1000分

## 四、设计原则

|原则|应用|
|---|---|
|DIP依赖倒置|ChemicalRAG依赖IKnowledgeBaseService|
|SRP单一职责|KnowledgeBaseService只负责检索|
|OCP开闭原则|Document的Metadata支持扩展|
