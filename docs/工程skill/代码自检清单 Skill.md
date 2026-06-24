# 代码自检清单 Skill

> **文档版本**：v2.0（深度扩展版）  
> **深度扩展**：2026-06-24 — 基于 Agent1 项目 30 个实战 Bug 提炼的 C# null 安全、并发安全、Key 一致性自检清单  
> **关联文档**：[P0-P1修复详细技术文档](../troubleshooting/P0-P1修复详细技术文档.md) | [RAG工程Bug修复笔记](../troubleshooting/RAG工程Bug修复笔记_2026-05-26.md)

> **适用对象**：所有编写生产级代码的开发者，尤其适合在 AI 辅助编程时作为质量约束。  
> **使用时机**：每完成一个函数或一个文件后，花 30 秒逐项自检。  
> **核心理念**：代码是写给三个月后的自己看的。好的代码不需要注释解释"做了什么"，坏的代码加再多注释也救不回来。

---

## 一、过度工程化检测

### 核心原则
**“先直写，再抽象”**。第一版用最朴素的方式实现，只有当同一个模式出现第三次时，才考虑抽象。YAGNI（You Aren't Gonna Need It）——你现在不需要的东西，就不要写。

### 触发信号
- 一个功能的接口数量超过了实现行数
- 为了“将来可能需要”而加的抽象层超过 2 层
- 一个类的职责描述超过 10 个字
- 写代码时心里想的是“这个设计模式好优雅”而不是“这解决了什么业务问题”

### 自检问题
- 如果把这个功能删掉重写，3 行代码能搞定吗？
- 这段代码在运行时，有多少比例的时间花在你的业务逻辑上，多少花在框架胶水上？
- 你现在写的这个抽象，在未来三个迭代里真的会被用到吗？

### 典型案例（1-20）

1. **问候功能**：为打印一句“Hello World”创建了 `IGreetingStrategy → FormalGreeting → GreetingContext → GreetingFactory`，四个文件加起来 80 行
2. **配置读取**：为读取一个 `appsettings.json` 值，创建了 `IConfigurationReader → ConfigurationReader → ConfigurationReaderOptions → ConfigurationReaderFactory`
3. **字符串拼接**：为拼接“姓+名”，创建了 `INameJoiner → NameJoiner → NameJoinerOptions`
4. **日志包装**：为给日志加一个前缀，创建了 `ILoggerDecorator → LoggerDecorator → LoggerDecoratorOptions`，而 Serilog 的 Enricher 一行就解决了
5. **数据库查询**：单表查询用了 Repository → UnitOfWork → Specification 模式，而 SQL 只有 `SELECT * FROM Users WHERE Id=1`
6. **API 路由**：只有 3 个 Controller，却创建了 `BaseController<T> → CrudController<T> → UserController`
7. **验证逻辑**：验证手机号用了 FluentValidation + 自定义 Validator + ValidationPipeline，正则表达式一行就够了
8. **DTO 映射**：3 个字段的 DTO 用了 AutoMapper Profile + MappingConfig + IMapper 注入
9. **异常处理**：为捕获一种异常创建了 `ExceptionHandlerFactory → IExceptionHandler → GlobalExceptionHandler`，而不是直接 try-catch
10. **缓存逻辑**：缓存一个固定值用了 `ICacheProvider → RedisCache → CachePolicy → CacheKeyGenerator`
11. **菜单命令**：每个菜单项都是一个 Command 类 + Handler + Memento 快照，而只有 5 个菜单项
12. **管道模式**：为 2 步处理创建了 `IPipeline<T> → PipelineBuilder<T> → PipelineStep<T>`
13. **事件总线**：只有 3 个事件类型，却用了 MassTransit + RabbitMQ + Saga
14. **状态机**：用 Stateless 库建模一个只有“开/关”两种状态的开关
15. **依赖注入**：为注入一个配置值创建了 `IOptions<T>` + `Configure<T>` + `PostConfigure<T>`，而不是直接 new
16. **工厂模式**：只为创建一种对象创建了 Factory，而构造函数就能解决
17. **适配器模式**：为对接一个外部 API 创建了 `IExternalApi → ExternalApi → ExternalApiAdapter → ExternalApiAdapterFactory`，HttpClient 直接调就行
18. **策略模式**：只有 2 种策略却用了策略模式 + 上下文 + 工厂
19. **观察者模式**：只有一个订阅者却实现了完整的事件发布/订阅框架
20. **微服务架构**：一个用户模块就拆成了 5 个微服务，每个只有 50 行代码

---

## 二、幽灵代码检测

### 核心原则
**“删除是优化，不是浪费”**。删掉未使用的代码，少一处需要维护、测试、理解的地方。Git 会记住历史，不需要你替它守墓。

### 触发信号
- 项目迭代中删了调用方，但忘了删函数
- 为“完整性”预留了 CRUD，但业务只用到了 Read
- AI 生成代码时顺手生成了辅助函数，从未被调用

### 自检问题
- 这个方法有调用方吗？（用 IDE 的 Find Usages 检查）
- 那个为“未来扩展”留的钩子，现在用上了吗？
- 这段代码对应着哪个需求文档或用户故事？如果找不到，就该删。

### 典型案例（21-40）

21. **预留接口**：定义了 `IUpdateHandler`、`IDeleteHandler`，但系统只实现了 `IQueryHandler`
22. **AI 生成的辅助函数**：AI 帮你生成 `GetUserById` 时，顺手生成了 `GetUserByEmail`、`GetUserByPhone`，从未调用
23. **旧版兼容代码**：`ProcessDataV1()`、`ProcessDataV2()`、`ProcessData()`，V1 早就不调用了
24. **示例方法**：教程里的 `SampleMethod()` 留在了生产代码里
25. **过时的枚举值**：`OrderStatus.Draft` 定义了但业务逻辑从未产生过草稿状态
26. **废弃的业务逻辑**：积分兑换功能下线了，但 `CalculatePoints()` 还在代码里
27. **重复的工具函数**：`StringUtils.IsNullOrEmpty()` 和 `StringHelper.IsEmpty()` 做同一件事，只用了一个
28. **预留给“未来版本”的 API**：`/api/v2/users` 创建了但从未完成，只返回 501
29. **调试用代码**：`Debug.WriteLine("走到这里了")` 留在提交里
30. **Mock 数据**：`var tempList = new[] {"test1","test2"}` 在正式代码里
31. **禁用的功能开关**：`if(FEATURE_NEW_UI == false) return;` 整个代码块永不执行
32. **未使用的构造函数重载**：定义了好几个重载，只用了默认的
33. **过时的常量**：`const string OLD_API_KEY = "xxx"` 早就换了新 Key，旧的还在
34. **历史遗留的扩展方法**：`ToFormattedString()` 是旧版格式化逻辑，新版改用 `ToJson()` 了
35. **未使用的 NuGet 包**：AutoMapper 装了但全部用手动赋值
36. **未使用的 using 语句**：`using System.Xml` 等从未用到
37. **空的事件处理器**：`OnDataChanged += (s,e) => {}` 占位用
38. **未完成的注释代码块**：一大段被注释掉的旧逻辑，注释里写着“先留着”
39. **废弃的配置文件**：`appsettings.Development.json` 里的配置项在代码中已删除
40. **多个版本的同一个 SQL 脚本**：`migration_v1.sql`、`migration_v1_fixed.sql`、`migration_v1_final.sql`

---

## 三、假注释检测

### 核心原则
**“代码即文档，注释只解释为什么，不解释是什么”**。如果注释可以用一个命名良好的变量或函数替代，删注释改命名。

### 触发信号
- 代码被修改了，但注释没跟着改
- 注释写的是“干什么”，而不是“为什么这么干”
- 用注释来“美化”一段糟糕的代码

### 自检问题
- 这段注释是在解释“做了什么”，还是“为什么这样做”？
- 如果我把变量名改得更清晰，还需要这段注释吗？
- 如果代码逻辑改了，这段注释还会被同步更新吗？

### 典型案例（41-60）

41. **过时注释**：`// 计算用户年龄` 下面代码是 `return user.Score;`（功能已改为计算积分）
42. **废话注释**：`// 循环遍历列表` 下面 `foreach(var item in list)`
43. **被注释掉的代码**：整段 `//var old = OldMethod();` 留在代码里，没人知道还要不要
44. **欺骗性注释**：`// 从缓存获取` 下面实际是 `_db.Query()`（缓存早删了）
45. **TODO 坟墓**：`// TODO: 优化这段逻辑 2022-01-01`，现在已是 2026 年
46. **写原因的注释被误删**：原本 `// 用 LRU 而非 FIFO 是因为热点数据频繁访问` 被人删了
47. **翻译注释**：`// Get user by id` 上面 `GetUserById(int id)`
48. **版本注释**：`// v2.3 新增`，Git blame 已经能查到
49. **作者签名**：`// Created by Zhang San on 2024-01-01`，Git 有记录
50. **过度详细的步骤**：`// 第一步打开文件，第二步读行，第三步关闭`，函数名 `ReadFileLines()` 已经说清楚了
51. **注释与代码逻辑矛盾**：`// 当用户为空时返回默认值`，但代码里 `if(user != null) return user;`
52. **用注释掩盖坏命名**：`// x 代表订单总金额`，而不是直接把变量叫 `totalOrderAmount`
53. **僵尸代码注释**：`// 暂时禁用，等产品确认`，一等就是两年
54. **与日志重复的注释**：`// 记录错误日志` 下面 `_logger.Error(ex.Message)`
55. **法律条款完整抄写**：把 GB 标准条文完整抄在注释里，导致注释比代码长十倍
56. **注释掉的测试代码**：`// [Fact] public void Test() {}` 整段注释
57. **警告注释不危险**：`// 注意：这里很危险` 但没有说为什么危险、怎样才安全
58. **多语言混杂注释**：`// 获取 user info from cache 从缓存` 
59. **注释比代码长三倍**：3 行的赋值，配了 10 行注释解释每个字段来源
60. **注释掉的性能测试**：`// Stopwatch sw = new(); sw.Start(); ...` 性能测试代码残骸

---

## 四、万能 try-catch 检测

### 核心原则
**“只捕获你能处理的异常”**。不能恢复的异常应该让它炸，炸了才有机会定位根因。如果一定要捕获宽泛异常，必须记录完整堆栈。

### 触发信号
- `catch (Exception)` 出现在非入口层
- catch 块为空或只有一行注释
- 捕获了异常但既没有日志也没有重新抛出
- 所有异常都用同一种方式处理

### 自检问题
- 这个 catch 块里，异常是被记录了，还是被吞掉了？
- 我只捕获了能处理的异常类型吗？
- 不能恢复的异常，我让它炸了吗？

### 典型案例（61-80）

61. **空 catch**：`catch { }`，异常被彻底吞掉，线上有问题永远找不到原因
62. **catch 只写注释**：`catch { // ignore }`，和空 catch 一样危险
63. **捕获后只打 Console.Write**：生产环境看控制台什么用都没有
64. **捕获后返回默认值不记录**：`catch { return null; }`，调用方拿到 null 不知道是正常空还是异常
65. **循环中的万能 catch**：循环处理 1000 条数据，一条异常全部中断，但没有记录是哪条
66. **捕获了数据库异常当网络异常**：`catch (SqlException) { Retry(); }`，死锁和语法错误用同一种重试策略
67. **在构造函数里吞异常**：对象创建失败但返回了半初始化对象
68. **Dispose 里的空 catch**：释放资源失败静默，资源泄漏
69. **事件处理器里的吞异常**：`OnButtonClick` 里吞异常，界面卡死不报错
70. **后台任务里的万能 catch**：定时任务失败静默，一个月后才发现数据没更新
71. **重试逻辑捕获了不可重试的错误**：数据格式错误（400）也重试，白白浪费 3 次调用
72. **捕获异常只为转换类型但丢了 InnerException**：`throw new MyException("错了")` 丢掉了原始堆栈
73. **异步 void 里的异常被吞**：`async void Handle()` 里的异常无法被捕获
74. **Task.WhenAll 的异常没解包**：所有子任务的异常被包装成一个 AggregateException
75. **递归中的异常被吞**：递归深处出错，逐层返回 null，不知道哪一层出的问题
76. **finally 块里又抛异常**：把原始异常覆盖了
77. **catch 块里写文件失败又吞掉**：日志都丢了
78. **捕获 HttpRequestException 但没区分 4xx 和 5xx**：客户端错误和服务端错误处理方式应该不同
79. **捕获 OperationCanceledException 当作错误记录**：用户主动取消不是错误
80. **异常信息直接返回给前端**：堆栈信息泄露，安全风险

---

## 五、变量命名检测

### 核心原则
**“命名是给三个月后的自己看的”**。变量名应该描述“是什么”和“为什么存在”，而不是类型或临时序号。

### 触发信号
- 变量名带数字后缀（data1、data2、temp3）
- 变量名是类型名（string1、int2、list3）
- 单字母变量名（除 i/j/k 且循环体不超过 5 行）
- 缩写只有自己看得懂

### 自检问题
- 变量名里有没有 data/temp/result/info 这些模糊词？
- 有没有带数字后缀的变量？
- 三个月后我自己看这行代码，能秒懂这个变量的含义吗？

### 典型案例（81-100）

81. **数字后缀**：`var data = GetData(); var data2 = Process(data); var data3 = Format(data2);`
82. **类型做变量名**：`string string1 = "hello"; int int1 = 5; List<string> list1 = new();`
83. **模糊词**：`var temp = user.Name; var result = DoSomething(temp);`
84. **单字母灾难**：`var x = GetUser(); var y = x.Order; var z = y.Total;`
85. **拼音命名**：`var shuju = GetData(); var chuli = Process(shuju);`
86. **无意义缩写**：`var usrCtx = new UserContext(); var rsp = await CallApi();`
87. **布尔变量命名像名词**：`bool state = true;` 应该说清楚 `isEnabled`
88. **集合变量用单数**：`var user = GetUsers();` 应该用 `users`
89. **方法名用动词但没宾语**：`Process()` 处理什么？`ProcessOrder()` 好得多
90. **Get 方法有副作用**：`GetUser()` 但内部还创建了新用户
91. **常量用魔法数字**：`if(age > 18)` 应该 `const int AdultAge = 18;`
92. **枚举值无意义排序**：`enum Status { A, B, C }` 而不是 `{ Draft, Published, Archived }`
93. **相同的词在不同上下文用不同含义**：`Process` 在 A 处是“清洗”，在 B 处是“验证”
94. **用否定名做否定判断**：`if(!isNotValid)` 双重否定
95. **事件命名看不出发生时机**：`OnChange` 是变化前还是变化后？
96. **Async 方法漏了 Async 后缀**：`Task<User> GetUser()` 应该 `GetUserAsync()`
97. **接口名没加 I 前缀**：`UserService` 是类还是接口分不清
98. **异常类没加 Exception 后缀**：`throw new UserError()` 不知道是异常
99. **DTO 命名与实体混淆**：`User` 既可以是数据库实体也可以是 API 返回模型
100. **变量名包含技术实现细节**：`var jsonString = "{...}"` 应该是 `var orderData = "{...}"`，未来换成 XML 不用改名

---

## 执行方式

### 日常开发
- 每写完一个函数，用 30 秒过一遍五类自检问题
- 发现任何一项为“是”，立即修改后再提交

### Code Review
- 审查者用此清单作为检查标准
- 发现违反项，直接标记并要求修改

### AI 辅助编程
- 将此 Skill 作为 AI 的约束条件加载
- 生成代码后，要求 AI 逐项自检并修正

### 每周复盘
- 每周五挑一个模块，用此清单做地毯式检查
- 记录本周发现的问题类型分布，找到自己的薄弱项

---

## 新增：Agent1 实战 Bug 驱动的自检清单 v2.0

> ▸ **v2.0 深度分析**

基于 Agent1 项目 30 个实战 Bug 提炼的专项自检项：

### C# null 安全自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| N1 | 所有 `.Substring()` 前是否判空 | Bug 10/11/14/25 | grep `\.Substring` → 确认前置 null 检查 |
| N2 | 所有 `.Length` 前是否判空 | Bug 10 | grep `\.Length` → 确认前置 null 检查 |
| N3 | 所有 `[]` 索引前是否判空 | Bug 25 | 检查 `references[i]` 前是否有 null 判断 |
| N4 | `Dictionary.TryGetValue` 是否检查返回值 | Bug 1/30 | 确认 out 变量被使用前检查了 TryGetValue 返回 |

### 并发安全自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| C1 | 共享集合读写是否加锁 | Bug 21 | 搜索 `_xxx.Add` 和 `_xxx.Where` 的并发调用 |
| C2 | 是否使用 `ConcurrentDictionary` 替代 `Dictionary` | Bug RagCache | 搜索 `Dictionary<` 在可能并发访问的类中 |
| C3 | Timer 是否使用 `PeriodicTimer` + `Task.Run` | Bug Timer | 搜索 `System.Timers.Timer` 或 `Timer(` |

### Key 一致性自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| K1 | 字典 key 和 Metadata key 是否同语义空间 | Bug 1/30 | 对比两处 key 的来源定义 |
| K2 | `_priorityLevels` 的 key 是否与查询用的 key 一致 | Bug 30 | 搜索 `_priorityLevels` 的定义和使用 |

### 多存储一致性自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| M1 | `Clear()` 是否清除了所有存储后端 | Bug 2 | 搜索类中所有 `Clear`/`Delete` → 确认覆盖所有后端 |
| M2 | `Add()` 是否写入了所有存储后端 | Bug 9 | 搜索类中所有 `Add` → 确认覆盖所有后端 |

### 降级副作用自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| D1 | catch 返回的兜底值是否会写入数据库 | Bug 3 | 追踪 catch 返回值的完整使用路径 |
| D2 | 降级结果是否会被二次使用/重复计算 | Bug 6 | 检查降级值是否参与后续融合计算 |

### 量纲一致性自检

| # | 检查项 | 来源 Bug | 检查方法 |
|---|--------|---------|---------|
| Q1 | 加权求和的各项分值范围是否一致 | Bug 8 | 检查参与加法的数值范围，差 100 倍则需归一化 |
| Q2 | 是否使用 RRF 替代简单加权求和 | Bug 8/Sprint 4 | 混合检索场景优先考虑 RRF |

---

**文档结束**

> **版本**：v2.0（深度扩展版）

**最终**：这份清单的目的不是让你的代码变得“完美”，而是帮你建立一种条件反射——写代码时自动避坑，看代码时自动识别。培养这个直觉需要时间，但只要坚持用，三个月后你会发现，自己写的代码自己真能看懂了。