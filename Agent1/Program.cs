// ============================================================================
// 【语法维度】using 指令是 C# 的编译期命名空间导入机制，不产生运行时 IL 代码。
//   其作用等同于给编译器一张"短名称→完整类型路径"的映射表，避免写全限定名。
//   例如 using Serilog; 之后可以用 Log 代替 Serilog.Log。
//
// 【框架维度】这里引入了两套体系：
//   - Microsoft.Extensions.* — 微软官方的 DI/配置/日志抽象层（.NET 8 标准组件）
//   - Serilog — 第三方结构化日志库，为"配置外化 + 结构化日志"双基石之一
//   - Semantic Kernel (通过 Agent1.Services 间接引入) — 微软 AI 编排 SDK
//
// 【架构维度】using 的顺序遵循 IDE 默认规则：System.* → 第三方 → 项目内部
//   这种分层排列有助于快速识别依赖方向（从底层框架到上层业务）
//
// 【生产维度】所有依赖均为 NuGet 托管的确定性版本，CI/CD 中通过 nuget.config
//   锁定国内镜像源，确保构建可重复、不受外网波动影响
// ============================================================================
using System;
using System.Threading.Tasks;
using Agent1.Services;
using Agent1.Config;
using Agent1.Models;
using Agent1.Commands;
using Agent1.Services.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Agent1.Services.Logging;
using Agent1.Services.Logging.Enrichers;
using Agent1.Services.Logging.Filters;
using Agent1.Services.Monitoring;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ============================================================================
// 【语法维度】namespace 是 C# 的逻辑组织单元，编译后影响类型的完整限定名。
//   此处的 Agent1 命名空间与项目名保持一致，是 .NET 的约定优于配置原则。
//
// 【架构维度】整个控制台应用只有一个 namespace，说明当前尚未到需要
//   命名空间分层隔离的阶段——这对一个独立 Agent 项目是合理的。
//   若未来拆分为多个可独立部署的服务，建议按功能划分子命名空间。
// ============================================================================
namespace Agent1
{
    // ========================================================================
    // 【语法维度】class Program 是 C# 顶级程序入口的约定名称。
    //   .NET 6+ 支持顶级语句（不用显式 class），但此处保留传统写法：
    //   → 优势：可以显式控制 static 方法、字段，便于写辅助方法和内部类
    //   → 代价：多一层缩进和样板代码
    //
    // 【架构维度】Program 类扮演"组合根"（Composition Root）角色：
    //   它是整个应用程序唯一知道所有具体类型的地方，负责：
    //   ① 加载配置  ② 初始化日志  ③ 构建 DI 容器  ④ 启动主循环
    //   这符合 DI 容器模式的最佳实践：依赖图的组装集中在入口点。
    // ========================================================================
    class Program
    {
        // ====================================================================
        // 【语法维度】static async Task Main(string[] args)
        //   - static:    入口方法必须是静态的，CLR 不需要实例化 Program 就能调用
        //   - async Task: C# 7.1 引入的异步入口点，让 Main 内部可以 await
        //                 编译器会生成状态机代码，把 await 后的代码转为回调
        //   - string[] args: 命令行参数数组，CLR 在启动时自动填充
        //
        // 【框架维度】async Main 在编译后被转换为：
        //   → 生成 <Main>d__0 状态机类（实现 IAsyncStateMachine）
        //   → 通过 AsyncTaskMethodBuilder 驱动异步执行
        //   → 异常会被包装进 Task，不会导致进程静默崩溃
        //
        // 【架构维度】入口方法保持轻量是重要原则：
        //   → 只做"组装 + 启动"，不包含业务逻辑
        //   → 真正常驻的业务循环放在下面 while(true) 中
        //   → 启动失败用 return 提前退出（而非 throw），避免堆栈信息干扰
        //
        // 【生产维度】async Main 的异常会被写入 Task 返回值。
        //   在 .NET 8 中，未处理的 Task 异常会触发 TaskScheduler.UnobservedTaskException，
        //   配合 Serilog 可在崩溃前记录完整现场。建议在 Program 最外层加 try-catch。
        // ====================================================================
        static async Task Main(string[] args)
        {
            // ================================================================
            // P0: 强制 UTF-8 控制台编码 — 防止中文输出乱码
            // Windows 中文版默认 GB2312(CP936) 会导致所有 UTF-8 输出
            // 被误解码为"锟斤拷"乱码。此设置影响：
            //   ① Console.WriteLine 输出 → 经 TextWriter.Encoding 编码
            //   ② Serilog Console Sink → 通过 Console.Out 输出
            //   ③ 子进程（dotnet test / TRX Logger）→ 继承控制台代码页
            // 必须在任何 Console 或 Serilog 调用之前执行。
            // ================================================================
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding  = System.Text.Encoding.UTF8;

            // ================================================================
            // Phase 1: 配置外部化 — appsettings.json + 环境变量
            // ================================================================

            // ================================================================
            // 【语法维度】var 是 C# 的隐式类型局部变量声明。
            //   编译器从右侧表达式推断类型为 IConfigurationRoot（不是 dynamic）。
            //   编译后 IL 码与显式声明完全一致，无运行时开销。
            //
            // 【设计模式维度】ConfigurationBuilder 是建造者（Builder）模式：
            //   → new 创建空建造器 → 链式调用添加配置源 → Build() 合并产出
            //   → 隐藏了"多源合并、键冲突覆盖、JSON 扁平化"等复杂构建逻辑
            //   → 每个 .Add*() 返回 this（Fluent API），支持链式调用
            //
            // 【框架维度】ConfigurationBuilder 是 Microsoft.Extensions.Configuration
            //   的核心类。.NET 的配置系统采用"配置源→配置提供者→配置根"三层抽象：
            //   → IConfigurationSource: 描述"从哪读"（文件、环境变量、命令行等）
            //   → IConfigurationProvider: 执行实际的读取和解析
            //   → IConfigurationRoot: 合并后的只读配置视图
            //   每个 .Add* 调用的背后都会创建对应的 Source+Provider 对。
            //
            // 【生产维度】配置外部化是 12-Factor App 的第三条（Config）：
            //   → JSON 文件存非敏感默认值（可入 git）
            //   → 环境变量存密码/密钥（不入 git，由部署平台注入）
            //   → reloadOnChange: true 支持运行时热更新（无需重启进程）
            // ================================================================
            var configuration = new ConfigurationBuilder()
                // 【语法】命名参数语法 C# 4.0+，增强可读性
                // 【框架】AppContext.BaseDirectory = 程序集所在目录（bin/Debug/net8.0/）
                // 【生产】如果不设置 BasePath，默认是 Environment.CurrentDirectory（可能是任意目录）
                .SetBasePath(AppContext.BaseDirectory)
                // 【框架】optional: false = 文件不存在时抛出 FileNotFoundException，启动即失败
                //   这比默默继续然后用默认值更安全（Fail-Fast 原则）
                // 【框架】reloadOnChange: true = 内部使用 FileSystemWatcher 监听文件变更
                //   文件保存后配置自动更新，无需重启（适用于调整日志级别、开关功能等场景）
                // 【生产】热加载有微小性能开销（文件监听线程），生产环境可考虑设为 false
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                // 【框架】AddEnvironmentVariables() 读取操作系统所有环境变量
                //   键名中的双下划线 __ 或冒号 : 会自动转换为配置层级分隔符
                //   例如 DB__PASSWORD=xxx → configuration["DB:Password"]
                // 【安全】环境变量的来源优先级高于 JSON，确保密钥不会意外泄露到源码仓库
                // 【生产】容器化部署时通过 docker-compose.yml 的 environment 段注入
                .AddEnvironmentVariables()
                // 【架构】Build() 是建造者模式的"终结方法"：
                //   遍历所有配置源 → 执行读取 → 按添加顺序合并（后覆盖前）→ 产出 IConfigurationRoot
                //   在 .NET 8 中，Build() 返回的 IConfigurationRoot 同时实现了 IConfiguration
                .Build();

            // 【架构】将 IConfigurationRoot 绑定到类型安全的 AppConfig 单例
            //   Load() 内部使用 configuration.Bind() 将扁平的键值对映射到嵌套对象
            //   例如 "Llm:ModelId":"qwen3" → AppConfig.Instance.Llm.ModelId
            // 【生产】Bind() 依赖属性名和配置键的命名约定匹配（大小写不敏感）
            //   如果配置键名和属性名不一致，需要用 [ConfigurationKeyName] 注解映射
            AppConfig.Load(configuration);

            // P0-2: 激活审计日志文件路径
            ComplianceAuditLogger.Initialize();

            // 【架构】Fail-Fast 验证：在程序进入业务循环之前检查所有必需配置项
            //   如果配置缺失，立即报错退出——避免运行到一半才发现配错导致数据损坏
            //   Validate() 返回错误列表而非直接抛异常，可以一次性展示所有问题
            //   （如果只抛第一个异常，用户修一个才发现下一个，体验很差）
            // 【生产】建议在 CI 部署流水线中增加配置校验步骤，上线前拦截配置错误
            var configErrors = AppConfig.Instance.Validate();
            if (configErrors.Count > 0)
            {
                Console.WriteLine("❌ 配置校验失败，请检查 appsettings.json:");
                foreach (var err in configErrors)
                    Console.WriteLine($"   - {err}");
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey();
                return;  // 注意：这里用 return 而非 Environment.Exit()，让 finally 块正常执行
            }

            // ================================================================
            // Phase 1: 结构化日志 — Serilog + Console 输出双写文件
            // ================================================================
            // 【设计模式维度】又是建造者模式！LoggerConfiguration 和 ConfigurationBuilder
            //   同根同源——都是"声明式配置 → Build 产出"的范式。这体现了 .NET 生态
            //   的 API 设计一致性：学会一种 Builder，就能举一反三。
            //
            // 【框架维度】Log.Logger = new LoggerConfiguration()...CreateLogger()
            //   → Log 是 Serilog 提供的静态类，Log.Logger 是全局静态 Logger 属性
            //   → CreateLogger() 创建的是 ILogger（Serilog.Core.Logger 实例）
            //   → .WriteTo.Console() 内部注册 ConsoleSink（输出适配器）
            //   → .WriteTo.File() 内部注册 FileSink（滚动文件适配器）
            //
            // 【语法维度】WriteTo 是 Serilog 特有的"伪属性"模式：
            //   WriteTo 返回一个配置对象，Console() 和 File() 是其扩展方法
            //   这种 API 设计让 IDE 的智能感知能自动列出所有可用的 Sink
            //
            // 【架构维度】全局静态 Logger 是"环境上下文"（Ambient Context）模式：
            //   优点：任何代码位置都能用 Log.Information() 写日志，无需 DI 注入
            //   缺点：全局可变状态，单元测试时需要 SaveAndRemoveAllSinks() 清理
            //   本项目同时使用了 Serilog 全局 Logger 和 Microsoft.Extensions.Logging
            //   抽象（通过 AddSerilog 桥接），实现了两套日志体系的无缝对接
            //
            // 【生产维度】RollingInterval.Day 按天滚动：
            //   → 文件名格式：agent1-20260616.log
            //   → 每天午夜自动创建新文件，旧文件保留
            //   → 配合 logs/archive/ 目录的定期归档脚本，实现日志生命周期管理
            //   → 生产环境建议改为 RollingInterval.Hour（高流量时避免单文件过大）
            // ================================================================
            // P0-3: Serilog SelfLog — 记录框架自身异常到独立文件
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                try { File.AppendAllText("logs/serilog-self.log", $"{DateTime.UtcNow:O} {msg}{Environment.NewLine}"); }
                catch { /* 静默丢弃——避免 SelfLog 异常导致递归 */ }
            });

            // P0 整改: 配置驱动 + Enricher 流水线 + Filter 层
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)          // 从 appsettings.json Serilog 节读取
                .Enrich.With<EnvironmentEnricher>()              // MachineName / ProcessId / OSVersion
                .Enrich.With<RunIdEnricher>()                    // RunId / StartTime
                .Enrich.With<SessionEnricher>()                  // SessionId（默认 "none"）
                .Enrich.With<ThreadEnricher>()                   // ThreadId
                .Filter.With<KeywordLogFilter>()                 // 拦截敏感关键词日志
                .WriteTo.Console()
                .WriteTo.File("logs/agent1-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)                   // 保留最近 7 天
                .CreateLogger();

            // ================================================================
            // Phase 2d: ConsoleTeeWriter — 双写 TextWriter 解决诊断日志丢失
            // ================================================================
            // 【架构维度】这是装饰器（Decorator）模式的应用：
            //   ConsoleTeeWriter 继承 TextWriter，内部持有两个 TextWriter 对象，
            //   每次 Write/WriteLine 同时写入两个流，对被装饰者（Console.Out）完全透明。
            //
            // 【生产维度】Windows 终端默认缓冲区 9000 行，超出后旧内容不可回溯。
            //   双写到文件后，所有 Console 输出都有持久化副本，运维排查问题时可
            //   直接 grep 日志文件而非翻终端历史。
            //
            // 【语法维度】?? 操作符 = null 合并：ReadLine() 返回 null（EOF）时用 "0" 代替。
            //   这是防御性编程——控制台应用没有标准输入时也能正常退出。
            //
            // 【业务维度】化工合规 Agent 运行一次完整的合规检查可能输出数千行推理过程，
            //   终端缓冲区根本无法容纳，文件日志是唯一可靠的完整记录来源。
            // ================================================================
            // P2 FIX: 诊断日志双写已冗余 — Serilog FileSink (Enricher + retainedFileCountLimit) 替代手写 ConsoleTeeWriter
            // ConsoleTeeWriter 保留代码但注释使用，避免与 Serilog ConsoleSink + FileSink 三重输出
            // var logDir = "logs";
            // if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            // var fullLogPath = Path.Combine(logDir, $"full-{DateTime.Now:yyyyMMdd}.log");
            // var fileWriter = new StreamWriter(fullLogPath, append: true) { AutoFlush = true };
            // Console.SetOut(new ConsoleTeeWriter(Console.Out, fileWriter));
            // Console.WriteLine($"📝 诊断日志双写已启用 → {fullLogPath}");

            // ================================================================
            // 日志桥接：Serilog → Microsoft.Extensions.Logging
            // ================================================================
            // 【框架维度】AddSerilog(dispose: true) 将 Serilog 的全局 Logger
            //   注册为 Microsoft.Extensions.Logging 的 Provider。之后任何通过
            //   ILogger<T> 写的日志都会经过 Serilog 管道，最终到达 Console + File Sink。
            //   dispose: true 表示 DI 容器释放时同步释放 Serilog Logger。
            //
            // 【架构维度】这个桥接让本项目同时拥有两套日志 API 的能力：
            //   ① Serilog 全局静态（Log.Information）→ 适合工具类、静态方法
            //   ② Microsoft ILogger<T>（DI 注入）→ 适合服务类，便于单元测试 mock
            // ================================================================
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog(dispose: true);
            });
            var logger = loggerFactory.CreateLogger<Program>();

            // ================================================================
            // 启动横幅 — 双写：结构化日志 + 终端输出（含版本号便于运维追溯）
            // ================================================================
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v?.?.?";
            logger.LogInformation("══════════════════════════════════════════");
            logger.LogInformation("        化工园区危化品合规审核AI Agent {Version}", versionStr);
            logger.LogInformation("══════════════════════════════════════════");
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine($"        化工园区危化品合规审核AI Agent {versionStr}");
            Console.WriteLine("══════════════════════════════════════════\n");

            // ================================================================
            // Phase 1: 依赖注入容器 — Microsoft.Extensions.DI
            // ================================================================
            // 【框架维度】ServiceCollection 是 .NET 内置的轻量级 DI 容器。
            //   它支持三种生命周期：
            //     Singleton:    全局唯一实例（控制台应用 = 全进程生命周期）
            //     Scoped:       每次 scope.CreateScope() 一个实例（控制台应用无意义）
            //     Transient:    每次请求创建一个新实例
            //   本项目中所有服务都是 Singleton——因为控制台应用只有一个"请求"
            //   （主循环），不存在 Web 请求级别的隔离需求。
            //
            // 【架构维度】DI 容器的核心原则"依赖倒置"（DIP）在这里完整体现：
            //   → 所有上层代码依赖接口（ILlmService），而非具体实现（LlmService）
            //   → 容器根据注册映射自动注入具体实现
            //   → 替换实现只需改注册（如 KnowledgeBaseService → HybridKnowledgeBaseService）
            //
            // 【生产维度】DI 容器使得每个服务可以被独立单元测试：
            //   测试中注入 Mock<ILlmService> 即可隔离外部 AI 服务依赖。
            // ================================================================
            //
            // 【学习笔记·容器本质】ServiceCollection 本质上是一本 Dictionary<类型, 配方>。
            //   key = 类型（如 ILlmService、AppConfig），value = "怎么造出这个对象"的配方。
            //   容器的唯一工作：有人通过 sp.GetRequiredService<T>() 或构造器注入要 T
            //   → 翻字典找 key=T → 找到就按配方 new 出来给 → 找不到就报错。
            //
            // 【学习笔记·三要素框架】理解任何一行 AddSingleton，都可以拆成三维：
            //   ① 登记的：字典里多了什么 key→value
            //   ② 容器构造的：容器怎么 new 出对象（自动 new / 执行 lambda）
            //   ③ 方向盘参数：有没有容器翻字典找不到、必须你亲手填的缺口
            //
            // 【学习笔记·自动注册 vs 工厂注册·唯一判据】
            //   看这个类的构造器——所有参数的类型，都在容器字典里存在吗？
            //   全有 → 自动注册（AddSingleton<I, C>()，容器自己翻字典拿参数、自己 new）
            //   缺一个 → 工厂注册（sp => { ... }，你亲手 new）
            //   构造完还需要调方法装配 → 工厂注册（容器只会调构造器，不会"new 完再调方法"）
            // ================================================================
            var services = new ServiceCollection();

            // 【架构】注册配置单例——整个程序共享同一个 AppConfig 实例
            //   AddSingleton(obj) 注册已创建实例（而非每次 new）
            //
            // 【学习笔记·字典第一条记录】这一行让 key=AppConfig 进入容器字典。
            //   从此所有构造器参数类型为 AppConfig 的服务（DatabaseService 等）都能自动解析。
            //   但 AppConfig 内部的子属性（Llm、Database、ChemicalTool 等）对容器不可见——
            //   字典的 key 是"类型"，不是"属性名"。List<ToolDefinition> 没有登记，
            //   所以 ToolService 的第三个参数容器查不到 → 只能工厂注册。
            services.AddSingleton(AppConfig.Instance);

            // 【框架】注册日志基础设施
            //   ILogger<T> 是 Microsoft.Extensions.Logging 的泛型日志接口
            //   typeof(Logger<>) 是开放泛型（Open Generic），DI 容器会自动为每个 T 创建 Logger<T>
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // 【架构】核心服务注册——数据库、会话、记忆三大基础设施
            //   都是接口→实现的映射，方便未来替换底层实现
            //
            // 【学习笔记·自动注册范例】这三行是"全自动注册"的标准模板：
            //   ① SessionService()：零参数 → 零个缺零个 → 容器闭眼 new ✅
            //   ② DatabaseService(AppConfig)：参数类型 AppConfig → L321 已登记 → 容器自己翻字典拿到 ✅
            //   ③ MemoryService(ILogger, AppConfig)：两个参数都在字典里 ✅
            //   判据验证：所有构造参数的类型都作为 key 在字典里存在 → 全自动！
            //
            // 【学习笔记·规则四·接口登记】这三个都以接口为 key（IDatabaseService 而非 DatabaseService）。
            //   判据：它们会被其他服务当依赖注入 → 用接口登记。
            //   对比：AgentDialog、ModuleDispatcher 是编排终点，不被别人依赖 → 裸类登记。
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<IMemoryService, MemoryService>();

            // ================================================================
            // [P0 Lazy<T>] 循环依赖破解 — Lazy<T> 延迟解析
            // ================================================================
            // 【架构维度】LlmService ↔ HybridKnowledgeBaseService 互相依赖：
            //   → LlmService 需要 IKnowledgeBaseService 用于 ChemicalComplianceTools 的 RAG 检索
            //   → HybridKnowledgeBaseService 需要 ILlmService 用于生成文本向量嵌入
            //
            // 【Lazy<T> 方案】将 IKnowledgeBaseService 包装为 Lazy<T>：
            //   → DI 注册时只创建 Lazy 包装器（不触发 Value 解析）
            //   → LlmService / ChemicalComplianceTools 仅在首次 RAG 检索时访问 .Value
            //   → 此时 ILlmService 已经构建完成，循环依赖自然解开
            //
            // 【对比 null! 方案】
            //   旧: new LlmService(null!) + 后续 SetKnowledgeBaseService — 运行时风险（忘记调用=NullRef）
            //   新: new LlmService(new Lazy<...>(() => sp.GetRequiredService<...>())) — 编译期安全
            // ================================================================
            services.AddSingleton<LlmService>(sp => new LlmService(
                new Lazy<IKnowledgeBaseService>(() => sp.GetRequiredService<IKnowledgeBaseService>())));
                //这行是关键：它只是把"到时候去容器拿"的配方装进盒子里，现在不执行（不触发解析）。sp = 容器自己——闭包把它抓住，等以后用。
                //
                // 【学习笔记·工厂注册案例1】LlmService 必须工厂注册——不是参数不在字典里，
                //   而是"Lazy<T> 延迟解析"这种特殊装配步骤，容器不会。
                //   这里 sp => new(...) 中的 sp 就是容器本人——你从它肚子里拿已登记的东西，
                //   再结合自己的装配逻辑（Lazy 盒子），造出最终对象。
            services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<LlmService>());
                // 【学习笔记·规则三·一个类一个身份】LlmService 以 LlmService 和 ILlmService 两个 key 登记？
                //   不是——这里不是两个独立单例。ILlmService 的配方只是转发到 LlmService 单例
                //   （sp.GetRequiredService<LlmService>() 返回的是上面那行的同一个实例），
                //   本质是"别名转发"，不是"两次构造"。规则三说的是不能两个 key 各自 new 一次。

            // 【架构】ToolService 是"门面"（Facade）——聚合 LLM + 知识库的能力暴露给外部
            //
            // 【学习笔记·工厂注册案例2】ToolService 必须工厂注册——判据验证：
            //   构造器参数: (ILlmService, IKnowledgeBaseService, List<ToolDefinition>)
            //   字典里:     ✅ ILlmService        ✅ IKnowledgeBaseService     ❌ List<ToolDefinition>
            //   缺一个 → 必须工厂注册！
            //   原因：容器字典里没有 key=List<ToolDefinition>——如果登记了它，
            //   全系统只能有一个工具列表，另一个服务（如命令注册表）也是 List<ToolDefinition> 会拿错。
            //   泛型基础类型没有"唯一身份"，这是字典撰写规则一的体现。
            services.AddSingleton<IToolService>(sp =>
            {
                var llm = sp.GetRequiredService<ILlmService>(); //拿大模型
                var kb = sp.GetRequiredService<IKnowledgeBaseService>(); //拿知识库
                return new ToolService(llm, kb, AppConfig.Instance.ChemicalTool?.Tools);
                //现在看这个类型名：List<ToolDefinition>——它太泛了。
                // ILlmService 自带标签"我是大模型服务"，IDatabaseService 
                // 自带标签"我是数据库门面"。但 List<ToolDefinition> 
                // 说出来只是"一个工具定义列表"——万一将来某个服务需要另一个工具列表
                // （比如命令注册表）也是 List<ToolDefinition> 类型呢？容器分不清，会给同一个。
            });
            services.AddSingleton<AgentDialog>();
                // 【学习笔记·规则四·裸类登记】AgentDialog 是编辑核心/被测对象，
                //   不被其他服务当依赖注入 → 裸类登记（不带接口）。
                //   它构造器的 7 个参数（ILlmService, IToolService 等）全在字典里 → 自动注册。

            // 【架构】这里注册 IKnowledgeBaseService → HybridKnowledgeBaseService
            //   而非注册为具体 KnowledgeBaseService（纯内存 BM25 版）
            //   HybridKnowledgeBaseService 是"门面 + 协调者"：
            //   内部组合了 KnowledgeBaseService（BM25 关键词检索）
            //   和 GpuVectorIndexService（向量语义检索），通过 RRF 算法融合结果
            //
            // 【学习笔记·工厂注册案例3】HybridKnowledgeBaseService 的构造器参数
            //   (IDatabaseService, ILlmService, AppConfig) 全在字典里——技术上可以自动注册。
            //   但选择了手动——因为构造器内部 new 了 KnowledgeBaseService 和 QueryCacheService，
            //   这两个子对象不走容器，由你亲手控制构造过程。
            //   → "能自动但选了手动"：方向盘交给容器也能开，但你知道更精确的路线。
            services.AddSingleton<IKnowledgeBaseService>(sp =>
            {
                var db = sp.GetRequiredService<IDatabaseService>();
                var llm = sp.GetRequiredService<ILlmService>();
                return new HybridKnowledgeBaseService(db, llm, AppConfig.Instance);
            });
            //精修为："语法没有限制（都能写），但选择自动还是手动，取决于你愿不愿意把方向盘交给容器。"
            // 自动传 = "容器，你比我清楚，全交给你"
            // 手动传 = "谢谢，我知道你都能拿到，但这辆车我自己开"
            // ToolService 是你被迫手动（有一个参数不支持自动），HybridKnowledgeBaseService 是你选择手动（能自动但我更清楚怎么造）。
            services.AddSingleton<IIntegrationService, IntegrationService>();
            //化工园区系统桥接器
            //对接 ERP/WMS/EHS 外部系统——查仓储台账（危化品）、查工单、同步数据
            //   【学习笔记·自动注册】构造参数 IDatabaseService + AppConfig 全在字典里 → 自动注册。
            //   【学习笔记·规则四·接口登记】它是 IIntegrationService——会被其他服务注入 → 用接口。
            services.AddSingleton<IAuditService, AuditService>();
            //审计门面
            //调用 DatabaseService.AddAuditLogAsync 记操作日志（之前数据库那 25 个方法里有它），含 SHA256 哈希链防篡改
            //   【学习笔记·自动注册】同 IntegrationService——参数全在字典里。
            services.AddSingleton<IModuleFactory, ModuleFactory>();
            //模块工厂
            // 根据命令类型（ModuleType 枚举）造对应的执行模块——工厂模式，11 行接口只两个方法
            services.AddSingleton<ModuleDispatcher>();
            //模块调度器
            // 持有一个工厂 + 已创建模块的字典缓存，ExecuteModuleAsync(type) 派发执行——命令模式的调度中枢
            //   【学习笔记·规则四·裸类登记】ModuleDispatcher 是调度中枢，不被其他服务注入 → 裸类。
            services.AddSingleton<ResponseCacheService>();
            //响应缓存
            //ConcurrentDictionary 内存缓存，用查询文本哈希当 key，默认 60 分钟过期，每 5 分钟清一次——重复问题秒回，不烧 GPU
            //   【学习笔记·规则四·裸类登记】内部缓存——不暴露给外部依赖 → 裸类。

            // [Phase 1 编排层] 巡检编排器 — 将原子能力编排为业务工作流
            //
            // 【学习笔记·三层架构·第三层起点】从这里开始进入"编排与规则引擎层"。
            //   第一层（L329-333）：基础设施——DatabaseService, SessionService, MemoryService
            //   第二层（L361-435）：核心业务能力——LlmService, ToolService, AuditService 等
            //   第三层（本段开始）：编排与规则——把第二层的原子能力串成工作流
            //   第三层全部裸类登记——它们是编排终点/被测对象，不被别人注入。
            services.AddSingleton<InspectionOrchestrator>();//巡检编排器：把"查库存→查工单→查合规"串成完整巡检报告

            // [铁律核心] 确定性规则引擎 — 传统化工安全系统核心范式
            services.AddSingleton<DeterministicRuleEngine>();//确定性规则引擎：硬编码化工安全规则（如"苯存储温度≤40°C"），不靠 AI，纯 if/else，100% 可审计

            // [Phase 1 编排层] 合规规则引擎 — 对标 Dependency-Track 的漏洞匹配引擎
            services.AddSingleton<ComplianceRuleEngine>();//合规规则引擎：对标行业漏洞匹配框架，把法规要求映射到你的化工数据上做合规判断

            // [P0 持久化] 巡检数据仓储
            services.AddSingleton<InspectionRepository>();//巡检数据仓储：巡检结果的持久化存取（你可能发现它肚子里有个 DatabaseService）

            // [P1 定时扫描] 自动合规监控
            services.AddSingleton<ScheduledScanService>();//定时扫描：自动定期跑合规检查，不需要人点按钮——类似 SessionCleanupHostedService 那种后台工作

            // [Phase 1 编排层] 能力注册表 — 范式 4 动态路由
            services.AddSingleton<CapabilityRegistry>();//能力注册表：用"动态路由"把命令分发到对应模块——配合 ModuleDispatcher 做命令→动作的映射

            // [Phase 1 编排层] 事件动作订阅器 — 范式 3 事件驱动
            // [P2] 事件订阅生效 — FindingCreated/ScheduledScanCompleted → 审计日志
            //
            // 【学习笔记·工厂注册变体·"装配不止构造器"】EventActionDispatcher 的构造器无参数，
            //   按理能自动注册。但 new 完之后还要调 Subscribe 绑定事件处理——
            //   容器只会调构造器，不会"构造完再调方法"。所以必须先 new，再 Subscribe，再登记。
            //   这种"构造 + 方法调用"的两步装配，是工厂注册的第二种触发条件。
            var eventDispatcher = new EventActionDispatcher();
            services.AddSingleton(eventDispatcher);
            eventDispatcher.Subscribe("FindingCreated", async evt =>
            {
                Serilog.Log.Information("[EventAction] 新合规发现: {Desc}", evt.Description);
                await Task.CompletedTask;
            });
            eventDispatcher.Subscribe("ScheduledScanCompleted", async evt =>
            {
                Serilog.Log.Information("[EventAction] 定时扫描完成: {Desc}", evt.Description);
                await Task.CompletedTask;
            });

            // [P1] 告警系统 — 控制台测试通道
            // AlertDispatcher（分发器）           ← 程序入口：只有一个 SendAlertAsync 方法
            // ├─ ConsoleAlertService（控制台）  ← 最低保障通道：红色高亮输出，始终启用
            // └─ EmailAlertService（邮件）     ← 可选通道：走 MailKit + SMTP，需配置
            // 调用方式（任何服务里都可以调）：
            //await alertDispatcher.SendAlertAsync("LLM熔断", "连续3次失败", AlertLevel.Critical);
            //                                   ───标题──  ───内容──      ──级别──
            // 效果：控制台打红色字 + 发邮件给安全员（如果邮件通道启用了）
            //
            // 【学习笔记·工厂注册案例4·"装配不止构造器"】AlertDispatcher 构造器零参数，
            //   但需要两步装配：① new 空壳 ② Register 通道。构造器只做①，②是额外步骤——
            //   容器只会调构造器，不会"构造完再调方法"，所以必须工厂注册。
            //   判据验证：构造完还需要调方法装配 → 工厂注册！
            //   EmailAlertService 需要 7 个配置参数（SmtpHost/密码/收件人等），
            //   这些全是 string/int，字典里都没有——如果换成自动注册，容器一个都找不到。
            services.AddSingleton<AlertDispatcher>(sp =>//// 工厂注册，不是业务逻辑
            {
                var d = new AlertDispatcher();
                d.Register(new ConsoleAlertService());// 通道1：控制台（无条件启用）
                var emailCfg = AppConfig.Instance.Alerting.Email;
                if (emailCfg.Enabled && !string.IsNullOrWhiteSpace(emailCfg.SmtpHost) && emailCfg.RecipientEmails.Count > 0)
                {
                    //// 参数全是配置，容器不认识
                    d.Register(new EmailAlertService(
                        emailCfg.SmtpHost, emailCfg.SmtpPort,
                        emailCfg.SenderEmail, emailCfg.SenderPassword,
                        emailCfg.RecipientEmails, enabled: emailCfg.Enabled));
                }
                return d;
            });

            // 【架构】Phase 2: 长期记忆——短期会话记忆 + 长期持久化记忆双层体系
            //   【学习笔记·工厂注册案例5】同 HybridKnowledgeBaseService——参数全在字典里（能自动），
            //   但选了手动（构造器内部装配可控）。
            services.AddSingleton<ILongTermMemoryService>(sp =>
            {
                var db = sp.GetRequiredService<IDatabaseService>();
                var llm = sp.GetRequiredService<ILlmService>();
                return new LongTermMemoryService(db, llm);
            });

            // 【架构】Phase 4.1: MemoryCoordinator 是"协调者"模式——
            //   统一管理短期记忆（MemoryService）、长期记忆（LongTermMemoryService）、
            //   响应缓存（ResponseCacheService）和审计日志（AuditService）
            services.AddSingleton<MemoryCoordinator>(sp =>
            {
                var shortMem = sp.GetRequiredService<IMemoryService>();
                var longMem = sp.GetRequiredService<ILongTermMemoryService>();
                var cache = sp.GetRequiredService<ResponseCacheService>();
                var audit = sp.GetRequiredService<IAuditService>();
                return new MemoryCoordinator(shortMem, longMem, cache, audit);
            });

            // [知识图谱] 化工知识图谱服务 — 替代硬编码 ChemicalSubstanceDatabase
            services.AddSingleton<IChemicalKnowledgeGraph, ChemicalKnowledgeGraph>();
            services.AddSingleton<ChemicalNamingInference>();

            // ================================================================
            // 【学习笔记·容器字典撰写五条规则·全注册表收束】
            //   以下五条规则从上方全部 AddSingleton 反推得出，每一条都对应"不遵守会怎样"：
            //
            //   规则一·强语义身份：进字典的类型必须能唯一标识系统中那个东西。
            //     ✅ ILlmService（唯一的大模型服务）  ❌ List<ToolDefinition>（哪个列表？）
            //     自检：类型名说出去，同事能立刻知道是系统中哪个唯一东西吗？
            //
            //   规则二·配置走 AppConfig：只登记 AppConfig 整体，子属性不拆开登记。
            //     ✅ AppConfig.Instance                          ❌ AppConfig.Instance.Database
            //     谁需要配置就注入 AppConfig，自己在构造器里取 config.Database。
            //
            //   规则三·一个类一个身份：绝不以多个 key 登记同一个实现类（会造出两个独立单例）。
            //     ✅ IDatabaseService → DatabaseService（仅此一个）
            //     例外：LlmService/ILlmService 是"别名转发"（ILlmService 配方返回同一实例），非两次 new。
            //
            //   规则四·接口 vs 裸类：被其他服务依赖的用接口，编排终点/被测对象的用裸类。
            //     接口登记：IDatabaseService, ILlmService, IAuditService, IToolService...
            //     裸类登记：AgentDialog, InspectionOrchestrator, DeterministicRuleEngine...
            //
            //   规则五·自动 vs 工厂：构造参数全在字典里 → 自动；缺一个/需要方法装配 → 工厂。
            //     自动注册约占 70%（DatabaseService, SessionService, AuditService...）
            //     工厂注册约占 30%（LlmService, ToolService, HybridKnowledgeBaseService, AlertDispatcher...）
            //
            //   五条规则互为约束，每一行 AddSingleton 都是五条规则同时审过的结果。
            // ================================================================

            // 【框架】BuildServiceProvider() 是 DI 容器的"编译"步骤：
            //   验证所有依赖关系——如果有无法解析的依赖，这里会抛异常。
            //   这提供了编译期（启动时）的安全网，而不是运行到一半才崩溃。
            var serviceProvider = services.BuildServiceProvider();

            // [知识图谱] 初始化门面类 — 将图服务注入到 ChemicalSubstanceDatabase 静态门面
            ChemicalSubstanceDatabase.SetGraph(serviceProvider.GetRequiredService<IChemicalKnowledgeGraph>());

            // ================================================================
            // Phase 1: 从 DI 容器解析服务
            // ================================================================
            // 【框架】GetRequiredService<T>() vs GetService<T>()：
            //   Required 版：找不到服务时抛 InvalidOperationException（推荐，Fail-Fast）
            //   普通版：找不到返回 null（需要判空，容易遗漏）
            //
            // 【架构】这里的服务解析顺序遵循"基础设施优先"原则：
            //   数据库 → 会话 → 记忆 → LLM → 工具 → 对话框 → 知识库
            //   这个顺序也反映了数据流的依赖方向。
            var databaseService = serviceProvider.GetRequiredService<IDatabaseService>();
            var sessionService = serviceProvider.GetRequiredService<ISessionService>();
            var memoryService = serviceProvider.GetRequiredService<IMemoryService>();
            var llmService = serviceProvider.GetRequiredService<ILlmService>();
            var toolService = serviceProvider.GetRequiredService<IToolService>();
            var agentDialog = serviceProvider.GetRequiredService<AgentDialog>();
            var knowledgeBaseService = serviceProvider.GetRequiredService<IKnowledgeBaseService>();

            // [P0 Lazy<T>] SetKnowledgeBaseService 已废弃 — Lazy<T> 自动完成延迟注入

            var integrationService = serviceProvider.GetRequiredService<IIntegrationService>();
            var auditService = serviceProvider.GetRequiredService<IAuditService>();
            var moduleFactory = serviceProvider.GetRequiredService<IModuleFactory>();
            var dispatcher = serviceProvider.GetRequiredService<ModuleDispatcher>();

            // ================================================================
            // 数据库连接初始化 + ChemicalRAG 知识库预加载
            // ================================================================
            // 【业务维度】化工合规 Agent 的"大脑"包含两个部分：
            //   ① PostgreSQL 数据库（持久化存储：化学物质数据、合规记录、会话记忆）
            //   ② ChemicalRAG（检索增强生成：GB 国标、园区规则、历史案例的混合检索）
            //   两者缺一不可——数据库存结构化数据，RAG 存非结构化文档。
            //
            // 【生产维度】TestConnectionAsync 和 InitializeDatabaseAsync 分离：
            //   先测连接 → 再初始化表 → 如果连接失败，跳过初始化避免异常连锁
            //   这种分步方式让运维能快速定位问题是网络还是表结构。
            // ================================================================
            logger.LogInformation("📦 正在测试数据库连接...");
            Console.WriteLine("📦 正在测试数据库连接...");
            if (await databaseService.TestConnectionAsync())
            {
                logger.LogInformation("✅ 数据库连接成功");
                Console.WriteLine("✅ 数据库连接成功！");
                await databaseService.InitializeDatabaseAsync();
                Console.WriteLine("✅ 数据库表初始化完成！");
            }
            else
            {
                logger.LogWarning("⚠️ 数据库连接失败，请检查配置");
                Console.WriteLine("⚠️ 数据库连接失败，请检查配置");
            }

            // 【架构】ChemicalRAG 是一个"聚合根"：
            //   组合了知识库服务（HybridKnowledgeBaseService）和数据库服务，
            //   对外提供统一的 SearchAsync 接口，隐藏内部的 BM25+Vector 混合检索细节。
            //   [OCR] 按配置可选挂载视觉 OCR 回退（扫描件 PDF 逐页转写，需 8083 视觉实例）。
            var kbConfig = AppConfig.Instance.KnowledgeBase;
            var pdfOcrService = kbConfig.EnableVisionOcr
                ? new PdfOcrService(kbConfig.OcrMaxPagesPerPdf, kbConfig.OcrRenderDpi)
                : null;
            var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService, databaseService, pdfOcrService);

            // 【业务】预加载化工知识库：扫描 knowledgebase/ 目录下所有 .txt 文件，
            //   分块 → 生成向量嵌入 → 存入内存索引 + PostgreSQL pgvector。
            //   这是程序启动中最耗时的步骤（取决于文档数量和 GPU 推理速度），
            //   但必须完成才能进入业务循环，因为后续的合规检索都依赖此索引。
            await chemicalRAG.LoadKnowledgeBaseAsync();

// ============================================================================
// 【以下为业务流程图注释——保留原始分析内容】
// ============================================================================
#region 完整的调用链路
// 程序启动
//   │
//   ▼
// ChemicalRAG.LoadKnowledgeBaseAsync()
//   │  [ChemicalRAG.cs 第38-93行]
//   │
//   ├── 扫描 knowledgebase/国标/*.txt         ← 只读 .txt 文件！
//   │   ├── GB15603-1995 常用化学危险品贮存通则.txt
//   │   ├── GB30000-2013 化学品分类和标签规范.txt
//   │   └── 危险化学品安全管理条例.txt
//   │
//   ├── 扫描 knowledgebase/园区规则/*.txt
//   │   ├── 园区动火作业安全规范.txt
//   │   └── 园区危化品存储管理规定.txt
//   │
//   ├── 扫描 knowledgebase/历史案例/*.txt
//   │   ├── 2022年安全检查整改案例.txt
//   │   └── 2023年储罐泄漏处置案例.txt
//   │
//   ▼
// LoadAndSplitFile() 对每个 .txt 文件
//   │  [ChemicalRAG.cs 第101-131行]
//   │
//   ├── File.ReadAllTextAsync(filePath)      ← 读入全文
//   ├── SplitTextIntoChunks(content, 500)    ← 按 500 字符分块
//   │
//   ▼
// _knowledgeBase.AddDocumentAsync(chunk, metadata)
//   │  [HybridKnowledgeBaseService.cs 第27-57行]
//   │
//   ├── [内存路径] _bm25Service.AddDocumentAsync(chunk)
//   │      → 存入 KnowledgeBaseService._documents 列表
//   │      → 更新 _termDocFreq 倒排索引
//   │      → 纯内存，进程重启就消失
//   │
//   └── [数据库路径] _databaseService.AddChemicalDocumentAsync(...)
//          → INSERT INTO chemical_documents (content, embedding, ...)
//          → 存入 PostgreSQL，持久化存储
//          → 会调用 _llmService.GetEmbeddingAsync(content) 生成 768 维向量


#endregion

// 所以数据是在程序每次启动时，从 knowledgebase/ 下的 .txt 文件读入，同时写入内存和数据库。 
// 因此 KnowledgeBaseService._documents 列表是空的只是因为还没运行过程序，
//或者运行过但用的 KnowledgeBaseService（纯内存版）而非 HybridKnowledgeBaseService。

            // ================================================================
            // [P1 菜单收敛] 22项 → 5项业务入口
            // 原有原子菜单保留在"系统运维→经典菜单"子选项中
            // ================================================================
            var commands = new Dictionary<string, IMenuCommand>
            {
                ["0"] = new ExitCommand(),

                // ── 业务入口 ──
                ["1"] = new ModuleCommand("1", "💬 对话工作台 [CoT·ReAct·Reflection·RAG]", ModuleType.UnifiedDialog, dispatcher),
                ["2"] = new InspectionWorkbenchCommand(
                    serviceProvider.GetRequiredService<InspectionOrchestrator>(),
                    serviceProvider.GetRequiredService<CapabilityRegistry>(), dispatcher),
                ["3"] = new ModuleCommand("3", "🚨 应急响应台 [泄漏·火灾·爆炸·中毒]", ModuleType.EmergencyResponse, dispatcher),
                ["4"] = new DashboardCommand(
                    serviceProvider.GetRequiredService<InspectionOrchestrator>(),
                    serviceProvider.GetRequiredService<ComplianceRuleEngine>(),
                    serviceProvider.GetRequiredService<InspectionRepository>(), dispatcher),
                ["5"] = new AdminMenuCommand(moduleFactory, databaseService, chemicalRAG,
                    agentDialog, llmService, knowledgeBaseService,
                    serviceProvider.GetRequiredService<AlertDispatcher>(), dispatcher),
            };

            while (true)
            {
                Console.WriteLine("\n请选择功能:");
                foreach (var cmd in commands.Values.OrderBy(c => int.TryParse(c.Key, out var k) ? k : 99))
                    Console.WriteLine($"  {cmd.Key,2}. {cmd.Label}");
                Console.WriteLine("  0. 退出\n");

                Console.Write("请输入选项: ");
                Console.ForegroundColor = ConsoleColor.Green;
                var input = Console.ReadLine() ?? "0";
                Console.ResetColor();

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    input = "0";

                if (commands.TryGetValue(input, out var command))
                {
                    try
                    {
                        await command.ExecuteAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n❌ 执行出错: {ex.Message}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("\n⚠️ 无效选项，请重新选择");
                }
            }
        }
    }

    // ========================================================================
    // ConsoleTeeWriter — 双写 TextWriter（装饰器模式）
    // ========================================================================
    // 【设计模式维度】这是标准的装饰器（Decorator）模式：
    //   ① 继承 TextWriter 抽象类（与被装饰者同类型）
    //   ② 内部持有 _original（原始 Console 输出流）和 _file（文件输出流）
    //   ③ 重写 Write/WriteLine，每次调用同时写入两个流
    //   ④ 对使用者完全透明：Console.SetOut() 替换后，所有 Console.Write 自动双写
    //
    // 【框架维度】TextWriter 是 .NET 的文本输出抽象基类，
    //   Console.Out 默认指向 stdout，Console.SetOut() 可以替换为任何 TextWriter。
    //   这是 .NET 流式 I/O 设计的强大之处——所有文本输出都经过同一个抽象。
    //
    // 【生产维度】生产环境的关键价值：
    //   ① 终端缓冲区溢出（Windows 默认 9000 行）后旧内容不可回溯
    //   ② 文件日志是唯一可靠的完整输出记录
    //   ③ 排查问题时可直接 grep 日志文件，无需翻终端
    //   ④ AutoFlush = true 确保每条消息立即落盘（崩溃不丢数据）
    //
    // 【中高级建议】当前实现每次 Write 都同步刷盘（AutoFlush=true），
    //   高频输出场景（如流式 LLM 响应）可能造成 I/O 瓶颈。
    //   建议：
    //   ① 改为定时批量刷盘（如每 500ms 一次）
    //   ② 或使用 Serilog 的 File Sink 替代手写文件（已有 Serilog，此处冗余）
    // ========================================================================
    /// <summary>
    /// 双写 TextWriter：同时写入原始 Console 输出流和文件流
    /// 解决终端缓冲区溢出后无法回溯诊断日志的问题
    /// </summary>
    public class ConsoleTeeWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly TextWriter _file;

        public ConsoleTeeWriter(TextWriter original, TextWriter file)
        {
            _original = original;
            _file = file;
        }

        // 【语法】=> 表达式体属性：Encoding 直接委托给原始输出流
        public override Encoding Encoding => _original.Encoding;

        // 重写三个核心 Write 方法——所有 Console.Write* 最终都会经过这三个方法之一
        public override void Write(char value)
        {
            _original.Write(value);
            _file.Write(value);
        }

        public override void Write(string? value)
        {
            _original.Write(value);
            _file.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            _file.WriteLine(value);
        }

        // 【架构】Dispose 模式：只释放文件流，不释放原始输出流
        //   Console.Out 由 CLR 管理，不应手动释放——否则后续 Console.Write 会报错。
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();  // 只释放文件流
            }
            base.Dispose(disposing);  // 调用父类 Dispose（基类无特殊资源）
        }
    }
}