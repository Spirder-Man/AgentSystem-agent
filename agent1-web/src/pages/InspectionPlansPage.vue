<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { InspectionPlanListItem, CreatePlanRequest } from '@/types/api';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';
import { ElMessage } from 'element-plus';

const plans = ref<InspectionPlanListItem[]>([]);
const loading = ref(true);
const error = ref('');
const showCreate = ref(false);
const submitting = ref(false);
const executingId = ref<string | null>(null);
const router = useRouter();

// 新建表单
const newPlan = ref({ name: '', area: '', type: 'DailyWeekly', notes: '', items: [{ query: '', capability: 'regulatory-audit' }] });

async function fetchPlans() {
  loading.value = true; error.value = '';
  try { const { data } = await apiClient.get<InspectionPlanListItem[]>('/api/Inspection/plans'); plans.value = data; }
  catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

async function createPlan() {
  const p = newPlan.value;
  if (!p.name.trim() || !p.area.trim()) { ElMessage.warning('名称和区域不能为空'); return; }
  const items = p.items.filter(i => i.query.trim());
  if (items.length === 0) { ElMessage.warning('请至少添加一个检查项'); return; }
  submitting.value = true;
  try {
    await apiClient.post('/api/Inspection/plans', {
      name: p.name.trim(), area: p.area.trim(), type: p.type,
      notes: p.notes.trim(), items: items.map(i => ({ query: i.query.trim(), capability: i.capability })),
    } as CreatePlanRequest);
    ElMessage.success('计划已创建');
    showCreate.value = false;
    newPlan.value = { name: '', area: '', type: 'DailyWeekly', notes: '', items: [{ query: '', capability: 'regulatory-audit' }] };
    await fetchPlans();
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ElMessage.error(ae.response?.data?.error || '创建失败');
  } finally { submitting.value = false; }
}

async function executePlan(plan: InspectionPlanListItem) {
  executingId.value = plan.planId;
  try {
    const { data } = await apiClient.post(`/api/Inspection/plans/${plan.planId}/execute`);
    ElMessage.success(`巡检完成，合规率 ${Math.round((data as { complianceRate: number }).complianceRate * 100)}%`);
    await fetchPlans();
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ElMessage.error(ae.response?.data?.error || '执行失败');
  } finally { executingId.value = null; }
}

function addItem() { newPlan.value.items.push({ query: '', capability: 'regulatory-audit' }); }
function removeItem(idx: number) { if (newPlan.value.items.length > 1) newPlan.value.items.splice(idx, 1); }

const statusBadge = (s: string) => {
  const m: Record<string, string> = {
    Draft: 'border-slate-200 text-slate-600 bg-slate-50',
    InProgress: 'border-blue-200 text-blue-700 bg-blue-50',
    Completed: 'border-green-200 text-green-700 bg-green-50',
    Archived: 'border-slate-200 text-slate-400 bg-slate-50',
  };
  const label: Record<string, string> = { Draft: '草稿', InProgress: '进行中', Completed: '已完成', Archived: '已归档' };
  return { cls: m[s] || m['Draft'], label: label[s] || s };
};

onMounted(fetchPlans);
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">巡检计划</h1>
      <button @click="showCreate = !showCreate" class="text-xs px-3 py-1.5 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 transition-colors">
        {{ showCreate ? '取消' : '+ 新建计划' }}
      </button>
    </div>

    <!-- 新建表单 -->
    <div v-if="showCreate" class="bg-white border border-blue-200 rounded p-4 space-y-3">
      <h3 class="text-sm font-semibold text-slate-700">新建巡检计划</h3>
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div>
          <label class="text-xs text-slate-400 block mb-1">计划名称</label>
          <input v-model="newPlan.name" class="w-full px-2 py-1.5 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="例：甲类仓库周检" />
        </div>
        <div>
          <label class="text-xs text-slate-400 block mb-1">巡检区域</label>
          <input v-model="newPlan.area" class="w-full px-2 py-1.5 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="例：甲类仓库A区" />
        </div>
        <div>
          <label class="text-xs text-slate-400 block mb-1">计划类型</label>
          <select v-model="newPlan.type" class="w-full px-2 py-1.5 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400">
            <option value="DailyWeekly">日常/周检</option>
            <option value="Monthly">月度检查</option>
            <option value="PreHoliday">节前检查</option>
          </select>
        </div>
      </div>
      <div>
        <label class="text-xs text-slate-400 block mb-1">备注</label>
        <input v-model="newPlan.notes" class="w-full px-2 py-1.5 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="选填" />
      </div>
      <div>
        <label class="text-xs text-slate-400 block mb-1">检查项 ({{ newPlan.items.length }})</label>
        <div class="space-y-2">
          <div v-for="(item, idx) in newPlan.items" :key="idx" class="flex gap-2 items-center">
            <input v-model="item.query" class="flex-1 px-2 py-1.5 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="例：苯与丙酮储存间距" />
            <select v-model="item.capability" class="w-36 px-2 py-1.5 text-xs border border-slate-300 rounded">
              <option value="regulatory-audit">法规审核</option>
              <option value="storage-compliance">储存合规</option>
              <option value="safety-distance">安全距离</option>
              <option value="ghs-label-check">GHS标签</option>
            </select>
            <button @click="removeItem(idx)" class="text-xs text-red-500 hover:text-red-700 px-1">✕</button>
          </div>
        </div>
        <button @click="addItem" class="text-xs text-blue-600 hover:text-blue-800 mt-2">+ 添加检查项</button>
      </div>
      <button @click="createPlan" :disabled="submitting" class="text-sm px-4 py-2 rounded text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-50">
        {{ submitting ? '创建中…' : '创建计划' }}
      </button>
    </div>

    <!-- 列表 -->
    <SkeletonTable v-if="loading" :rows="3" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchPlans" />
    <EmptyState v-else-if="plans.length === 0" icon="empty" title="暂无巡检计划" />

    <div v-else class="space-y-3">
      <div v-for="plan in plans" :key="plan.planId" class="bg-white border border-slate-200 rounded p-4">
        <div class="flex items-start justify-between">
          <div class="flex-1">
            <div class="flex items-center gap-3 mb-2">
              <router-link :to="'/inspection/plans/' + plan.planId" class="text-sm font-semibold text-blue-700 hover:text-blue-900 hover:underline transition-colors">{{ plan.name }}</router-link>
              <span :class="statusBadge(plan.status).cls" class="inline-block text-xs px-1.5 py-0.5 rounded border">
                {{ statusBadge(plan.status).label }}
              </span>
            </div>
            <div class="flex gap-4 text-xs text-slate-500">
              <span>📍 {{ plan.area }}</span>
              <span>👤 {{ plan.inspector }}</span>
              <span>📋 {{ plan.items }} 项</span>
              <span class="font-mono">{{ new Date(plan.createdAt).toLocaleDateString('zh-CN') }}</span>
            </div>
          </div>
          <div class="flex items-center gap-2 ml-4">
            <button
              v-if="plan.status === 'Draft' || plan.status === 'InProgress'"
              @click="executePlan(plan)"
              :disabled="executingId === plan.planId"
              class="text-xs px-3 py-1.5 rounded border border-green-200 text-green-700 bg-green-50 hover:bg-green-100 disabled:opacity-50 whitespace-nowrap"
            >{{ executingId === plan.planId ? '执行中…' : '▶ 执行' }}</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
