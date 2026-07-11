<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { HazardQueryResponse } from '@/types/api';
import EmptyState from '@/components/common/EmptyState.vue';
import { useLoadingBar } from '@/lib/useLoadingBar';

const substance = ref('');
const result = ref<HazardQueryResponse | null>(null);
const loading = ref(false);
const error = ref('');
const fromCache = ref(false);
const { start, stop } = useLoadingBar();

async function query() {
  if (!substance.value.trim()) return;
  error.value = '';
  result.value = null;
  fromCache.value = false;
  loading.value = true;
  start('正在查询危险化学信息…');
  try {
    const startTime = performance.now();
    const { data } = await apiClient.post<HazardQueryResponse>('/api/Compliance/hazard/query', {
      substanceName: substance.value.trim(),
    });
    fromCache.value = (performance.now() - startTime) < 500 && data.toolsUsed.length === 0;
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '查询失败';
  } finally { loading.value = false; stop(); }
}

const presets = [
  '苯', '丙酮', '甲醇', '硝酸', '硫酸', '甲苯', '盐酸', '氢氧化钠', '氨水', '甲醛',
];
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">危化品查询</h1>
    <p class="text-xs text-slate-500 -mt-4">查询化学品的危险类别、适用国标及安全处置要求</p>

    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex gap-2">
        <input
          v-model="substance"
          @keyup.enter="query"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
          placeholder="输入化学品名称，如：苯、硫酸、氢氧化钠"
        />
        <button @click="query" :disabled="loading || !substance.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >{{ loading ? '查询中…' : '查询' }}</button>
      </div>
      <div class="flex gap-2 mt-3 flex-wrap">
        <button v-for="p in presets" :key="p" @click="substance = p; query()" :disabled="loading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50 transition-colors"
        >{{ p }}</button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>
    <EmptyState v-if="!result && !loading && !error" icon="search" title="输入化学品名称查询危险信息" />

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5 space-y-4">
      <div class="flex items-center justify-between">
        <div>
          <h2 class="text-lg font-bold text-slate-800">{{ result.substanceName }}</h2>
          <span v-if="fromCache" class="text-xs px-1.5 py-0.5 rounded bg-green-50 text-green-600 border border-green-200">⚡ 缓存命中</span>
        </div>
        <span class="text-xs text-slate-400">工具: {{ result.toolsUsed.join(' → ') || '直接推理' }}</span>
      </div>
      <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100">
        {{ result.response || '（无响应）' }}
      </div>
    </div>
  </div>
</template>
