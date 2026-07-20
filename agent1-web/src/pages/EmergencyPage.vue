<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { EmergencyResult } from '@/types/api';
import { useLoadingBar } from '@/lib/useLoadingBar';

const scenario = ref<'leak' | 'fire' | 'explosion' | 'poisoning'>('leak');
const substance = ref('');
const location = ref('');
const result = ref<EmergencyResult | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

async function respond() {
  if (!substance.value.trim()) return;
  error.value = '';
  result.value = null;
  loading.value = true;
  start('正在生成应急响应方案…');
  try {
    const { data } = await apiClient.post<EmergencyResult>('/api/Emergency/response', {
      scenario: scenario.value,
      substance: substance.value.trim(),
      location: location.value.trim() || undefined,
    });
    result.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '生成失败';
  } finally { loading.value = false; stop(); }
}

const scenarioLabels: Record<string, string> = {
  leak: '泄漏', fire: '火灾', explosion: '爆炸', poisoning: '中毒',
};

const examples: Record<string, { substance: string; location: string }> = {
  leak: { substance: '苯', location: '甲类仓库A区' },
  fire: { substance: '甲醇', location: '储罐区2号罐' },
  explosion: { substance: '氢气', location: '压力容器车间' },
  poisoning: { substance: '氯气', location: '化学品装卸区' },
};
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">应急响应</h1>
    <p class="text-xs text-slate-500 -mt-4">根据事故类型和化学品生成应急处置方案</p>

    <div class="bg-white border border-slate-200 rounded p-4">
      <!-- 事故类型 -->
      <div class="mb-4">
        <label class="text-xs text-slate-500 block mb-2">事故类型</label>
        <div class="flex gap-2 flex-wrap">
          <button
            v-for="s in (['leak','fire','explosion','poisoning'] as const)" :key="s"
            @click="scenario = s; substance = examples[s].substance; location = examples[s].location"
            class="text-sm px-4 py-2 rounded border transition-colors"
            :class="scenario === s ? 'border-red-300 bg-red-50 text-red-700 font-medium' : 'border-slate-200 text-slate-500 hover:bg-slate-50'"
          >{{ { leak: '💧', fire: '🔥', explosion: '💥', poisoning: '☠️' }[s] }} {{ scenarioLabels[s] }}</button>
        </div>
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3 mb-3">
        <div>
          <label class="text-xs text-slate-500 block mb-1">涉及化学品</label>
          <input v-model="substance" :disabled="loading"
            class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
            placeholder="如：苯、甲醇、氯气…" />
        </div>
        <div>
          <label class="text-xs text-slate-500 block mb-1">事发位置（可选）</label>
          <input v-model="location" :disabled="loading"
            class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
            placeholder="如：甲类仓库A区" />
        </div>
      </div>

      <button
        @click="respond"
        :disabled="loading || !substance.trim()"
        class="px-5 py-2 text-sm font-medium text-white bg-red-600 rounded hover:bg-red-700 disabled:opacity-50 transition-colors"
      >{{ loading ? '生成方案中…' : '🚨 生成应急方案' }}</button>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">
        应急响应方案：{{ result.scenario }}
      </h3>
      <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100">
        {{ result.output }}
      </div>
    </div>
  </div>
</template>
