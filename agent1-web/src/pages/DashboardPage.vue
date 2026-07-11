<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useAuthStore } from '@/stores/auth';
import apiClient from '@/lib/axios';
import type { ComplianceSummary } from '@/types/api';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const auth = useAuthStore();
const summary = ref<ComplianceSummary | null>(null);
const loading = ref(true);
const error = ref('');

const complianceRatePercent = computed(() => summary.value ? Math.round(summary.value.complianceRate * 100) : 0);

async function fetchSummary() {
  loading.value = true; error.value = '';
  try { const { data } = await apiClient.get<ComplianceSummary>('/api/Compliance/summary'); summary.value = data; }
  catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

onMounted(fetchSummary);

const statCards = computed(() => {
  if (!summary.value) return [];
  const s = summary.value;
  return [
    { label: '合规率', value: `${complianceRatePercent.value}%`, color: s.complianceRate >= 0.8 ? 'text-green-600' : s.complianceRate >= 0.6 ? 'text-amber-600' : 'text-red-600' },
    { label: '资产总量', value: String(s.totalAssets), color: 'text-slate-900' },
    { label: '已检查', value: `${s.checkedAssets}/${s.totalAssets}`, color: 'text-blue-600' },
    { label: '待处理工单', value: String(s.openFindings), color: s.openFindings > 0 ? 'text-red-600' : 'text-green-600' },
  ];
});

const severityBars = computed(() => {
  if (!summary.value) return [];
  const m = summary.value.findingsBySeverity;
  const max = Math.max(1, ...Object.values(m));
  return Object.entries(m).map(([k, v]) => ({ name: k, count: v, pct: Math.round((v / max) * 100) }));
});
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">合规仪表盘</h1>
      <span class="text-xs text-slate-400">当前: {{ auth.username }} ({{ auth.role }})</span>
    </div>

    <SkeletonCard v-if="loading" :count="4" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchSummary" />

    <template v-else-if="summary">
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div v-for="c in statCards" :key="c.label" class="bg-white border border-slate-200 rounded p-4">
          <p class="text-xs text-slate-500 mb-1">{{ c.label }}</p>
          <p class="text-2xl font-bold" :class="c.color">{{ c.value }}</p>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div class="bg-white border border-slate-200 rounded p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-4">问题按严重程度分布</h3>
          <div class="space-y-2">
            <div v-for="b in severityBars" :key="b.name" class="flex items-center gap-3">
              <span class="text-xs text-slate-500 w-16">{{ b.name }}</span>
              <div class="flex-1 h-5 bg-slate-100 rounded">
                <div class="h-full rounded" :class="{'bg-red-500':b.name==='Critical','bg-orange-500':b.name==='High','bg-amber-500':b.name==='Medium','bg-blue-400':b.name==='Low','bg-slate-300':b.name==='Info'}" :style="{width:`${b.pct}%`}" />
              </div>
              <span class="text-xs font-mono text-slate-600 w-6 text-right">{{ b.count }}</span>
            </div>
          </div>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-4">风险分布</h3>
          <div class="flex items-center justify-center h-32 gap-6">
            <div v-for="r in [{k:'严重',v:summary.riskDistribution.critical,c:'red'},{k:'高',v:summary.riskDistribution.high,c:'orange'},{k:'未知',v:summary.riskDistribution.unknown,c:'amber'},{k:'低',v:summary.riskDistribution.low,c:'green'}]" :key="r.k" class="text-center">
              <div :class="`w-12 h-12 rounded-full bg-${r.c}-100 flex items-center justify-center mx-auto mb-1`">
                <span :class="`text-sm font-bold text-${r.c}-600`">{{ r.v }}</span>
              </div>
              <span class="text-xs text-slate-500">{{ r.k }}</span>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
