<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { KnowledgeGraphResult } from '@/types/api';
import { useLoadingBar } from '@/lib/useLoadingBar';

const query = ref('');
const result = ref<KnowledgeGraphResult | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

async function search() {
  if (!query.value.trim()) return;
  error.value = '';
  result.value = null;
  loading.value = true;
  start('正在查询知识图谱…');
  try {
    const { data } = await apiClient.post<KnowledgeGraphResult>('/api/KnowledgeGraph/query', {
      query: query.value.trim(),
    });
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '查询失败';
  } finally {
    loading.value = false;
    stop();
  }
}

const presets = ['苯的关联法规和事故案例', '甲类仓库涉及哪些化学品和处罚记录', 'GB 15603 关联哪些危化品和园区'];
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">知识图谱</h1>
    <p class="text-xs text-slate-500 -mt-4">查询危化品—法规—事故—园区之间的关联关系</p>

    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex gap-2 mb-3">
        <input
          v-model="query"
          @keyup.enter="search"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
          placeholder="输入要查询的化学品、法规或事故类型…"
        />
        <button
          @click="search"
          :disabled="loading || !query.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-indigo-600 rounded hover:bg-indigo-700 disabled:opacity-50 transition-colors"
        >
          {{ loading ? '查询中…' : '🔗 查询' }}
        </button>
      </div>
      <div class="flex gap-2 flex-wrap">
        <button
          v-for="p in presets"
          :key="p"
          @click="
            query = p;
            search();
          "
          :disabled="loading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50"
        >
          {{ p }}
        </button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">图谱查询结果</h3>
      <div
        class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100"
      >
        {{ result.output }}
      </div>
    </div>
  </div>
</template>
