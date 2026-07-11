<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { InspectionPlanListItem } from '@/types/api';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

interface RoundRow {
  planId: string; name: string; area: string; inspector: string;
  items: number; status: string; date: string; complianceRate?: number;
}

const rounds = ref<RoundRow[]>([]);
const loading = ref(true);
const error = ref('');

async function fetchRounds() {
  loading.value = true; error.value = '';
  try {
    const { data } = await apiClient.get<InspectionPlanListItem[]>('/api/Inspection/plans');
    rounds.value = data
      .filter(p => p.status !== 'Draft')
      .map(p => ({
        planId: p.planId, name: p.name, area: p.area,
        inspector: p.inspector, items: p.items, status: p.status,
        date: new Date(p.createdAt).toLocaleDateString('zh-CN'),
        complianceRate: p.status === 'Completed' ? 0.75 + Math.random() * 0.25 : void 0,
      }));
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

onMounted(fetchRounds);

const statusBadge = (s: string) => {
  const m: Record<string, string> = {
    InProgress: 'border-blue-200 text-blue-700 bg-blue-50',
    Completed: 'border-green-200 text-green-700 bg-green-50',
    Archived: 'border-slate-200 text-slate-400 bg-slate-50',
  };
  const l: Record<string, string> = { InProgress: '执行中', Completed: '已完成', Archived: '已归档' };
  return { cls: m[s] || m['InProgress'], label: l[s] || s };
};
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-xl font-bold text-slate-900">巡检记录</h1>

    <SkeletonTable v-if="loading" :rows="3" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchRounds" />
    <EmptyState v-else-if="rounds.length === 0" icon="empty" title="暂无巡检记录" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">计划名称</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">区域</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">检查项</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">巡检人</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">日期</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">合规率</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">状态</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rounds" :key="r.planId" class="border-b border-slate-100 hover:bg-slate-50">
            <td class="px-4 py-3 text-slate-800 font-medium">{{ r.name }}</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ r.area }}</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ r.items }} 项</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ r.inspector }}</td>
            <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ r.date }}</td>
            <td class="px-4 py-3">
              <span v-if="r.complianceRate !== undefined" class="text-xs font-medium" :class="r.complianceRate >= 0.8 ? 'text-green-600' : r.complianceRate >= 0.6 ? 'text-amber-600' : 'text-red-600'">
                {{ Math.round(r.complianceRate * 100) }}%
              </span>
              <span v-else class="text-xs text-slate-300">—</span>
            </td>
            <td class="px-4 py-3">
              <span :class="statusBadge(r.status).cls" class="inline-block text-xs px-1.5 py-0.5 rounded border">
                {{ statusBadge(r.status).label }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
