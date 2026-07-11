<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { ComplianceResponse } from '@/types/api';
import EmptyState from '@/components/common/EmptyState.vue';
import { useLoadingBar } from '@/lib/useLoadingBar';

const query = ref('');
const result = ref<ComplianceResponse | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

async function submit() {
  if (!query.value.trim()) return;
  error.value = '';
  loading.value = true;
  start('AI 正在分析合规风险…');
  try {
    const { data } = await apiClient.post<ComplianceResponse>('/api/Compliance/check', { query: query.value.trim() });
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '请求失败';
  } finally { loading.value = false; stop(); }
}

const presets = ['苯和丙酮能放在同一个仓库吗', '苯属于什么危险类别', '甲类仓库与明火点的安全距离'];
function usePreset(q: string) { query.value = q; submit(); }
</script>

<template>
  <div class="space-y-6 max-w-3xl">
    <h1 class="text-xl font-bold text-slate-900">合规检查</h1>
    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex gap-2">
        <input v-model="query" @keyup.enter="submit" :disabled="loading" class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="例如：苯和丙酮能放一起吗" />
        <button @click="submit" :disabled="loading || !query.trim()" class="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50">{{ loading ? '分析中…' : '提交' }}</button>
      </div>
      <div class="flex gap-2 mt-3 flex-wrap">
        <button v-for="q in presets" :key="q" @click="usePreset(q)" :disabled="loading" class="text-xs px-2 py-1 border border-slate-200 rounded text-slate-500 hover:bg-slate-50">{{ q }}</button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>
    <EmptyState v-if="!result && !loading && !error" icon="search" title="输入查询开始合规分析" />

    <div v-if="result" class="bg-white border border-slate-200 rounded p-4 space-y-3">
      <div class="flex gap-2 text-xs text-slate-500">
        <span class="font-mono">工具: {{ result.toolsUsed.join(',') || '无' }}</span>
        <span>·</span>
        <span>法规: {{ result.verifiedRegulations.length }} 条</span>
      </div>
      <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-800 font-mono">{{ result.response || '无响应' }}</div>
      <div v-if="result.warnings.length" class="border-t border-slate-100 pt-3">
        <p class="text-xs font-semibold text-amber-700 mb-1">警告</p>
        <ul class="text-xs text-amber-600 space-y-1"><li v-for="(w,i) in result.warnings" :key="i">⚠ {{ w }}</li></ul>
      </div>
    </div>
  </div>
</template>
