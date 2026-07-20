# no-any 约束 — TypeScript 严格模式铁律

> **版本**: v1.0.0  
> **优先级**: 🔴 P0 — 违反即拒绝  
> **适用范围**: agent1-web/src/**/*.ts, *.vue  

---

## 规则

**禁止在任何 TypeScript / Vue SFC 代码中使用 `any` 类型。**

## 理由

| 问题 | 说明 |
|------|------|
| 类型安全 | `any` 关闭了 TypeScript 的所有类型检查，使 `strict: true` 形同虚设 |
| 重构风险 | `any` 类型的变量在重构时不会被编译器检测到错误 |
| 后端契约 | Agent1 后端 C# 强类型体系要求前端类型精确对齐 |
| 代码审查 | `any` 是代码审查的高频否决项 |

## 例外

以下场景**可豁免**（需在注释中说明原因）：

```typescript
// ✅ 允许: 第三方库类型定义不完整
const data = await legacyLib.call({}) as Record<string, unknown>;
// 原因: legacy-lib@1.x 缺少完整 TS 声明

// ✅ 允许: JSON.parse 动态内容（需配合 zod 校验）
const raw = JSON.parse(apiResponse) as unknown;
const validated = MySchema.parse(raw); // zod 提供运行时类型安全
```

## 替代方案速查

| 场景 | ❌ any | ✅ 正确写法 |
|------|--------|-------------|
| 不知道类型 | `data: any` | `data: unknown` + 类型守卫 |
| 动态对象 | `obj: any` | `Record<string, unknown>` |
| 泛型占位 | `<any>` | `<T>` + extends 约束 |
| 事件处理 | `(e: any)` | `(e: Event)` / `(e: MouseEvent)` |
| 组件 props | `props: any` | 显式 `defineProps<{ ... }>()` |
| 临时调试 | `as any` | `// eslint-disable-next-line @typescript-eslint/no-explicit-any` |
| 第三方库 | `require('x'): any` | 安装 `@types/x` 或手写 `.d.ts` |

## 检查命令

```bash
# TypeScript 编译检查
cd agent1-web && npx vue-tsc --noEmit

# 全局搜索 any 使用（排除 node_modules 和 .d.ts）
rg '\bany\b' --type ts --type vue -g '!*.d.ts' -g '!node_modules/**' src/

# 在 CI 中阻断
# .github/workflows/ci.yml 已配置: vue-tsc --noEmit --strict
```

## 违规示例与修正

```typescript
// ❌ 违规: any 作为函数参数
function fetchData(params: any): Promise<any> {
  return apiClient.get('/api/data', { params });
}

// ✅ 修正: 使用泛型 + 类型约束
function fetchData<T>(params: Record<string, unknown>): Promise<T> {
  return apiClient.get<T>('/api/data', { params }).then(r => r.data);
}
```

```typescript
// ❌ 违规: any 作为响应类型
const { data } = useQuery<any[]>({ queryKey: ['items'], ... });

// ✅ 修正: 显式声明类型
type Item = { id: number; name: string; status: string };
const { data } = useQuery<Item[]>({ queryKey: ['items'], ... });
```

```typescript
// ❌ 违规: any 作为 store state
state: (): { items: any[] } => ({ items: [] }),

// ✅ 修正: 从 types/api.ts 导入
import type { Item } from '@/types/api';
state: (): { items: Item[] } => ({ items: [] }),
```

## A/B 测试追踪

- **约束版本**: v1.0.0
- **评估指标**: `any` 出现次数、`vue-tsc --noEmit` 错误数、代码审查拒绝率
