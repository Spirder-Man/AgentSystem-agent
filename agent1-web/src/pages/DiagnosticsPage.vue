<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { DiagnosticsRunResponse, DiagnosticsTestResult } from '@/types/api';
import { useLoadingBar } from '@/lib/useLoadingBar';

const results = ref<DiagnosticsTestResult[]>([]);
const summary = ref<{ model: string; total: number; pass: number; passRate: string; elapsedMs: number } | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

async function runAll() {
  error.value = '';
  results.value = [];
  summary.value = null;
  loading.value = true;
  start('正在执行诊断…');
  try {
    const { data } = await apiClient.post<DiagnosticsRunResponse>('/api/diagnostics/tool-calling');
    summary.value = { model: data.model, total: data.total, pass: data.pass, passRate: data.passRate, elapsedMs: data.elapsedMs };
    results.value = data.results;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '诊断执行失败';
  } finally { loading.value = false; stop(); }
}

function clearResults() { results.value = []; summary.value = null; }
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">工具调用诊断</h1>
    <p class="text-xs text-slate-500 -mt-4">测试 LLM 能否正确选择合适的工具（Function Calling 诊断）</p>

    <!-- 执行按钮 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <p class="text-xs text-slate-500 mb-3">运行 5 条预置用例，验证 LLM Function Calling 是否正确触发目标工具</p>
      <div class="flex items-center gap-3">
        <button
          @click="runAll"
          :disabled="loading"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >{{ loading ? '执行中…' : '🚀 运行全部诊断' }}</button>
        <button v-if="results.length" @click="clearResults" class="text-xs text-slate-400 hover:text-red-500">清除结果</button>
      </div>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>

    <!-- 汇总 -->
    <div v-if="summary" class="bg-white border border-slate-200 rounded p-4">
      <div class="flex items-center gap-4 text-sm">
        <span class="text-slate-500">模型: <code class="text-blue-600">{{ summary.model }}</code></span>
        <span class="text-slate-500">通过率: <span class="font-bold" :class="summary.pass === summary.total ? 'text-green-600' : 'text-orange-600'">{{ summary.passRate }}</span></span>
        <span class="text-slate-500">总耗时: {{ summary.elapsedMs }}ms</span>
      </div>
    </div>

    <!-- 结果列表 -->
    <div v-if="results.length" class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">用例详情</h3>
      <div class="space-y-3">
        <div v-for="r in results" :key="r.index"
          class="p-3 rounded border text-xs"
          :class="r.triggered ? 'border-green-200 bg-green-50' : 'border-red-200 bg-red-50'"
        >
          <div class="flex items-center gap-2 mb-1">
            <span :class="r.triggered ? 'text-green-600' : 'text-red-600'">{{ r.triggered ? '✅' : '❌' }}</span>
            <span class="font-medium text-slate-800">{{ r.description }}</span>
            <span class="text-slate-400 ml-auto">{{ r.elapsedMs }}ms</span>
          </div>
          <div class="grid grid-cols-2 gap-2 text-[11px] mb-1">
            <div><span class="text-slate-400">期望工具:</span> <code class="text-blue-600">{{ r.expectedTools || '无' }}</code></div>
            <div><span class="text-slate-400">实际调用:</span> <code :class="r.triggered ? 'text-green-700' : 'text-red-700'">{{ r.toolCalls.join(', ') || '无' }}</code></div>
          </div>
          <div v-if="r.error" class="text-red-500">{{ r.error }}</div>
          <div class="text-slate-400 font-mono">{{ r.query }}</div>
        </div>
      </div>
    </div>
  </div>
</template>
