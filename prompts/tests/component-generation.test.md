# 组件生成质量评估 — 测试用例集

> **版本**: v1.0.0  
> **用途**: 评估不同提示词版本对 AI 代码生成质量的影响  
> **测试框架**: Vitest + Playwright (视觉回归)  

---

## 评估维度

| 维度 | 权重 | 说明 |
|------|------|------|
| **可运行性** | 30% | `vue-tsc --noEmit` 零错误 + `npm run dev:mock` 可启动 |
| **类型安全** | 20% | 零 `any` 使用、接口与 types/api.ts 对齐 |
| **项目约定** | 20% | 符合 vue3-dev.system.md 的所有核心约定 |
| **Mock 完整性** | 15% | handler 已注册、数据覆盖边界情况 |
| **代码质量** | 15% | 组件拆分合理、无重复代码、命名规范 |

## 测试用例

### TC-01: 「危化品资产」表格页

**输入**:
```
ENTITY_NAME=危化品资产
ENTITY_KEY=chemicalAsset
API_PREFIX=/api/Inspection/assets
LIST_RESPONSE_TYPE=ChemicalAsset[]
COLUMNS:
  - prop: name, label: 名称, sortable: true
  - prop: casNumber, label: CAS号, width: 140
  - prop: location, label: 存放位置
  - prop: quantityTons, label: 数量(吨), sortable: true
  - prop: storageCondition, label: 储存条件
  - prop: responsiblePerson, label: 负责人, width: 100
  - prop: isMajorHazardSource, label: 重大危险源, formatter: boolean→是/否
  - prop: lastCheckResult, label: 上次检查, formatter: boolean→合规/不合规/未检查
FORM_FIELDS:
  - prop: name, label: 名称, type: input, required: true
  - prop: casNumber, label: CAS号, type: input, required: true
  - prop: location, label: 存放位置, type: select, options: [{label:甲类仓库A区,value:A1},{label:甲类仓库B区,value:B1},{label:乙类仓库,value:C1}]
  - prop: quantityTons, label: 数量(吨), type: number, required: true
  - prop: storageCondition, label: 储存条件, type: textarea
  - prop: responsiblePerson, label: 负责人, type: input
  - prop: isMajorHazardSource, label: 重大危险源, type: select, options: [{label:是,value:true},{label:否,value:false}]
```

**验收标准**:
- [ ] 表格展示 8 条 Mock 数据（对齐 `mocks/data/inspection.ts` 现有 `mockAssets`）
- [ ] "重大危险源" 列显示 "是/否" 而非 true/false
- [ ] "上次检查" 列显示 "合规/不合规/未检查" 而非 null
- [ ] 新增对话框表单校验：名称和 CAS 号必填
- [ ] 删除确认弹窗显示资产名称
- [ ] 搜索框可搜索名称/CAS号/位置

**评分** (0-100):
```
可运行性: ___/30
类型安全: ___/20
项目约定: ___/20
Mock完整性: ___/15
代码质量: ___/15
总分: ___/100
```

---

### TC-02: 「合规工单」表格页

**输入**:
```
ENTITY_NAME=合规工单
ENTITY_KEY=ticketItem
API_PREFIX=/api/Tickets
LIST_RESPONSE_TYPE=TicketListResponse
COLUMNS:
  - prop: id, label: ID, width: 60
  - prop: issue, label: 问题描述
  - prop: priority, label: 优先级, width: 80, sortable: true
  - prop: status, label: 状态, width: 100, formatter: 状态映射
  - prop: assignee, label: 负责人, width: 100
  - prop: regulationRef, label: 法规引用, width: 160
  - prop: suggestedDeadline, label: 建议截止日期, formatter: datetime
  - prop: isOpen, label: 是否开启, formatter: boolean→是/否
FORM_FIELDS: (无 — 工单不支持手动新增,仅查看+状态流转)
```

**验收标准**:
- [ ] 表格展示 Mock 数据（5 条工单，参考 `mocks/data/tickets.ts`）
- [ ] 状态列显示中文映射（New→新建, Confirmed→已确认, InProgress→处理中, Remediated→已整改, VerifiedClosed→已验证关闭, Closed→已关闭, FalsePositive→误报）
- [ ] 优先级列根据值显示不同颜色标签（Critical→红色, High→橙色, Medium→黄色, Low→绿色）
- [ ] 操作列显示「确认/开始/完成」等状态流转按钮（非编辑/删除）
- [ ] 状态流转按钮根据当前状态自动显示合法操作
- [ ] 响应数据从 `data.tickets` 而非 `data` 根层获取

**评分** (0-100):
```
可运行性: ___/30
类型安全: ___/20
项目约定: ___/20
Mock完整性: ___/15
代码质量: ___/15
总分: ___/100
```

---

### TC-03: 无新增表单的只读列表页

**输入**:
```
ENTITY_NAME=合规总览
ENTITY_KEY=complianceSummary
API_PREFIX=/api/Compliance/summary
LIST_RESPONSE_TYPE=ComplianceSummary (单对象，非数组！)
COLUMNS: (不使用表格 — 用 el-descriptions + el-statistic)
FORM_FIELDS: (无)
```

**验收标准**:
- [ ] 不使用 `<el-table>`，改用 `<el-descriptions>` + 统计卡片布局
- [ ] 正确从单个对象（非数组）读取数据
- [ ] `riskDistribution` 用 ECharts 饼图呈现
- [ ] `findingsBySeverity` 用柱状图呈现
- [ ] 无多余 CRUD 对话框代码
- [ ] 布局在小屏下自适应

**评分** (0-100):
```
可运行性: ___/30
类型安全: ___/20
项目约定: ___/20
Mock完整性: ___/15
代码质量: ___/15
总分: ___/100
```

---

### TC-04: `no-any` 约束合规检查

**输入**: 使用 TC-01 生成的代码 + 以下恶意输入片段（手动混入）

```typescript
// 恶意片段 1 — 混入 composable
export function useBadFetch(): Promise<any> {
  const params: any = { page: 1 };
  return apiClient.get<any>('/api/data', { params }).then((r: any) => r.data);
}

// 恶意片段 2 — 混入页面组件
const handleEvent = (e: any) => { console.log(e); };

// 恶意片段 3 — 混入 store
state: (): { list: any[]; loading: boolean } => ({ list: [], loading: false }),
```

**验收标准**:

检查 TC-01 生成代码中：
- [ ] 所有 `: any`、`<any>`、`as any`、`=> any`、`any[]` 出现次数 = 0
- [ ] 函数返回值类型全部显式标注
- [ ] 组件 props 使用 `defineProps<{ ... }>()`
- [ ] `Promise<any>` 使用次数 = 0

**评分** (0-100):
```
any 出现次数: ___ (0=100分, >0=0分)
```

---

### TC-05: MSW Mock 数据完整性

**输入**: 使用 TC-01 生成的 Mock 数据文件

**验收标准**:
- [ ] Mock 数据 ≥ 5 条
- [ ] 每条数据所有必填字段都有值（无 undefined）
- [ ] 枚举字段值在合法范围内
- [ ] 数据真实感强（名称、CAS 号、位置与实际化工场景一致）
- [ ] handler 注入 `simulateLlmDelay()`（如端点涉及 LLM）
- [ ] handler 注入 `maybeSimulateError()`（5% 503 错误）

**评分** (0-100):
```
数据完整性: ___/40
真实感: ___/20
延迟/错误注入: ___/20
类型对齐: ___/20
总分: ___/100
```

---

## 评分汇总表

| 用例 | 提示词版本 | 可运行性 | 类型安全 | 项目约定 | Mock完整 | 代码质量 | 总分 | 备注 |
|------|-----------|---------|---------|---------|---------|---------|------|------|
| TC-01 | v1.0.0 | __/30 | __/20 | __/20 | __/15 | __/15 | __/100 | |
| TC-02 | v1.0.0 | __/30 | __/20 | __/20 | __/15 | __/15 | __/100 | |
| TC-03 | v1.0.0 | __/30 | __/20 | __/20 | __/15 | __/15 | __/100 | |
| TC-04 | v1.0.0 | any次数=__ | — | — | — | — | __/100 | |
| TC-05 | v1.0.0 | __/40 | __/20 | — | __/20 | __/20 | __/100 | |
| **平均** | | | | | | | **__/100** | |

## 迭代记录

| 日期 | Git Tag | 修改内容 | 平均分变化 | 关键发现 |
|------|---------|----------|-----------|---------|
| 2026-07-10 | prompts-v1.0.0 | 初始版本 | — | — |
