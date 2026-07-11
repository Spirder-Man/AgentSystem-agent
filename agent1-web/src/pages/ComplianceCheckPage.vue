<script setup lang="ts">
import { ref, computed } from 'vue';
import apiClient from '@/lib/axios';
import type { ComplianceResponse } from '@/types/api';
import EmptyState from '@/components/common/EmptyState.vue';
import { useLoadingBar } from '@/lib/useLoadingBar';

const query = ref('');
const result = ref<ComplianceResponse | null>(null);
const loading = ref(false);
const error = ref('');
const fromCache = ref(false);
const { start, stop } = useLoadingBar();

const activeTab = ref<'analysis' | 'regulations' | 'warnings'>('analysis');

const hasRegulations = computed(() =>
  (result.value?.verifiedRegulations.length ?? 0) > 0 || (result.value?.hallucinatedRegulations.length ?? 0) > 0
);
const hasWarnings = computed(() => (result.value?.warnings.length ?? 0) > 0);

async function submit() {
  if (!query.value.trim()) return;
  error.value = '';
  result.value = null;
  fromCache.value = false;
  loading.value = true;
  start('AI 正在分析合规风险…');
  try {
    const startTime = performance.now();
    const { data } = await apiClient.post<ComplianceResponse>('/api/Compliance/check', { query: query.value.trim() });
    const elapsed = performance.now() - startTime;
    // 超快速响应 = 缓存命中 (< 500ms)
    fromCache.value = elapsed < 500 && data.toolsUsed.length === 0;
    result.value = data;
    activeTab.value = 'analysis';
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string; retryAfter?: number } } };
    const msg = ae.response?.data?.error || '请求失败';
    const retry = ae.response?.data?.retryAfter;
    error.value = retry ? `${msg}（${retry}秒后可重试）` : msg;
  } finally { loading.value = false; stop(); }
}

const presets = [
  { q: '苯和丙酮能放在同一个仓库吗', label: '苯+丙酮共存' },
  { q: '苯属于什么危险类别', label: '苯危险分类' },
  { q: '甲类仓库与明火点的安全距离', label: '安全距离' },
  { q: '硝酸应该如何储存', label: '硝酸储存' },
];
function usePreset(q: string) { query.value = q; submit(); }
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">合规检查</h1>

    <!-- 查询输入区 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex gap-2">
        <input
          v-model="query"
          @keyup.enter="submit"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-200"
          placeholder="输入化工合规问题，如：苯和丙酮能放在同一个仓库吗"
        />
        <button
          @click="submit"
          :disabled="loading || !query.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >{{ loading ? 'AI 分析中…' : '提交审核' }}</button>
      </div>
      <div class="flex gap-2 mt-3 flex-wrap">
        <button
          v-for="p in presets" :key="p.q"
          @click="usePreset(p.q)"
          :disabled="loading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50 hover:border-slate-300 transition-colors"
        >{{ p.label }}</button>
      </div>
    </div>

    <!-- 错误 -->
    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4">
      <p class="text-sm font-medium text-red-800 mb-1">❌ 审核失败</p>
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <!-- 空状态 -->
    <EmptyState v-if="!result && !loading && !error" icon="search" title="输入查询开始AI合规分析" description="支持危化品危险分类、储存兼容性、安全距离等合规审核" />

    <!-- 结果区 -->
    <div v-if="result" class="space-y-4">
      <!-- 结果头部 -->
      <div class="bg-white border border-slate-200 rounded p-4">
        <div class="flex items-center justify-between mb-2">
          <div class="flex items-center gap-2">
            <h2 class="text-sm font-semibold text-slate-700">审核结论</h2>
            <span v-if="fromCache" class="text-xs px-1.5 py-0.5 rounded bg-green-50 text-green-600 border border-green-200">⚡ 缓存命中</span>
          </div>
          <span class="text-xs text-slate-400">工具调用: {{ result.toolsUsed.length > 0 ? result.toolsUsed.join(' → ') : '直接推理' }}</span>
        </div>
        <p class="text-xs text-slate-500 mb-3">查询: <span class="text-slate-700">"{{ result.query }}"</span></p>

        <!-- Tab 切换 -->
        <div class="flex gap-1 border-b border-slate-200 mb-3">
          <button
            @click="activeTab = 'analysis'"
            :class="activeTab === 'analysis' ? 'border-b-2 border-blue-500 text-blue-700' : 'text-slate-500 hover:text-slate-700'"
            class="text-xs px-3 py-2 font-medium transition-colors"
          >📋 分析结果</button>
          <button
            v-if="hasRegulations"
            @click="activeTab = 'regulations'"
            :class="activeTab === 'regulations' ? 'border-b-2 border-blue-500 text-blue-700' : 'text-slate-500 hover:text-slate-700'"
            class="text-xs px-3 py-2 font-medium transition-colors"
          >📜 法规引用 ({{ result.verifiedRegulations.length + result.hallucinatedRegulations.length }})</button>
          <button
            v-if="hasWarnings"
            @click="activeTab = 'warnings'"
            :class="activeTab === 'warnings' ? 'border-b-2 border-orange-500 text-orange-700' : 'text-slate-500 hover:text-slate-700'"
            class="text-xs px-3 py-2 font-medium transition-colors"
          >⚠️ 安全警告 ({{ result.warnings.length }})</button>
        </div>

        <!-- Tab: 分析结果 -->
        <div v-if="activeTab === 'analysis'" class="text-sm whitespace-pre-wrap leading-relaxed text-slate-800 p-2 bg-slate-50 rounded border border-slate-100 min-h-20">
          {{ result.response || '（模型未返回有效响应）' }}
        </div>

        <!-- Tab: 法规引用 -->
        <div v-if="activeTab === 'regulations' && hasRegulations" class="space-y-3">
          <div v-if="result.verifiedRegulations.length > 0" class="bg-green-50 border border-green-200 rounded p-3">
            <h3 class="text-xs font-semibold text-green-800 mb-2">✅ 已验证法规引用 ({{ result.verifiedRegulations.length }} 条)</h3>
            <ul class="space-y-1">
              <li v-for="(r, i) in result.verifiedRegulations" :key="i" class="text-sm text-green-700 flex items-start gap-2">
                <span class="text-green-400 mt-0.5 shrink-0">✓</span>
                <span>{{ r }}</span>
              </li>
            </ul>
          </div>
          <div v-if="result.hallucinatedRegulations.length > 0" class="bg-red-50 border border-red-200 rounded p-3">
            <h3 class="text-xs font-semibold text-red-800 mb-2">🚫 检测到不存在的法规 (幻觉) — {{ result.hallucinatedRegulations.length }} 条</h3>
            <ul class="space-y-1">
              <li v-for="(r, i) in result.hallucinatedRegulations" :key="i" class="text-sm text-red-700 flex items-start gap-2">
                <span class="text-red-400 mt-0.5 shrink-0">✗</span>
                <span>{{ r }}</span>
              </li>
            </ul>
          </div>
        </div>

        <!-- Tab: 安全警告 -->
        <div v-if="activeTab === 'warnings' && hasWarnings" class="bg-amber-50 border border-amber-200 rounded p-3">
          <h3 class="text-xs font-semibold text-amber-800 mb-2">⚠️ 安全与合规警告</h3>
          <ul class="space-y-2">
            <li v-for="(w, i) in result.warnings" :key="i" class="text-sm text-amber-700 flex items-start gap-2">
              <span class="text-amber-400 mt-0.5 shrink-0 text-base">⚠</span>
              <span>{{ w }}</span>
            </li>
          </ul>
        </div>
      </div>
    </div>
  </div>
</template>
