<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useAuthStore } from '@/stores/auth';
import apiClient from '@/lib/axios';
import type { ComplianceSummary, QuickCheckResult, ScanResult } from '@/types/api';
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
    { label: '未闭环发现', value: String(s.openFindings), color: s.openFindings > 0 ? 'text-red-600' : 'text-green-600' },
  ];
});

const severityBars = computed(() => {
  if (!summary.value) return [];
  const m = summary.value.findingsBySeverity;
  const max = Math.max(1, ...Object.values(m));
  return Object.entries(m).map(([k, v]) => ({ name: k, count: v, pct: Math.round((v / max) * 100) }));
});

// ── 一键快检 ──
const quickQuery = ref('');
const quickResult = ref<QuickCheckResult | null>(null);
const quickLoading = ref(false);
const quickError = ref('');

async function runQuickCheck() {
  if (!quickQuery.value.trim()) return;
  quickError.value = ''; quickResult.value = null;
  quickLoading.value = true;
  try {
    const { data } = await apiClient.post<QuickCheckResult>('/api/Inspection/quick-check', { query: quickQuery.value.trim() });
    quickResult.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    quickError.value = ae.response?.data?.error || '快检失败';
  } finally { quickLoading.value = false; }
}

// ── 自动扫描 ──
const scanResult = ref<ScanResult | null>(null);
const scanLoading = ref(false);
const scanError = ref('');

async function runAutoScan() {
  scanError.value = ''; scanResult.value = null;
  scanLoading.value = true;
  try {
    const { data } = await apiClient.post<ScanResult>('/api/Inspection/scan', null, { timeout: 600_000 });
    scanResult.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    scanError.value = ae.response?.data?.error || '扫描启动失败';
  } finally { scanLoading.value = false; }
}
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

      <!-- 快速操作区 -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <!-- 一键快检 -->
        <div class="bg-white border border-slate-200 rounded p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-3">🔍 一键快检</h3>
          <div class="flex gap-2 mb-3">
            <input
              v-model="quickQuery"
              @keyup.enter="runQuickCheck"
              :disabled="quickLoading"
              class="flex-1 px-3 py-1.5 text-xs border border-slate-300 rounded focus:outline-none focus:border-blue-400"
              placeholder="输入合规问题，如：硝酸储存条件是否合规"
            />
            <button
              @click="runQuickCheck"
              :disabled="quickLoading || !quickQuery.trim()"
              class="px-4 py-1.5 text-xs font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >{{ quickLoading ? '检测中…' : '检测' }}</button>
          </div>
          <div v-if="quickError" class="text-xs text-red-600">{{ quickError }}</div>
          <div v-if="quickResult" class="p-3 rounded border text-xs space-y-1" :class="quickResult.isCompliant ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'">
            <p class="font-medium" :class="quickResult.isCompliant ? 'text-green-800' : 'text-red-800'">
              {{ quickResult.isCompliant ? '✅ 合规' : '❌ 不合规' }}
              <span class="text-slate-400 font-normal ml-2">{{ quickResult.elapsedMs }}ms</span>
            </p>
            <p class="text-slate-600">{{ quickResult.conclusion }}</p>
            <p v-if="quickResult.regulationRef" class="text-slate-400">法规: {{ quickResult.regulationRef }}</p>
            <p v-if="quickResult.warnings.length > 0" class="text-amber-600">⚠ {{ quickResult.warnings.join('; ') }}</p>
          </div>
        </div>

        <!-- 自动扫描 -->
        <div class="bg-white border border-slate-200 rounded p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-3">🔄 自动扫描</h3>
          <p class="text-xs text-slate-500 mb-3">对全部资产执行合规规则扫描，生成最新发现报告</p>
          <button
            @click="runAutoScan"
            :disabled="scanLoading"
            class="px-5 py-2 text-sm font-medium text-white bg-emerald-600 rounded hover:bg-emerald-700 disabled:opacity-50 transition-colors"
          >{{ scanLoading ? '扫描中…' : '触发全库扫描' }}</button>
          <div v-if="scanError" class="text-xs text-red-600 mt-2">{{ scanError }}</div>
          <div v-if="scanResult" class="mt-3 p-3 bg-emerald-50 border border-emerald-200 rounded text-xs space-y-1">
            <p class="font-medium text-emerald-800">✅ 扫描完成</p>
            <p class="text-slate-600">资产 {{ scanResult.checkedAssets }}/{{ scanResult.totalAssets }} · 发现 {{ scanResult.totalFindings }} 条 · 新增 {{ scanResult.newFindings }} 条</p>
            <p class="text-slate-400">{{ new Date(scanResult.scannedAt).toLocaleString('zh-CN') }}</p>
          </div>
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
