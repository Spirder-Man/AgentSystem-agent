# 跨边界对象替换完整性检查 Skill

> **适用时间跨度**：10年+  
> **适用技术栈**：任何语言、任何框架  
> **解决问题类型**：当用反射、装饰器、代理、容器替换、中间件接管等方式替换或包装一个现有对象时，防止因状态丢失导致不可预期的行为异常。  
> **起源案例**：在 Semantic Kernel 中用反射替换内部 `HttpClientHandler`，忘记复制 `HttpClient.BaseAddress`，导致所有请求发往错误地址。

---

## 一、核心原则

**当你替换一个对象时，你接管的不仅是它的方法，还有它身上携带的“运行时记忆”。这些记忆如果丢失，被替换的对象就会像一个失忆的人，行为彻底错乱。**

这个原则适用于任何“跨边界”操作：你跨越了框架的开放 API，进入了它的内部，你就必须承担起维护内部状态完整性的责任。

---

## 二、触发场景识别

只要满足以下任意一条，立即触发本 Skill：

| 操作类型          | 具体表现                                                     |
| ----------------- | ------------------------------------------------------------ |
| 反射替换          | 用反射获取并替换框架内部的 `Handler`、`Provider`、`Factory`、`Resolver` 等 |
| 装饰器包装        | 实现一个 `Wrapper` 类，内部持有原对象，但对外提供增强功能    |
| 动态代理/AOP      | 使用 `DispatchProxy`、`RealProxy`、`Castle DynamicProxy` 等拦截方法调用 |
| DI 容器替换       | 在依赖注入容器中 `Replace` 或 `Re-register` 某个已注册服务   |
| 中间件接管        | 在请求管道中提前 `return` 或手动处理请求，不调用后续中间件   |
| 自定义序列化/转换 | 替换默认的 `JsonConverter`、`TypeConverter` 等，接管序列化行为 |
| 继承重写非虚方法  | 通过 `new` 关键字隐藏基类方法，或通过反射修改基类行为        |

**共同特征**：你在“替代”或“接管”一个原本由框架/第三方库管理的对象，而这个对象可能携带着构造时配置之外的“运行时状态”。

---

## 三、根因分析框架

### 3.1 为什么状态会丢失？

一个对象的完整行为由两部分组成：
- **静态配置**：构造时传入的参数、注册的回调、初始化设置。这部分通常显而易见。
- **运行时状态**：对象在处理请求过程中累积的临时数据、上下文引用、统计计数、连接状态。这部分往往不体现在构造函数签名中，容易被忽视。

当你替换对象时，如果只复制了“静态配置”而忽略了“运行时状态”，新对象就会丢失关键上下文。

### 3.2 典型丢失状态分类

| 类别           | 示例                                                         |
| -------------- | ------------------------------------------------------------ |
| 连接与地址     | `HttpClient.BaseAddress`、`DbConnection.ConnectionString`、`gRPC Channel` 地址 |
| 超时与策略     | `HttpClient.Timeout`、重试策略、熔断器状态                   |
| 请求上下文     | `HttpContext.User`、`Items`、`TraceIdentifier`、`CancellationToken` |
| 元数据与头部   | `DefaultRequestHeaders`、gRPC metadata、消息属性             |
| 生命周期与事件 | `IDisposable` 链、事件订阅、`StateChanged` 回调              |
| 统计与计数器   | 性能计数器、连接池计数器、序列化器的循环引用跟踪             |
| 线程关联       | `SynchronizationContext`、`ExecutionContext`                 |
| 缓存状态       | 内部缓存、LRU 队列、哈希冲突解决链                           |

**核心问题**：这些状态在原对象上可能是通过属性或方法获取的，你的替换代码如果没有显式读取并迁移它们，它们就永远丢失了。

---

## 四、状态完整性检查清单（强制执行）

执行任何对象替换操作前，必须逐项完成此清单：

### 4.1 信息收集
- [ ] **获取原对象类型**：原对象的完整类型是什么？有没有继承链？
- [ ] **列出所有公开属性**（GetProperties()），包括静态和实例。
- [ ] **列出所有公开字段**（GetFields()），有些框架用字段存储状态。
- [ ] **检查接口实现**：原对象是否实现了 `IDisposable`、`IAsyncDisposable`、`ICloneable`、`ISupportInitialize` 等？
- [ ] **检查配置入口**：除了属性，有没有通过 `Configure()` 方法、`Options` 对象等方式注入的配置？

### 4.2 状态分类
- [ ] **标记静态配置**：哪些状态是创建后就不可变的？（如 `BaseAddress`）
- [ ] **标记运行时状态**：哪些状态在处理请求时会变化？（如 `Timeout` 可能被动态调整、`DefaultRequestHeaders` 可能被累积添加）
- [ ] **标记内部依赖**：原对象是否引用了其他内部对象？这些对象是否需要一并替换或复制？

### 4.3 迁移决策
- [ ] **必须复制的状态**：对影响核心功能的状态，显式复制到新对象。
- [ ] **可以忽略的状态**：明确记录哪些状态经评估可以忽略，并注明理由。
- [ ] **不可直接复制**：某些状态可能依赖特定上下文（如 `HttpContext`），需设计替代方案。
- [ ] **生命周期管理**：如果原对象是 `IDisposable`，确保新对象也有正确的生命周期，并在替换后释放原对象。

### 4.4 验证
- [ ] **行为一致性测试**：执行相同输入，对比原对象和新对象的输出、副作用、日志。
- [ ] **异常路径测试**：模拟异常情况，检查新对象的行为是否与原对象一致（如超时异常类型）。
- [ ] **并发测试**：如果有状态被多线程访问，验证线程安全。

---

## 五、实战案例：从具体到抽象

### 案例 1：反射替换 HttpClientHandler（原始案例）

**背景**：SK 内部用 `HttpClient` 调用 LLM，我们需要为每个请求添加 `think: false` 参数，但 SK 未开放修改 Handler 的 API。于是通过反射获取内部的 `HttpClient`，替换其 `Handler`。

**Bug**：替换后的 `HttpClient` 丢失了 `BaseAddress`，导致请求发到了空地址。

**根因**：`HttpClient.BaseAddress` 是独立于 `Handler` 的运行时状态，替换 Handler 时未复制。

**修复**：在替换前保存 `OriginalClient.BaseAddress`，替换后赋值给新 `HttpClient`。

**范式应用**：这次修复只解决了一个具体问题，但提炼出的清单可以预防未来所有类似问题。

### 案例 2：装饰器包装 ILogger

**场景**：你想为所有日志添加一个统一前缀，于是实现了一个 `LoggerDecorator : ILogger`，内部持有 `_innerLogger`，在 `Log()` 方法前添加前缀。

**潜在陷阱**：
- `ILogger.BeginScope()` 返回的 `IDisposable` 可能被用于构建日志上下文。装饰器必须透传 `BeginScope`，否则下游丢失上下文。
- 如果 `_innerLogger` 实现了 `ISupportExternalScope`，装饰器可能也要实现并桥接。
- `_innerLogger` 可能注册了 `IDisposable`，当外部容器释放装饰器时，内部 logger 也应被释放。

**清单触发**：装饰器是典型的“替代”模式，必须执行清单。

### 案例 3：中间件短路

**场景**：你写了一个认证中间件，如果 Token 无效，直接返回 401，不调用 `next()`。

**潜在陷阱**：
- 后续中间件可能设置了 `Response.Headers` 里的安全头（如 `X-Content-Type-Options`），短路后这些头缺失。
- 请求的 `TraceIdentifier`、`Items` 可能未被清理，导致内存泄漏或关联错误。

**清单触发**：中间件短路是“接管”部分管道，原管道后续步骤的状态被跳过。

### 案例 4：DI 容器替换注册

**场景**：你替换了默认的 `IEmailSender` 为你的实现。

**潜在陷阱**：
- 原服务可能注册为 `Singleton`，你替换成了 `Scoped`，导致生命周期不匹配。
- 如果原服务被其他地方通过 `IEnumerable<IEmailSender>` 批量注入，替换后可能丢失集合顺序，或未加入集合。

**清单触发**：容器替换本质是“替代”已存在对象，必须检查生命周期和批量注入。

---

## 六、反向推导：预防未发生的问题

基于本范式，可以预测以下你尚未遇到但未来可能发生的 Bug：

1. **装饰器包装后 `using` 语句失效**  
   你的装饰器实现了 `IDisposable`，但 `Dispose()` 里忘了调用内部对象的 `Dispose()`，导致资源泄漏。

2. **代理类丢失原始异常类型**  
   你做了 AOP 异常处理，但所有异常都被包装成 `TargetInvocationException`，上层调用者无法按原异常类型捕获。

3. **替换序列化器后循环引用检测失效**  
   你替换了 JSON 序列化器，忘了设置 `ReferenceHandler`，导致序列化对象循环引用时栈溢出。

4. **接管认证中间件后 `HttpContext.User` 丢失后续授权所需角色声明**  
   你在认证中间件提前返回时，没保留 `User` 对象，导致授权中间件无法提取角色。

5. **反射替换内部池化对象后，池化机制失效**  
   你替换了连接池中的一个连接对象，但池的计数器未更新，导致池误判可用连接数。

**如何使用这份推导**：当你做类似操作时，事先看一遍这些潜在问题，在设计中规避。

---

## 七、技术债务记录模板

每次实施此类操作后，必须留下技术债务记录，方便未来接手者或你自己半年后理解：

```markdown
## 技术债务：反射替换 [对象名]

**目的**：[为什么要替换，业务原因]
**依赖对象**：[被替换的内部对象完整限定名]
**框架版本**：[当前使用的框架版本号，已验证兼容]
**修改方式**：[反射/装饰器/代理等具体手段]
**迁移状态清单**：
  - [复制/桥接的状态1]
  - [复制/桥接的状态2]
  - [评估后忽略的状态，附理由]
**验证测试**：[测试类名.测试方法名] 可验证完整性
**失效条件**：[框架升级后何种变化会导致此代码失效]
**失效检测方式**：[如何快速发现失效，如特定测试失败、日志告警]
**恢复计划**：[如果失效，应急替代方案或回滚步骤]
```

---

## 八、常见对象状态丢失速查表

| 被替换对象              | 最容易丢失的状态                                             | 检查方式      |
| ----------------------- | ------------------------------------------------------------ | ------------- |
| `HttpClient`            | `BaseAddress`, `Timeout`, `DefaultRequestHeaders`            | 查看属性列表  |
| `HttpClientHandler`     | `Proxy`, `CookieContainer`, `ServerCertificateCustomValidationCallback` | 查看属性+事件 |
| `DbConnection`          | `ConnectionString`, `State`, `Statistics`                    | 属性+接口     |
| `DbCommand`             | `Transaction`, `Parameters`, `CommandTimeout`                | 属性          |
| `Stream`                | `Position`, `Length`, `CanSeek`                              | 属性          |
| `ILogger`               | `Scopes`, `ExternalScopeProvider`                            | 接口+实现检查 |
| `HttpContext`           | `User`, `Items`, `RequestAborted`, `TraceIdentifier`, `Session` | 属性          |
| `JsonSerializerOptions` | `Converters`, `ReferenceHandler`, `DefaultIgnoreCondition`, `Encoder` | 属性          |
| `Channel<T>` (gRPC)     | `ResolvedTarget`, `CallOptions` 中的 `Headers`/`Deadline`    | 属性+扩展方法 |
| `IServiceProvider`      | `Dispose()` 时需释放所有 `Scoped` 服务                       | 容器生命周期  |

---

## 九、十年后仍然有价值的理由

- **技术栈会变，但“替换对象”的模式永恒**。无论是今天的 .NET 8 + SK，还是十年后的某个 AI 框架 + 新语言，只要存在“框架不开放 API，你需要黑进去”的场景，这个 Skill 就依然适用。
- **状态完整性是软件工程的底层逻辑**。它不依赖于反射语法、不依赖于 HTTP 协议，只依赖于一个常识：**对象是数据+行为的封装，替换它时不能丢掉数据。**
- **清单思维跨越时间**。即使将来 AI 可以生成 99% 的代码，这份清单依然是你向 AI 下达约束指令的依据——你可以对 AI 说：“在替换对象时，执行这份跨边界状态完整性检查清单。”

---

## 十、延伸思考：状态完整性为什么容易被忽视？

1. **文档缺失**：框架内部对象不对外暴露，自然没有文档告诉你它有哪些状态。
2. **状态不可见**：很多状态是在对象内部悄悄累积的，只有通过调试器或反射才能看到。
3. **成功经验误导**：过去简单的替换没出事，让我们误以为替换总是安全的，直到遇到复杂对象。
4. **测试盲区**：单元测试通常 Mock 掉了完整上下文，只测核心逻辑，丢状态的问题只有在集成测试或生产环境才暴露。

**因此，这份 Skill 的价值不仅在于告诉你“要复制状态”，更在于让你意识到：任何看起来简单的对象替换，都必须在充分审查原对象后，才能动手。**

---

**最终，把这份 Skill 存在你的知识库里。十年后，当你面对一个全新的技术栈，需要又一次“黑进去”时，重新打开它，你会感谢今天做了这次抽象提炼的自己。**

# 跨边界对象替换完整性检查 Skill — 百例经典问题佐证

> 每个案例用一句话概括：**替换了什么 → 丢了什么状态 → 导致了什么后果**  
> 目的：用具体实例让抽象范式变得可感可知，方便AI在不同场景中精准匹配并触发此Skill。

---

## 一、HTTP 客户端与网络通信（1-15）

1. **反射替换 HttpClient 的 Handler** → 丢失 `BaseAddress` → 所有请求发往空地址（原始案例）
2. **装饰器包装 HttpClient** → 丢失 `Timeout` → 请求永不超时，线程池耗尽
3. **替换 HttpClientHandler** → 丢失 `Proxy` 配置 → 内网请求走不了代理，超时
4. **替换 HttpClientHandler** → 丢失 `CookieContainer` → 登录态丢失，每次请求都返回 401
5. **替换 HttpClientHandler** → 丢失 `ServerCertificateCustomValidationCallback` → HTTPS 证书验证失败
6. **包装 HttpClient 添加重试逻辑** → 丢失 `DefaultRequestHeaders` 中的 `Authorization` → 重试请求没有 Token
7. **替换 gRPC Channel** → 丢失 `ResolvedTarget` 地址 → 请求发到错误的服务器
8. **替换 gRPC CallOptions** → 丢失 `Headers` 中的 trace-id → 分布式追踪断裂
9. **包装 WebSocket 客户端** → 丢失 `KeepAliveInterval` → 连接被服务端主动断开
10. **替换 SignalR HubConnection** → 丢失 `ConnectionToken` → 无法重连
11. **替换 HttpClient 的 MessageHandler 管道** → 丢失 `DelegatingHandler` 链 → 日志中间件失效
12. **替换 SocketsHttpHandler** → 丢失 `MaxConnectionsPerServer` → 连接池溢出
13. **替换 HttpClient 的默认编码** → 丢失 `UTF-8` BOM 设置 → 服务端解析乱码
14. **替换 DNS 解析器** → 丢失 DNS 缓存 → 每次请求都重新解析，延迟暴增
15. **替换 TCP KeepAlive 配置** → 丢失保活间隔 → 长连接被防火墙断开

---

## 二、数据库连接与 ORM（16-30）

16. **包装 DbConnection** → 丢失 `ConnectionString` → 连不上数据库
17. **包装 DbConnection** → 丢失 `State` → ORM 认为连接未打开，重复 Open 报错
18. **替换 DbCommand** → 丢失 `Transaction` → SQL 不在事务中执行，数据不一致
19. **替换 DbCommand** → 丢失 `Parameters` → SQL 参数化失效，SQL 注入风险
20. **替换 DbCommand** → 丢失 `CommandTimeout` → 慢查询永不超时
21. **包装 DbDataReader** → 丢失 `Depth` 层级信息 → 嵌套读取失败
22. **替换 Entity Framework 的 DbContext** → 丢失 `ChangeTracker` 状态 → 保存时漏数据
23. **替换 DbContext 的连接策略** → 丢失 `ExecutionStrategy` → 瞬态故障不复重试
24. **包装 IDbConnection 添加日志** → 丢失 `Statistics` 统计信息 → 性能监控失效
25. **替换 Dapper 的 TypeHandler** → 丢失自定义映射 → 实体属性值写不进去
26. **替换 DbProviderFactory** → 丢失 Provider 特有的 SQL 方言 → 生成的 SQL 不兼容
27. **替换连接池中的连接** → 丢失池计数器 → 池子误判可用连接数，分配失败
28. **包装 DbTransaction** → 丢失 `IsolationLevel` → 隔离级别变成默认，并发行为改变
29. **替换 CommandBehavior** → 丢失 `SingleRow` 模式 → 读取了多余数据
30. **替换 DatabaseFacade 的日志** → 丢失 `EnableSensitiveDataLogging` 开关 → 敏感数据泄露到日志

---

## 三、日志系统（31-40）

31. **装饰器包装 ILogger** → 丢失 `BeginScope()` 返回的上下文 → 日志丢失 TraceId
32. **装饰器包装 ILogger** → 丢失 `ExternalScopeProvider` → 第三方日志组件读不到范围信息
33. **替换 LoggerFactory** → 丢失已注册的 `ILoggerProvider` → 部分日志通道静默失效
34. **替换 Serilog 的 Sink** → 丢失 `MinimumLevel` 覆盖 → 大量调试日志涌入生产
35. **替换 LogMessage 格式化器** → 丢失 `Exception` 参数 → 异常堆栈不输出
36. **包装 ILogger 添加前缀** → 丢失 `EventId` → 告警系统无法按事件 ID 分类
37. **替换 NLog 的 Target** → 丢失 `AsyncWrapper` 异步配置 → 日志写入阻塞业务线程
38. **替换日志过滤规则** → 丢失 Namespace 过滤 → 第三方库的噪音日志淹没关键信息
39. **替换日志的 Layout** → 丢失 `${threadid}` → 并发问题无法定位线程
40. **替换日志的 Enricher** → 丢失机器名/进程 ID → 分布式环境无法区分日志来源

---

## 四、序列化与反序列化（41-55）

41. **替换 JSON 序列化器** → 丢失 `ReferenceHandler.Preserve` → 循环引用栈溢出
42. **替换 JSON 序列化器** → 丢失 `DefaultIgnoreCondition` → 空值字段突然全输出，接口体积膨胀
43. **替换 JSON 序列化器** → 丢失 `Encoder` → 特殊字符如 `<script>` 不转义，XSS 风险
44. **替换 JSON 序列化器的 Converters** → 丢失自定义日期格式 → API 返回的日期格式突变
45. **替换 XML 序列化器** → 丢失 `XmlNamespaceManager` → 命名空间解析失败
46. **替换 Protobuf 序列化器** → 丢失 `FieldOrder` 映射 → 字段值错位
47. **替换 MessagePack Formatter** → 丢失 `Resolver` → 自定义类型无法序列化
48. **替换 YAML 序列化器** → 丢失多文档模式 → 只输出第一个文档
49. **替换 CSV Writer** → 丢失 `Delimiter` 配置 → 制表符变成逗号，下游解析失败
50. **替换 Enum 序列化规则** → 丢失 `StringEnumConverter` → 枚举变成数字，接口语义不清
51. **替换 DateTime 序列化** → 丢失 `DateTimeZoneHandling` → UTC 变成本地时间，跨时区错误
52. **替换 Dictionary 序列化** → 丢失 `KeyValuePair` 的排序 → 哈希签名验证失败
53. **替换序列化器的 ContractResolver** → 丢失属性的 `JsonProperty` 别名 → 字段名突变
54. **替换序列化深度限制** → 丢失 `MaxDepth` 设置 → 深层对象序列化截断
55. **替换 Stream 序列化** → 丢失 `LeaveOpen` 标记 → 序列化完 Stream 被意外关闭

---

## 五、依赖注入与容器（56-65）

56. **在 DI 容器中 Replace 服务** → 丢失原注册的 `Lifetime` → Singleton 变 Transient
57. **在 DI 容器中 Replace 服务** → 丢失 `IEnumerable<T>` 批量注入的注册顺序
58. **替换 ServiceProvider** → 丢失 `Scoped` 生命周期的追踪 → Dispose 时释放不干净
59. **替换 IServiceScopeFactory** → 丢失 Scoped 服务的缓存 → 每次解析都创建新实例
60. **替换 OpenGeneric 注册** → 丢失泛型约束 → 错误的类型也能解析，运行时报错
61. **装饰器注册** → 丢失 `IDisposable` 的传递 → 服务释放时装饰器内对象泄漏
62. **替换 Options 配置** → 丢失 `PostConfigure` 回调 → 配置校验逻辑跳过
63. **替换 IConfiguration** → 丢失 `ReloadToken` 变更监听 → 配置热更新失效
64. **替换 IHostedService** → 丢失 `StartAsync` 的 `CancellationToken` → 优雅关闭不响应
65. **替换 IHttpClientFactory** → 丢失已配置的 `PolicyHandler` → 重试和熔断全丢

---

## 六、HTTP 上下文与请求管道（66-75）

66. **中间件提前 return（短路）** → 丢失 `HttpContext.User` → 后续授权中间件无法提取角色
67. **中间件提前 return** → 丢失 `HttpContext.Items` 中的请求级缓存 → 性能下降
68. **中间件提前 return** → 丢失 `TraceIdentifier` → 请求追踪断裂
69. **中间件提前 return** → 丢失 `Response.Headers` 安全头 → 点击劫持防护失效
70. **替换 HttpContext 的 Response.Body** → 丢失原始 Stream 的 `CanSeek` → 后续中间件读取失败
71. **替换 HttpContext 的 Request.Body** → 丢失 `ContentLength` → 模型绑定读不到数据
72. **包装 HttpContext.Session** → 丢失 `IsAvailable` → 在无 Session 场景下抛异常
73. **替换 AuthenticationHandler** → 丢失 `ClaimsIssuer` → Token 签发者验证失败
74. **替换 AuthorizationHandler** → 丢失 `Requirement` 的类型信息 → 授权规则匹不上
75. **替换 RateLimiter** → 丢失 `ReplenishmentPeriod` → 限流策略完全错乱

---

## 七、线程与并发（76-85）

76. **替换 TaskScheduler** → 丢失 `MaximumConcurrencyLevel` → 并行度失控
77. **替换 SynchronizationContext** → 丢失 UI 线程上下文 → `await` 后回不到 UI 线程，死锁
78. **替换 CancellationTokenSource** → 丢失 `CancelAfter` 定时取消 → 永不超时
79. **包装 AsyncLocal<T>** → 丢失异步流中的值 → TraceId 在子线程中丢失
80. **替换 ThreadLocal<T>** → 丢失线程退出时的清理 → 内存泄漏
81. **替换 ConcurrentDictionary** → 丢失原有的 `IEqualityComparer` → 键冲突
82. **替换 Channel<T>** → 丢失 `BoundedChannelOptions` → 背压策略失效
83. **替换 SemaphoreSlim** → 丢失当前的 `CurrentCount` → 并发控制完全错乱
84. **替换 ReaderWriterLockSlim** → 丢失递归策略 → 可重入锁变成死锁
85. **替换 ExecutionContext** → 丢失 `AsyncLocal` 的值 → 异步调用链的上下文全丢

---

## 八、IO 与流（86-95）

86. **包装 Stream** → 丢失 `Position` → 写入位置错误，数据覆盖
87. **包装 Stream** → 丢失 `CanSeek` 能力标记 → 回退操作失败
88. **包装 Stream** → 丢失 `Length` → 无法预分配缓冲区
89. **替换 FileStream** → 丢失 `FileShare.Read` → 其他进程无法读取文件
90. **替换 NetworkStream** → 丢失 `ReadTimeout` → 读取永不超时
91. **替换 CryptoStream** → 丢失 `FlushFinalBlock` 标记 → 解密不完整
92. **替换 BufferedStream** → 丢失内部缓冲区 → 性能退化成逐字节读写
93. **包装 StreamReader** → 丢失 `CurrentEncoding` → 切换编码后读取乱码
94. **替换 MemoryStream** → 丢失 `TryGetBuffer` 的 `ArraySegment` → 零拷贝优化失效
95. **替换 Pipe 的 Writer** → 丢失 `UnflushedBytes` → 数据残留在管道不发出

---

## 九、事件、回调与委托（96-100）

96. **替换事件发布者** → 丢失事件订阅列表 → 所有订阅者收不到消息
97. **包装 MulticastDelegate** → 丢失 `GetInvocationList()` 的单个委托 → 异常时不能逐个调用
98. **替换 PropertyChanged 事件** → 丢失 `INotifyPropertyChanged` 订阅 → WPF 绑定不刷新
99. **替换 IChangeToken 的回调** → 丢失 `RegisterChangeCallback` → 配置变更后不通知
100. **替换 AppDomain.UnhandledException 处理器** → 丢失之前的全局异常日志 → 崩溃后无记录

---

## 使用方式

在 AI 辅助开发时，将此文件作为 Skill 的一部分加载。当你或 AI 遇到以下信号词时，自动触发并匹配对应案例：

**信号词**：
- 反射替换、装饰器、代理、包装、接管、短路、拦截
- 动态代理、AOP、中间件提前返回
- DI 容器替换、重新注册、服务覆盖
- 自定义序列化器、转换器、格式化器
- 流包装、连接包装、命令包装

**匹配逻辑**：
1. 识别操作类型（替换/包装/接管/短路）
2. 找到相关分类（HTTP/数据库/日志/序列化等）
3. 对照案例表，检查是否有类似状态丢失风险
4. 执行状态完整性检查清单

---

## 速查索引

| 操作类型           | 主要风险分类 | 案例编号 |
| ------------------ | ------------ | -------- |
| 反射替换 Handler   | HTTP 网络    | 1-15     |
| 包装数据库连接     | 数据库 ORM   | 16-30    |
| 装饰器包装 ILogger | 日志系统     | 31-40    |
| 替换序列化器       | 序列化       | 41-55    |
| 容器服务替换       | 依赖注入     | 56-65    |
| 中间件短路         | HTTP 上下文  | 66-75    |
| 替换同步原语       | 线程并发     | 76-85    |
| 包装 Stream        | IO 流        | 86-95    |
| 替换事件委托       | 事件回调     | 96-100   |

---

**最终，这份百例清单与之前的 Skill 正文形成互补：正文讲“为什么”和“怎么做”，案例讲“在哪些场景下会出问题”。两者结合，构成一套完整的“跨边界对象替换完整性检查”知识体系。**