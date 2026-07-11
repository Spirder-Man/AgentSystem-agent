<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { InspectionPlan } from '@/types/api';
import { ElMessage } from 'element-plus';
import { ArrowLeft } from '@element-plus/icons-vue';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const route = useRoute();
const router = useRouter();
const planId = route.params.planId as string;

const plan = ref<InspectionPlan | null>(null);
const loading = ref(true);
const error = ref('');
const executing = ref(false);

const statusBadge = computed(() => {
  const m: Record<string, { cls: string; label: string }> = {
    Draft: { cls: 'bg-slate-100 text-slate-600 border-slate-200', label: '草稿' },
    InProgress: { cls: 'bg-blue-50 text-blue-700 border-blue-200', label: '进行中' },
    Completed: { cls: 'bg-green-50 text-green-700 border-green-200', label: '已完成' },
    Archived: { cls: 'bg-slate-100 text-slate-400 border-slate-200', label: '已归档' },
  };
  return m[plan.value?.status ?? 'Draft'] ?? m['Draft'];
});

const typeLabel = computed(() => {
  const m: Record<string, string> = { DailyWeekly: '日常/周检', Monthly: '月度检查', PreHoliday: '节前检查' };
  const t = plan.value?.type;
  return m[t ?? ''] ?? t ?? '—';
});

async function fetchPlan() {
  loading.value = true; error.value = '';
  try {
    const { data } = await apiClient.get<InspectionPlan>(`/api/Inspection/plans/${planId}`);
    plan.value = data;
  } catch { error.value = '加载计划失败'; }
  finally { loading.value = false; }
}

async function executePlan() {
  if (!plan.value) return;
  executing.value = true;
  try {
    const { data } = await apiClient.post(`/api/Inspection/plans/${plan.value.planId}/execute`);
    ElMessage.success(`巡检完成，合规率 ${Math.round((data as { complianceRate: number }).complianceRate * 100)}%`);
    await fetchPlan();
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ElMessage.error(ae.response?.data?.error || '执行失败');
  } finally { executing.value = false; }
}

function goBack() { router.push('/inspection/plans'); }

onMounted(fetchPlan);
</script>

<template>
  <div class="space-y-4">
    <!-- 返回导航 -->
    <div class="flex items-center gap-2">
      <el-button :icon="ArrowLeft" size="small" text @click="goBack">返回计划列表</el-button>
    </div>

    <!-- 加载态 -->
    <SkeletonCard v-if="loading" />

    <!-- 错误态 -->
    <div v-else-if="error" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="error" :title="error" @action="fetchPlan" />
    </div>

    <!-- 正常态 -->
    <template v-else-if="plan">
      <!-- 头部 -->
      <div class="bg-white border border-slate-200 rounded p-6">
        <div class="flex items-start justify-between">
          <div>
            <h1 class="text-xl font-bold text-slate-900">{{ plan.name }}</h1>
            <div class="flex items-center gap-3 mt-2">
              <span class="inline-flex items-center gap-1 px-2 py-0.5 rounded border text-xs" :class="statusBadge.cls">
                {{ statusBadge.label }}
              </span>
              <span class="text-sm text-slate-500">📍 {{ plan.area }}</span>
              <span class="text-sm text-slate-500">👤 {{ plan.inspector }}</span>
            </div>
          </div>
          <button
            v-if="plan.status === 'Draft' || plan.status === 'InProgress'"
            @click="executePlan"
            :disabled="executing"
            class="px-4 py-2 rounded text-sm font-medium border border-green-200 text-green-700 bg-green-50 hover:bg-green-100 disabled:opacity-50 transition-colors"
          >{{ executing ? '执行中…' : '▶ 执行巡检' }}</button>
        </div>
      </div>

      <!-- 基本信息 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4">基本信息</h2>
        <dl class="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt class="text-slate-400 text-xs mb-1">巡检区域</dt>
            <dd class="text-slate-800">{{ plan.area }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs mb-1">计划类型</dt>
            <dd class="text-slate-800">{{ typeLabel }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs mb-1">检查人</dt>
            <dd class="text-slate-800">{{ plan.inspector }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs mb-1">计划日期</dt>
            <dd class="text-slate-800 font-mono text-xs">{{ new Date(plan.scheduledDate).toLocaleDateString('zh-CN') }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs mb-1">创建时间</dt>
            <dd class="text-slate-800 font-mono text-xs">{{ new Date(plan.createdAt).toLocaleDateString('zh-CN') }}</dd>
          </div>
          <div v-if="plan.notes" class="col-span-2">
            <dt class="text-slate-400 text-xs mb-1">备注</dt>
            <dd class="text-slate-800">{{ plan.notes }}</dd>
          </div>
        </dl>
      </div>

      <!-- 检查项目 -->
      <div class="bg-white border border-slate-200 rounded overflow-hidden">
        <div class="px-5 py-3 border-b border-slate-200 bg-slate-50">
          <h2 class="text-sm font-semibold text-slate-700">检查项目 ({{ plan.items.length }} 项)</h2>
        </div>
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-slate-200 bg-slate-50">
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-12">#</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">检查内容</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-32">检查能力</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-44">预期法规</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, idx) in plan.items" :key="item.itemId" class="border-b border-slate-100">
              <td class="px-4 py-3 text-xs text-slate-400 font-mono">{{ idx + 1 }}</td>
              <td class="px-4 py-3 text-slate-800">{{ item.query }}</td>
              <td class="px-4 py-3">
                <span class="text-xs px-1.5 py-0.5 rounded bg-blue-50 text-blue-700 border border-blue-100">{{ item.capabilityName }}</span>
              </td>
              <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ item.expectedRegulation || '—' }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="plan.items.length === 0" class="px-5 py-8 text-center text-sm text-slate-400">暂无检查项目</div>
      </div>
    </template>
  </div>
</template>
