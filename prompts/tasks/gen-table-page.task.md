# 生成表格管理页 — 任务模板

> **版本**: v1.0.0  
> **适用场景**: 需要生成一个带 CRUD 操作的企业级数据表格页面  
> **前置条件**: 已在 `src/types/api.ts` 中定义相关 interface  

---

## 任务目标

生成一个完整的 Element Plus 表格管理页面，包含：
- 数据表格（分页、排序、筛选）
- 新增/编辑对话框（vee-validate + zod 校验）
- 删除确认
- MSW Mock handler
- Vue Query 数据管理

## 输入参数

| 参数 | 必填 | 说明 | 示例 |
|------|------|------|------|
| `ENTITY_NAME` | ✅ | 实体中文名 | 危化品资产 |
| `ENTITY_KEY` | ✅ | 实体英文标识（camelCase） | chemicalAsset |
| `API_PREFIX` | ✅ | API 路由前缀 | /api/Inspection/assets |
| `LIST_RESPONSE_TYPE` | ✅ | 列表响应类型（来自 types/api.ts） | ChemicalAsset[] |
| `COLUMNS` | ✅ | 表格列定义 | 见下方格式 |
| `FORM_FIELDS` | ⚠️ | 表单字段（无则跳过新增/编辑） | 见下方格式 |

### COLUMNS 格式

```typescript
interface ColumnDef {
  prop: string;        // 字段 key（对应 interface 属性）
  label: string;       // 列标题（中文）
  width?: number;      // 列宽
  sortable?: boolean;  // 是否可排序
  formatter?: string;  // 格式化说明（如 'datetime', 'boolean→是/否'）
}
```

### FORM_FIELDS 格式

```typescript
interface FormField {
  prop: string;           // 字段 key
  label: string;          // 标签
  type: 'input' | 'select' | 'date' | 'number' | 'textarea';
  required?: boolean;
  options?: { label: string; value: string }[];  // select 选项
  zodRule?: string;       // zod 校验规则（如 'z.string().min(1)'）
}
```

## 生成清单

生成以下文件（按依赖顺序）：

### 1️⃣ `src/types/api.ts` — 类型补充（如需要）

新增/补全接口类型定义，**禁止使用 `any`**。

### 2️⃣ `src/mocks/data/{{ENTITY_KEY}}.ts` — Mock 数据

```typescript
// 模板结构
import type { {{LIST_RESPONSE_TYPE}} } from '../../types/api';

export const mock{{ENTITY_KEY}}List: {{LIST_RESPONSE_TYPE}}[] = [
  // 至少 5 条模拟数据，覆盖各种状态/类型
];

// 如涉及状态流转，需实现状态机函数
export function apply{{ENTITY_KEY}}Update(id: number, action: string): {{ENTITY_KEY}} | null {
  // 状态流转逻辑
}
```

### 3️⃣ `src/mocks/handlers.ts` — Handler 注册

在 `handlers` 数组中追加以下 handler：

```typescript
// ── GET {{API_PREFIX}} ──
http.get('{{API_PREFIX}}', async () => {
  await delay(200);
  return HttpResponse.json(mock{{ENTITY_KEY}}List);
}),

// ── POST {{API_PREFIX}} ──
http.post('{{API_PREFIX}}', async ({ request }) => {
  await delay(300);
  const body = await request.json();
  // 模拟创建逻辑
  return HttpResponse.json({ id: Date.now(), ...body });
}),

// ── PUT {{API_PREFIX}}/:id ──
http.put('{{API_PREFIX}}/:id', async ({ params, request }) => {
  await delay(300);
  const body = await request.json();
  return HttpResponse.json({ id: Number(params.id), ...body });
}),

// ── DELETE {{API_PREFIX}}/:id ──
http.delete('{{API_PREFIX}}/:id', async ({ params }) => {
  await delay(200);
  return HttpResponse.json({ success: true });
}),
```

### 4️⃣ `src/composables/use{{ENTITY_KEY}}.ts` — Vue Query 封装

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query';
import apiClient from '@/lib/axios';
import type { {{LIST_RESPONSE_TYPE}} } from '@/types/api';

const QUERY_KEY = '{{ENTITY_KEY}}';

export function use{{ENTITY_KEY}}List() {
  return useQuery({
    queryKey: [QUERY_KEY],
    queryFn: () => apiClient.get<{{LIST_RESPONSE_TYPE}}[]>('{{API_PREFIX}}').then(r => r.data),
  });
}

export function useCreate{{ENTITY_KEY}}() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: Partial<{{ENTITY_KEY}}>) => 
      apiClient.post('{{API_PREFIX}}', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}

export function useUpdate{{ENTITY_KEY}}() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...data }: { id: number } & Partial<{{ENTITY_KEY}}>) =>
      apiClient.put(`{{API_PREFIX}}/${id}`, data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}

export function useDelete{{ENTITY_KEY}}() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiClient.delete(`{{API_PREFIX}}/${id}`).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}
```

### 5️⃣ `src/pages/{{ENTITY_KEY}}Page.vue` — 页面组件

```vue
<script setup lang="ts">
import { ref, computed } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { use{{ENTITY_KEY}}List, useCreate{{ENTITY_KEY}}, useUpdate{{ENTITY_KEY}}, useDelete{{ENTITY_KEY}} } from '@/composables/use{{ENTITY_KEY}}';
import type { {{ENTITY_KEY}} } from '@/types/api';

// ── 查询参数 ──
const page = ref(1);
const pageSize = ref(20);
const searchKeyword = ref('');

const { data, isLoading } = use{{ENTITY_KEY}}List();
const createMutation = useCreate{{ENTITY_KEY}}();
const updateMutation = useUpdate{{ENTITY_KEY}}();
const deleteMutation = useDelete{{ENTITY_KEY}}();

// ── 表格数据 ──
const tableData = computed(() => {
  let list = data.value ?? [];
  if (searchKeyword.value) {
    const kw = searchKeyword.value.toLowerCase();
    list = list.filter(item => 
      Object.values(item).some(v => String(v).toLowerCase().includes(kw))
    );
  }
  return list;
});

// ── 对话框控制 ──
const dialogVisible = ref(false);
const isEditing = ref(false);
const formData = ref<Partial<{{ENTITY_KEY}}>>({});

function openCreateDialog() {
  isEditing.value = false;
  formData.value = {};
  dialogVisible.value = true;
}

function openEditDialog(row: {{ENTITY_KEY}}) {
  isEditing.value = true;
  formData.value = { ...row };
  dialogVisible.value = true;
}

async function handleSubmit() {
  if (isEditing.value) {
    await updateMutation.mutateAsync(formData.value as { id: number } & Partial<{{ENTITY_KEY}}>);
    ElMessage.success('更新成功');
  } else {
    await createMutation.mutateAsync(formData.value);
    ElMessage.success('创建成功');
  }
  dialogVisible.value = false;
}

async function handleDelete(row: {{ENTITY_KEY}}) {
  await ElMessageBox.confirm(`确定删除「${(row as any).name ?? row.id}」？`, '确认删除', {
    type: 'warning',
  });
  await deleteMutation.mutateAsync((row as any).id);
  ElMessage.success('已删除');
}
</script>

<template>
  <div class="p-6">
    <!-- 工具栏 -->
    <div class="flex justify-between items-center mb-4">
      <h1 class="text-2xl font-bold">{{ENTITY_NAME}}管理</h1>
      <el-button type="primary" @click="openCreateDialog">+ 新增{{ENTITY_NAME}}</el-button>
    </div>

    <!-- 搜索栏 -->
    <el-card class="mb-4">
      <el-input 
        v-model="searchKeyword" 
        placeholder="搜索{{ENTITY_NAME}}…" 
        clearable 
        class="max-w-xs"
      />
    </el-card>

    <!-- 数据表格 -->
    <el-card>
      <el-table 
        :data="tableData" 
        v-loading="isLoading"
        stripe
        border
      >
        {{#each COLUMNS}}
        <el-table-column 
          prop="{{prop}}" 
          label="{{label}}" 
          {{#if width}}width="{{width}}"{{/if}}
          {{#if sortable}}sortable{{/if}}
        >
          {{#if formatter}}
          <template #default="{ row }">
            <!-- 格式化逻辑 -->
          </template>
          {{/if}}
        </el-table-column>
        {{/each}}
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button size="small" @click="openEditDialog(row)">编辑</el-button>
            <el-button size="small" type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 分页 -->
      <div class="flex justify-end mt-4">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="tableData.length"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
        />
      </div>
    </el-card>

    <!-- 新增/编辑对话框 -->
    <el-dialog 
      v-model="dialogVisible" 
      :title="isEditing ? '编辑{{ENTITY_NAME}}' : '新增{{ENTITY_NAME}}'"
      width="600px"
    >
      <el-form :model="formData" label-width="100px">
        {{#each FORM_FIELDS}}
        <el-form-item label="{{label}}" {{#if required}}required{{/if}}>
          {{#if type === 'input'}}
          <el-input v-model="formData.{{prop}}" />
          {{/if}}
          {{#if type === 'select'}}
          <el-select v-model="formData.{{prop}}" class="w-full">
            {{#each options}}
            <el-option label="{{label}}" value="{{value}}" />
            {{/each}}
          </el-select>
          {{/if}}
          {{#if type === 'textarea'}}
          <el-input v-model="formData.{{prop}}" type="textarea" :rows="3" />
          {{/if}}
          {{#if type === 'number'}}
          <el-input-number v-model="formData.{{prop}}" class="w-full" />
          {{/if}}
        </el-form-item>
        {{/each}}
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button 
          type="primary" 
          @click="handleSubmit"
          :loading="createMutation.isPending.value || updateMutation.isPending.value"
        >
          确认
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
/* 仅在 Tailwind 无法表达时添加 */
</style>
```

### 6️⃣ `src/router/index.ts` — 路由注册

```typescript
{
  path: '/{{ENTITY_KEY}}',
  name: '{{ENTITY_KEY}}',
  component: () => import('@/pages/{{ENTITY_KEY}}Page.vue'),
  meta: { title: '{{ENTITY_NAME}}管理', requiresAuth: true },
},
```

## 验收标准

生成的代码必须通过以下检查：

- [ ] `npx vue-tsc --noEmit` 无类型错误
- [ ] `npm run dev:mock` 启动后页面可访问
- [ ] 表格数据从 MSW Mock 正确加载
- [ ] 新增/编辑对话框提交后表格刷新
- [ ] 删除确认后数据移除
- [ ] 搜索功能正常工作
- [ ] 无 `any` 类型使用
- [ ] 所有文案使用中文

## A/B 测试追踪

- **提示词版本**: v1.0.0
- **生成目标**: `{{ENTITY_KEY}}Page.vue` + 配套文件
- **评估指标**: 首次可运行率、类型错误数、需人工修改行数、耗时
