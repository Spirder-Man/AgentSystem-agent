# 化工园区危化品合规审核AI Agent 文档库

## 文档结构说明

```
docs/
├── architecture/              # 架构设计相关文档
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

### 2. technical-principles/ - 技术原理文档
深入解析项目的核心技术原理：
- BM25检索算法详解
- C#底层机制与检索算法
- 向量数据库原理与部署
- 化工RAG系统技术深度拆解

### 3. testing/ - 测试文档
包含测试方案、测试案例、手动测试指南等。

### 4. troubleshooting/ - 故障排查文档
记录项目开发过程中的故障问题与修复方案。

### 5. learning-notes/ - 学习笔记
项目学习过程中的理解记录、设计思考、问题解答等。

### 6. project/ - 项目基本文档
项目基础信息文档，包括数据库配置说明、版本演进记录等。

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

