<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { RegulatoryAuditResult } from '@/types/api';
import { useLoadingBar } from '@/lib/useLoadingBar';

const query = ref('');
const result = ref<RegulatoryAuditResult | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

async function audit() {
  if (!query.value.trim()) return;
  error.value = '';
  result.value = null;
  loading.value = true;
  start('正在进行法规审计…');
  try {
    const { data } = await apiClient.post<RegulatoryAuditResult>('/api/regulatory/audit', {
      query: query.value.trim(),
    });
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '审计失败';
  } finally { loading.value = false; stop(); }
}

const presets = [
  '审查甲类仓库的消防安全合规性',
  '检查危化品储存区的防泄漏措施',
  '评估装卸区操作人员的防护装备合规性',
];
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">法规审计</h1>
    <p class="text-xs text-slate-500 -mt-4">对园区设施、流程进行法规合规性审计，输出整改建议</p>

    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex gap-2 mb-3">
        <input
          v-model="query"
          @keyup.enter="audit"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
          placeholder="描述需要审计的设施、流程或操作…"
        />
        <button
          @click="audit"
          :disabled="loading || !query.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >{{ loading ? '审计中…' : '🔍 执行审计' }}</button>
      </div>
      <div class="flex gap-2 flex-wrap">
        <button v-for="p in presets" :key="p"
          @click="query = p; audit()"
          :disabled="loading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50"
        >{{ p }}</button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">审计结果</h3>
      <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100">
        {{ result.output }}
      </div>
    </div>
  </div>
</template>
