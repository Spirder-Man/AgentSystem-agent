# 化工园区危化品合规审核AI Agent架构整改方案

## 一、核心目标

**完全聚焦于化工园区危化品合规审核场景**，遵循架构适配方案进行整改。

## 二、问题分析

### 2.1 当前硬编码问题统计

| 文件 | 问题类型 | 严重程度 | 说明 |
|------|---------|---------|------|
| **ModelConfig.cs** | 配置硬编码 | 高 | 模型ID、Endpoint都是const硬编码 |
| **LlmService.cs** | 重试参数硬编码 | 中 | MaxRetries、RetryDelayMs是常量 |
| **ToolService.cs** | 工具列表硬编码 | 高 | 工业工具列表不符合化工场景 |
| **KnowledgeBaseService.cs** | Tokenize方法不完整 | 中 | 中文分词ngram逻辑需要优化 |

### 2.2 架构缺口

| 功能模块 | 状态 | 说明 |
|---------|------|------|
| **化工知识库加载** | 部分完成 | ChemicalRAG已创建，但需完善 |
| **IntegrationService** | 缺失 | 工业系统集成接口（ERP/WMS/EHS） |
| **AuditService** | 缺失 | 等保三级操作审计 |
| **化工专用推理模块** | 缺失 | ComplianceCheckModule等模块 |

## 三、整改详细方案

### 3.1 新增化工专属配置模型

在AppConfig.cs中增加化工场景专用配置：
- ChemicalLlmConfig - 化工场景LLM配置
- ChemicalKnowledgeBaseConfig - 化工知识库配置
- IntegrationConfig - 工业系统集成配置
- AuditConfig - 等保三级审计配置

### 3.2 新增服务

- IntegrationService - ERP/WMS/EHS系统集成
- AuditService - 操作审计日志

### 3.3 新增模块

- ComplianceCheckModule - 合规审查模块
