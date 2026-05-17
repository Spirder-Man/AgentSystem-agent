# 化工园区危化品合规审核RAG系统 技术原理深度拆解

## 一、内存索引

### 1.1 定义

内存索引是将索引数据结构完全加载到应用程序的内存（RAM）中，而非存储在磁盘的索引方式。

- 内存索引：纳秒级访问
- 磁盘索引：毫秒级访问（受IO瓶颈限制）

### 1.2 加载逻辑

```csharp
public async Task LoadKnowledgeBaseAsync(string basePath)
{
    // 1. 按业务优先级加载目录
    var dirs = new Dictionary<string, int>
    {
        { "国标", 3000 },
        { "园区规则", 2000 },
        { "历史案例", 1000 }
    };

    foreach (var (dirPath, priority) in dirs)
    {
        // 2. 读取文件并分块（500字符）
        var chunks = SplitTextIntoChunks(content, 500);

        // 3. 每个分块生成Document并加入内存索引
        foreach (var chunk in chunks)
        {
            var doc = new Document { ... };
            _documents.Add(doc);
            BuildInvertedIndex(doc);
        }
    }
}
```

## 二、倒排索引

### 2.1 数据结构

```csharp
Dictionary<string, Dictionary<int, int>>
// 外层key: 分词
// 内层: 文档ID -> 词频
```

### 2.2 构建过程

1. 对文档进行NGram分词
2. 统计每个分词在文档中的出现次数
3. 更新倒排索引

## 三、BM25算法

### 3.1 公式

```csharp
score = Σ IDF(token) × (tf × (K1+1)) / (tf + K1 × (1-B+B×len/avgLen))
```

### 3.2 参数作用

|参数|作用|
|---|---|
|K1|词频饱和度|
|B|长度归一化|

## 四、业务优先级

### 最终得分

```csharp
finalScore = bm25Score + priorityBonus
// 国标: +3000
// 园区规则: +2000
// 历史案例: +1000
```
