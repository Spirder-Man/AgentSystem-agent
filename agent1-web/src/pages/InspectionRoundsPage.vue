<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { InspectionRoundListItem } from '@/types/api';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const router = useRouter();
const rounds = ref<InspectionRoundListItem[]>([]);
const loading = ref(true);
const error = ref('');
const page = ref(1);
const pageSize = ref(20);

async function fetchRounds() {
  loading.value = true; error.value = '';
  try {
    const { data } = await apiClient.get<InspectionRoundListItem[]>('/api/Inspection/rounds');
    rounds.value = data;
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

function viewReport(round: InspectionRoundListItem) {
  router.push(`/inspection/report/${round.roundId}`);
}

function viewDetail(round: InspectionRoundListItem) {
  router.push(`/inspection/rounds/${round.roundId}`);
}

function rateColor(r: number) {
  if (r >= 0.9) return 'text-green-600';
  if (r >= 0.7) return 'text-amber-600';
  return 'text-red-600';
}

function formatTime(iso: string) {
  const d = new Date(iso);
  return `${d.toLocaleDateString('zh-CN')} ${d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}`;
}

onMounted(fetchRounds);

const pagedRounds = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return rounds.value.slice(start, start + pageSize.value);
});
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-xl font-bold text-slate-900">巡检记录</h1>

    <SkeletonTable v-if="loading" :rows="3" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchRounds" />
    <EmptyState v-else-if="rounds.length === 0" icon="empty" title="暂无巡检记录，请先创建并执行巡检计划" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">计划名称</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">执行时间</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">合规率</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">合格/不合格</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">工单</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">执行人</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in pagedRounds" :key="r.roundId" class="border-b border-slate-100 hover:bg-slate-50 cursor-pointer" @click="viewDetail(r)">
            <td class="px-4 py-3 text-slate-800 font-medium">{{ r.planName }}</td>
            <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ formatTime(r.startedAt) }}</td>
            <td class="px-4 py-3">
              <span :class="rateColor(r.complianceRate)" class="text-xs font-bold">{{ Math.round(r.complianceRate * 100) }}%</span>
            </td>
            <td class="px-4 py-3">
              <span class="text-xs text-green-600">{{ r.compliantCount }} 合格</span>
              <span v-if="r.nonCompliantCount > 0" class="text-xs text-red-600 ml-1">{{ r.nonCompliantCount }} 不合格</span>
            </td>
            <td class="px-4 py-3">
              <span v-if="r.ticketCount > 0" class="text-xs font-medium text-orange-600">{{ r.ticketCount }} 个工单</span>
              <span v-else class="text-xs text-slate-300">—</span>
            </td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ r.executedBy }}</td>
            <td class="px-4 py-3" @click.stop>
              <button @click="viewDetail(r)" class="text-xs px-2 py-1 rounded border border-slate-200 text-slate-600 bg-white hover:bg-slate-50 transition-colors mr-1">详情</button>
              <button @click="viewReport(r)" class="text-xs px-2 py-1 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 transition-colors">报告</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 分页 -->
    <div v-if="rounds.length > pageSize" class="flex justify-center">
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :total="rounds.length"
        layout="prev, pager, next"
        small
        @current-change="(p: number) => page = p"
      />
    </div>
  </div>
</template>
