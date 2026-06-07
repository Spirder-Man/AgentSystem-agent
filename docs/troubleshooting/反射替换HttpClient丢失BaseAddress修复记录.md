# 突破框架边界时的"状态复制"意识——从一次 BaseAddress 丢失说起

> **日期**：2026-06-04  
> **核心洞察**：用反射突破框架封装后，**"状态复制"不是可选项，是必修课**。  
> 这个 Bug 本身很小（一行 `BaseAddress`），但它暴露的思维盲区适用于所有语言、所有框架。

---

## 一、先放下 Bug，谈一个更大的问题

### 1.1 这个 Bug 是怎么被制造出来的？

项目的 `InjectThinkingHandler()` 用递归反射穿透 SK 内部对象图，找到 `OllamaApiClient._client`（`HttpClient`），然后**创建一个新 `HttpClient` 替换它**——目的是在 HTTP 管道中插入 `OllamaThinkingHandler` 来注入 `think` 参数。

新 `HttpClient` 只设置了 `Timeout`，漏掉了 `BaseAddress`。SK 内部用相对 URI（`"api/chat"`）发请求，`BaseAddress` 为 `null` → 全部失败。

### 1.2 但真正的问题是：为什么我会漏掉它？

我当时的心态是：**"我要解决的 SK 的问题（注入 think 参数），不是 .NET HttpClient 的问题。"**

这个心态导致我只关注了新实例上**我主动修改的部分**（`Timeout`），而忽略了**原实例上我不关心的部分**（`BaseAddress`、`DefaultRequestHeaders`……）。但这些"不关心的部分"正是框架正常运行的前提。

### 1.3 换个框架会怎样？

假如明天项目换成 Spring Boot + RestTemplate，需要反射替换一个内部 `RestTemplate` 实例来注入拦截器：

- 原实例上有 `rootUri`、`errorHandler`、`messageConverters`……
- 如果我只关心"拦截器注入"，创建新实例时漏了 `messageConverters` → 响应反序列化全部失败

假如换成 Python FastAPI + httpx，反射替换内部 `AsyncClient`：

- 原实例上有 `base_url`、`auth`、`cookies`……
- 漏了 `auth` → 所有需认证的外部 API 调用 401

**坑会换，但踩坑的模式不会换。**

---

## 二、核心方法论：反射替换的"状态复制"检查清单

> **每当你突破框架开放 API、用反射修改内部状态时，必须完整复制原对象的关键状态。**

这不是某个框架的技巧，是**跨语言、跨框架的通用工程纪律**。

### 2.1 执行步骤

当你准备用反射替换一个框架内部对象时，按以下顺序思考：

```
Step 1: 停下来，列出原对象的所有公开属性 + 非公开字段
        ↓
Step 2: 对每一项问自己：框架的正常运行依赖这个值吗？
        ↓
Step 3: 如果依赖 → 必须在新实例上显式设置
        如果不确定 → 保守处理，也设置
        ↓
Step 4: 新实例替换完成后，旧实例是否需要 Dispose()？
```

### 2.2 通用参考表

| 被替换对象类型 | 容易遗漏的状态 | 遗漏后果 |
|-------------|--------------|---------|
| `HttpClient` | `BaseAddress`、`DefaultRequestHeaders`、`MaxResponseContentBufferSize` | 相对 URI 请求全部失败 |
| `DbConnection` | `ConnectionString`、`ConnectionTimeout` | 连接到错误的数据库或超时 |
| `Stream` / `StreamReader` | `Position`、当前 `Encoding` | 读取位置错乱或乱码 |
| `HttpClientHandler` | `Proxy`、`Credentials`、`ServerCertificateCustomValidationCallback` | 代理/认证失败 |
| ORM 的 `DbContext` | `ChangeTracker` 状态、`DatabaseFacade` 事务 | 数据不一致 |
| 任何 `IDisposable` | 旧实例是否需要 `Dispose()` | 连接泄漏、文件句柄泄漏 |

### 2.3 这跟 SK 无关

这一点值得反复强调：

- **在 SK 开放 API 范围内编程**：你不需要反射，不需要关心 `HttpClient.BaseAddress`。一切由框架保证。
- **一旦跨过反射这条线**：框架对你的保护全部失效。你取代了框架的一部分职责，状态完整性由你负责。

这不是 SK 的问题，也不是 .NET 的问题。你把任何框架的封装打穿一个洞，都需要同等的 discipline 去填补。

---

## 三、案例回放：BaseAddress 丢失的完整链路

> 以下为本次 Bug 的具体技术细节，作为第二章方法论的案例印证。

### 3.1 问题现象

评测系统（菜单 13）全部 50 条用例，每条 3 次重试，**150 次调用全部失败**：

```
❌ An invalid request URI was provided. 
   Either the request URI must be an absolute URI or BaseAddress must be set.
```

但菜单 1-7 的流式推理在此之前正常工作——这是排查被误导的关键线索。

### 3.2 State 丢失链

```
替换前 (SK 原始状态):
  OllamaApiClient._client → HttpClient
    ├── BaseAddress = http://localhost:11434/   ← SK 构造时自动设置
    ├── Timeout = 100s (默认)
    └── Handler → HttpClientHandler

替换后 (Bug 版本):
  OllamaApiClient._client → HttpClient
    ├── BaseAddress = null                      ← ❌ 丢了
    ├── Timeout = 15min                         ← ✅ 手动设置了
    └── Handler → OllamaThinkingHandler → HttpClientHandler
```

Bug 代码：

```csharp
// ❌ 创建新实例时只复制了 Timeout，漏了 BaseAddress
var newHttpClient = new HttpClient(handler)
{
    Timeout = TimeSpan.FromMinutes(15)
};
```

### 3.3 为什么流式没暴露

`HttpClient.SendAsync` 的 URI 解析逻辑：

```
if (requestUri.IsAbsoluteUri)  → 直接用，不依赖 BaseAddress
else if (BaseAddress != null)  → new Uri(BaseAddress, relativeUri) 拼接
else                           → throw
```

SK 内部对两种路径的 URI 构造方式不同：

```
非流式 (InvokePromptAsync):
  发 "api/chat"（相对）→ 依赖 BaseAddress → ❌

流式 (InvokePromptStreamingAsync):
  发 "http://localhost:11434/api/chat"（绝对）→ 不依赖 BaseAddress → ✅
```

**但这是 SK 的实现细节，版本升级可能变化，不应依赖。**

### 3.4 修复

```csharp
// ✅ 显式复制 BaseAddress，末尾加 / 做防御
var baseAddr = ModelConfig.Endpoint;
if (!baseAddr.AbsoluteUri.EndsWith("/"))
    baseAddr = new Uri(baseAddr.AbsoluteUri + "/");

var newHttpClient = new HttpClient(handler)
{
    Timeout = TimeSpan.FromMinutes(15),
    BaseAddress = baseAddr
};
```

`BaseAddress` 末尾加 `/` 的原因：你不知道框架内部用的是 `"api/chat"`（相对路径）还是 `"/api/chat"`（绝对路径）。有 `/` 两种都能正确拼接。

---

## 四、思维复盘：从"记一个坑"到"掌握一种模式"

### 4.1 第一次犯错时的思维

> "SK 框架我不熟，所以才犯了这个错。"

这个反思的方向**不完全对**。真正的问题是：

> "我在用反射突破框架封装时，没有意识到自己已经接替了框架对 `HttpClient` 生命周期管理的职责。"

SK 不熟 ≠ 会犯错。**在 SK 开放 API 内编程根本不需要知道 `BaseAddress`**。只有跨过反射这条线，这个知识才变得必要。而"跨过反射后要复制状态"这件事，跟 SK 毫无关系——它适用于所有框架。

### 4.2 下次遇到类似场景时

不需要记住"SK 的 Ollama 连接器内部用相对 URI"，而是执行第二章的检查清单：

```
1. 我要反射替换什么对象？ → 写出类型名
2. 原对象上有哪些关键状态？ → 列出属性/字段
3. 新实例上哪些我设置了？哪些可能遗漏？ → 逐一比对
4. 旧实例需要 Dispose 吗？
```

### 4.3 从"修 Bug"到"建体系"

| 修复前 | 修复后 |
|--------|--------|
| 遇到报错 → 排查 → 修一行代码 | 建立检查清单 → 反射操作前预处理 → 从源头避免 |
| 记住 SK 的 BaseAddress | 记住"状态复制"这个模式 |
| 下次换框架可能再踩 | 下次换框架自动触发检查 |

---

## 五、相关文档

- `docs/articles/Semantic_Kernel_Ollama_enable_thinking_参数注入方案探讨.md` — §3.4 加固措施表 / §7.2 HttpClient 替换代码
- `Agent1/Services/LlmService.cs` → `InjectThinkingHandler()` / `OllamaThinkingHandler`
