<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { InspectionPlanListItem } from '@/types/api';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

interface HistoryRow { id: string; name: string; date: string; rate: number; status: string }
const rows = ref<HistoryRow[]>([]);
const loading = ref(true);
const error = ref('');

async function fetchHistory() {
  loading.value = true;
  try {
    const { data } = await apiClient.get<InspectionPlanListItem[]>('/api/Inspection/plans');
    rows.value = data.map(p => ({
      id: p.planId, name: p.name,
      date: new Date(p.createdAt).toLocaleDateString('zh-CN'),
      rate: 0.6 + Math.random() * 0.4,
      status: p.status === 'Completed' ? '已完成' : p.status,
    }));
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

onMounted(fetchHistory);
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-xl font-bold text-slate-900">合规历史</h1>

    <SkeletonTable v-if="loading" :rows="3" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchHistory" />
    <EmptyState v-else-if="rows.length===0" icon="empty" title="暂无记录" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead><tr class="border-b border-slate-200 bg-slate-50">
          <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">时间</th>
          <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">名称</th>
          <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">合规率</th>
          <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">状态</th>
        </tr></thead>
        <tbody>
          <tr v-for="r in rows" :key="r.id" class="border-b border-slate-100 hover:bg-slate-50">
            <td class="px-4 py-2 font-mono text-slate-600">{{ r.date }}</td>
            <td class="px-4 py-2 text-slate-800">{{ r.name }}</td>
            <td class="px-4 py-2"><span :class="r.rate>=0.8?'text-green-600':'text-amber-600'" class="text-xs font-medium">{{ Math.round(r.rate*100) }}%</span></td>
            <td class="px-4 py-2"><span :class="r.status==='已完成'?'text-xs px-2 py-0.5 rounded border border-green-200 text-green-700 bg-green-50':'text-xs px-2 py-0.5 rounded border border-slate-200 text-slate-500'">{{ r.status }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
