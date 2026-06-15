# 化工园区危化品合规审核AI Agent 文档库

## 文档结构说明

```
docs/
├── architecture/              # 架构设计相关文档
├── articles/                  # 技术文章与参数注入方案
├── deploy/                    # 部署评测文档
│   └── 3090服务器评测.md      # RTX 3090 RAG GPU加速性能评测
├── technical-principles/      # 技术原理深度解析
├── testing/                   # 测试相关文档
├── troubleshooting/           # 故障排查与修复
├── learning-notes/            # 学习笔记与理解
└── project/                   # 项目基本文档
```

## 文档分类详情

### 1. architecture/ - 架构设计文档
包含项目的架构设计、整改方案、适配方案、优化方案等文档，适合阅读顺序：
1. 先看「架构设计文档.md」了解整体架构
2. 再看「化工园区危化品合规审核AI Agent架构适配方案.md」了解行业适配
3. 最后看「架构验证报告.md」了解架构验证结果
4. **「ModelScope模型选型决策框架.md」** — 本地模型选型评估方法（Qwen3/DeepSeek-R1 对比决策过程）

### 2. articles/ - 技术文章
实战技术文章与参数注入方案探讨：
- **「Semantic_Kernel_Ollama_enable_thinking_参数注入方案探讨.md」** — 如何通过 DelegatingHandler 向 Ollama 注入 think 参数，控制 Qwen3 思考模式
- 附参考图片：ollama-api-chat-think-schema.png、ollama-api-chat-think-example.png

### 3. deploy/ - 部署评测文档
GPU 部署环境评测与性能基准：
- **「3090服务器评测.md」** — RTX 3090 24GB 环境下 RAG GPU 全链路加速性能评估报告

### 4. technical-principles/ - 技术原理文档
深入解析项目的核心技术原理：
- BM25检索算法详解
- C#底层机制与检索算法
- 向量数据库原理与部署
- 化工RAG系统技术深度拆解

### 5. testing/ - 测试文档
包含测试方案、测试案例、手动测试指南等。

### 6. troubleshooting/ - 故障排查文档
记录项目开发过程中的故障问题与修复方案：
- **P0-P1修复详细技术文档.md** — 工业工具→化工合规工具替换全流程（P0 + P1 + 3项Bug修复），含完整代码对比与技术原理解析

### 7. learning-notes/ - 学习笔记
项目学习过程中的理解记录、设计思考、问题解答等：
- **「K1-K9的具体问题.md」** — K1–K9 知识点详细问答记录

### 8. project/ - 项目基本文档
项目基础信息文档，包括数据库配置说明、版本演进记录等。

### 9. 根目录文档
- **「Agent1 十项核心技术决策深度拆解.md」** — 项目十项核心技术决策的完整复盘与深度拆解
- **「FunctionCalling模型评测BUG记录.md」** — Function Calling 模型评测过程中的 BUG 记录与修复过程
- **「别小看这两个for循环！中文RAG检索的底层核心解法.md」** — 中文 RAG 检索中 BM25 核心算法解析
- **「🔴 断点地图：RAG 全链路深度理解.md」** — RAG 全链路断点调试地图（含详细截图）

---

## 建议阅读路径

**初学者路径**：
1. 先看 learning-notes/ 了解学习过程
2. 再看 architecture/ 理解整体架构
3. 然后看 technical-principles/ 深入技术原理

**架构师路径**：
1. 先看 architecture/ 掌握架构设计
2. 再看 technical-principles/ 深入技术细节
3. 最后看 testing/ 和 troubleshooting/ 了解验证与改进

## 与软考结合

这个文档库完整覆盖了软考「系统架构设计师」的核心考点：
- 软件架构设计（分层架构、策略模式等）
- 信息检索系统（BM25、向量检索）
- 知识管理与知识图谱
- 系统安全与等保三级

