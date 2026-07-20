<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import apiClient from '@/lib/axios';
import type { InspectionReport } from '@/types/api';
import MarkdownIt from 'markdown-it';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';
import { ElMessage } from 'element-plus';

const route = useRoute();
const roundId = route.params.roundId as string;
const report = ref<InspectionReport | null>(null);
const loading = ref(true);
const error = ref('');
const exporting = ref(false);

const md = new MarkdownIt({ html: false, breaks: true });

async function fetchReport() {
  loading.value = true; error.value = '';
  try {
    const { data } = await apiClient.get<InspectionReport>(`/api/Inspection/reports/${roundId}`);
    report.value = data;
  } catch { error.value = '报告加载失败，可能该轮次尚未生成报告'; }
  finally { loading.value = false; }
}

async function exportJson() {
  exporting.value = true;
  try {
    const { data } = await apiClient.get(`/api/Inspection/reports/${roundId}/export`, { params: { format: 'json' } });
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `inspection-report-${roundId}.json`;
    a.click();
    URL.revokeObjectURL(url);
    ElMessage.success('报告已导出');
  } catch { ElMessage.error('导出失败'); }
  finally { exporting.value = false; }
}

function rateBadge(r: number) {
  if (r >= 0.9) return { cls: 'text-green-700 bg-green-50 border-green-200', label: '优秀' };
  if (r >= 0.7) return { cls: 'text-amber-700 bg-amber-50 border-amber-200', label: '一般' };
  return { cls: 'text-red-700 bg-red-50 border-red-200', label: '需整改' };
}

onMounted(fetchReport);
</script>

<template>
  <div class="space-y-6">
    <SkeletonCard v-if="loading" :count="3" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchReport" />

    <template v-else-if="report">
      <!-- 头部信息 -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-xl font-bold text-slate-900">巡检报告</h1>
          <p class="text-xs text-slate-500 mt-1">
            {{ report.plan.name }} · {{ report.plan.area }} · {{ new Date(report.generatedAt).toLocaleDateString('zh-CN') }}
          </p>
        </div>
        <button
          @click="exportJson"
          :disabled="exporting"
          class="text-xs px-3 py-1.5 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 disabled:opacity-50 transition-colors"
        >{{ exporting ? '导出中…' : '📥 导出 JSON' }}</button>
      </div>

      <!-- 合规率卡片 -->
      <div class="grid grid-cols-1 sm:grid-cols-4 gap-4">
        <div class="bg-white border border-slate-200 rounded p-4 text-center">
          <p class="text-xs text-slate-500 mb-1">合规率</p>
          <p class="text-3xl font-bold" :class="report.complianceRate >= 0.9 ? 'text-green-600' : report.complianceRate >= 0.7 ? 'text-amber-600' : 'text-red-600'">
            {{ Math.round(report.complianceRate * 100) }}%
          </p>
          <span :class="rateBadge(report.complianceRate).cls" class="inline-block text-xs px-2 py-0.5 rounded border mt-1">
            {{ rateBadge(report.complianceRate).label }}
          </span>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4 text-center">
          <p class="text-xs text-slate-500 mb-1">报告摘要</p>
          <p class="text-sm text-slate-700 line-clamp-3">{{ report.summary }}</p>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4 text-center">
          <p class="text-xs text-slate-500 mb-1">关键发现</p>
          <p class="text-2xl font-bold" :class="report.criticalFindings.length > 0 ? 'text-red-600' : 'text-green-600'">
            {{ report.criticalFindings.length }}
          </p>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4 text-center">
          <p class="text-xs text-slate-500 mb-1">审计哈希</p>
          <p class="text-xs font-mono text-slate-400 truncate" :title="report.auditHash">{{ report.auditHash.slice(0, 16) }}…</p>
          <p class="text-xs text-green-500 mt-1">🔒 SHA256</p>
        </div>
      </div>

      <!-- 关键发现 -->
      <div v-if="report.criticalFindings.length > 0" class="bg-red-50 border border-red-200 rounded p-4">
        <h2 class="text-sm font-semibold text-red-800 mb-3">⚠️ 关键发现 ({{ report.criticalFindings.length }})</h2>
        <ul class="space-y-2">
          <li v-for="(f, i) in report.criticalFindings" :key="i" class="text-sm text-red-700 flex items-start gap-2">
            <span class="text-red-400 mt-0.5 shrink-0">●</span>
            <span>{{ f }}</span>
          </li>
        </ul>
      </div>

      <!-- Markdown 报告全文 -->
      <div class="bg-white border border-slate-200 rounded p-6">
        <h2 class="text-sm font-semibold text-slate-700 mb-4">📄 完整报告</h2>
        <div
          class="prose prose-sm max-w-none prose-headings:text-slate-800 prose-p:text-slate-600 prose-strong:text-slate-800 prose-code:text-blue-600 prose-code:bg-blue-50 prose-code:px-1 prose-code:rounded"
          v-html="md.render(report.markdown)"
        />
      </div>

      <!-- 元信息 -->
      <div class="bg-slate-50 border border-slate-200 rounded p-4 flex gap-6 text-xs text-slate-500">
        <span>报告ID: <code class="text-slate-600">{{ report.reportId }}</code></span>
        <span>轮次ID: <code class="text-slate-600">{{ report.roundId }}</code></span>
        <span>生成人: {{ report.generatedBy }}</span>
        <span>生成时间: {{ new Date(report.generatedAt).toLocaleString('zh-CN') }}</span>
      </div>
    </template>
  </div>
</template>
