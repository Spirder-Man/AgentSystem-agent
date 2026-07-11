<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { StorageCompatibilityResponse } from '@/types/api';
import EmptyState from '@/components/common/EmptyState.vue';
import { useLoadingBar } from '@/lib/useLoadingBar';

const substanceA = ref('');
const substanceB = ref('');
const result = ref<StorageCompatibilityResponse | null>(null);
const loading = ref(false);
const error = ref('');
const fromCache = ref(false);
const { start, stop } = useLoadingBar();

async function check() {
  if (!substanceA.value.trim() || !substanceB.value.trim()) return;
  error.value = '';
  result.value = null;
  fromCache.value = false;
  loading.value = true;
  start('正在分析储存兼容性…');
  try {
    const startTime = performance.now();
    const { data } = await apiClient.post<StorageCompatibilityResponse>('/api/Compliance/storage/compatibility', {
      substanceA: substanceA.value.trim(),
      substanceB: substanceB.value.trim(),
    });
    fromCache.value = (performance.now() - startTime) < 500 && data.toolsUsed.length === 0;
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '检查失败';
  } finally { loading.value = false; stop(); }
}

const presets = [
  ['苯', '丙酮'],
  ['硝酸', '硫酸'],
  ['甲醇', '盐酸'],
  ['氢氧化钠', '硫酸'],
  ['甲苯', '苯'],
];
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">储存兼容性检查</h1>
    <p class="text-xs text-slate-500 -mt-4">检查两种化学品是否可以同库储存，避免反应风险</p>

    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3 mb-3">
        <div>
          <label class="text-xs text-slate-500 block mb-1">化学品 A</label>
          <input v-model="substanceA" @keyup.enter="check" :disabled="loading"
            class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
            placeholder="如：苯" />
        </div>
        <div>
          <label class="text-xs text-slate-500 block mb-1">化学品 B</label>
          <input v-model="substanceB" @keyup.enter="check" :disabled="loading"
            class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
            placeholder="如：丙酮" />
        </div>
      </div>
      <button @click="check" :disabled="loading || !substanceA.trim() || !substanceB.trim()"
        class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
      >{{ loading ? 'AI 分析中…' : '检查兼容性' }}</button>

      <div class="flex gap-2 mt-3 flex-wrap">
        <button v-for="(pair, i) in presets" :key="i" @click="substanceA = pair[0]; substanceB = pair[1]; check()"
          :disabled="loading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50 transition-colors"
        >{{ pair[0] }} + {{ pair[1] }}</button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>
    <EmptyState v-if="!result && !loading && !error" icon="search" title="输入两种化学品名称开始兼容性检查" />

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5 space-y-4">
      <div class="flex items-center justify-between">
        <div>
          <h2 class="text-lg font-bold text-slate-800">{{ result.substanceA }} vs {{ result.substanceB }}</h2>
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
