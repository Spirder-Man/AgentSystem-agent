<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { SearchModeResponse, RagTestResponse, IncrementalLoadResponse } from '@/types/api';
import { ElMessage } from 'element-plus';
import { useLoadingBar } from '@/lib/useLoadingBar';

const searchMode = ref('');
const modeLoading = ref(false);

const ragQuery = ref('');
const ragResult = ref<RagTestResponse | null>(null);
const ragLoading = ref(false);
const ragError = ref('');

const kbLoading = ref(false);

const { start, stop } = useLoadingBar();

async function fetchSearchMode() {
  try {
    const { data } = await apiClient.get<SearchModeResponse>('/api/knowledgebase/search-mode');
    searchMode.value = data.mode;
  } catch { /* ignore */ }
}

async function switchMode(mode: 'Bm25' | 'Vector' | 'Hybrid') {
  modeLoading.value = true;
  try {
    await apiClient.put('/api/knowledgebase/search-mode', { mode });
    searchMode.value = mode;
    ElMessage.success(`搜索模式已切换为 ${mode}`);
  } catch { ElMessage.error('切换失败'); }
  finally { modeLoading.value = false; }
}

async function runRagTest() {
  if (!ragQuery.value.trim()) return;
  ragError.value = '';
  ragResult.value = null;
  ragLoading.value = true;
  start('正在执行 RAG 检索…');
  try {
    const { data } = await apiClient.post<RagTestResponse>('/api/knowledgebase/rag-test', {
      query: ragQuery.value.trim(),
    });
    ragResult.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ragError.value = ae.response?.data?.error || 'RAG 测试失败';
  } finally { ragLoading.value = false; stop(); }
}

async function incrementalLoad() {
  kbLoading.value = true;
  try {
    const { data } = await apiClient.post<IncrementalLoadResponse>('/api/knowledgebase/incremental-load');
    ElMessage.success(data.message);
  } catch { ElMessage.error('增量加载失败'); }
  finally { kbLoading.value = false; }
}

onMounted(fetchSearchMode);

const ragPresets = ['苯的储存要求是什么', '甲类仓库消防间距标准', '危化品装卸作业安全规范'];
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <h1 class="text-xl font-bold text-slate-900">知识库管理</h1>
    <p class="text-xs text-slate-500 -mt-4">RAG 检索测试、搜索模式切换、知识库增量更新</p>

    <!-- 搜索模式 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">搜索模式</h3>
      <div class="flex items-center gap-3 flex-wrap">
        <span class="text-xs text-slate-500">当前:</span>
        <span class="text-sm font-mono font-bold text-blue-700">{{ searchMode || '加载中…' }}</span>
        <div class="flex gap-2 ml-2">
          <button
            v-for="m in (['Bm25','Vector','Hybrid'] as const)"
            :key="m"
            @click="switchMode(m)"
            :disabled="modeLoading || searchMode === m"
            class="text-xs px-3 py-1.5 rounded border transition-colors"
            :class="searchMode === m ? 'border-blue-300 bg-blue-50 text-blue-700' : 'border-slate-200 text-slate-500 hover:bg-slate-50'"
          >{{ m }}</button>
        </div>
      </div>
    </div>

    <!-- RAG 测试 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">RAG 检索测试</h3>
      <div class="flex gap-2 mb-3">
        <input
          v-model="ragQuery"
          @keyup.enter="runRagTest"
          :disabled="ragLoading"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400"
          placeholder="输入查询内容测试 RAG 检索效果…"
        />
        <button
          @click="runRagTest"
          :disabled="ragLoading || !ragQuery.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >{{ ragLoading ? '检索中…' : '测试' }}</button>
      </div>
      <div class="flex gap-2 flex-wrap">
        <button
          v-for="p in ragPresets" :key="p"
          @click="ragQuery = p; runRagTest()"
          :disabled="ragLoading"
          class="text-xs px-2.5 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50"
        >{{ p }}</button>
      </div>

      <div v-if="ragError" class="mt-3 bg-red-50 border border-red-200 rounded p-3 text-sm text-red-700">{{ ragError }}</div>

      <div v-if="ragResult" class="mt-4 space-y-4">
        <div class="text-xs text-slate-400">耗时 {{ ragResult.elapsedMs }}ms · 召回 {{ ragResult.totalResults }} 个片段</div>
        <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100">
          {{ ragResult.summary }}
        </div>
        <details class="text-xs" v-if="ragResult.results?.length">
          <summary class="cursor-pointer text-slate-500 hover:text-slate-700">查看 Top-{{ ragResult.results.length }} 检索片段</summary>
          <div class="mt-2 space-y-2">
            <div v-for="c in ragResult.results" :key="c.id"
              class="p-3 bg-slate-50 border border-slate-200 rounded">
              <div class="flex justify-between mb-1">
                <span class="text-blue-600 font-mono">#{{ c.rank }}</span>
                <span class="text-slate-400">score: {{ c.score.toFixed(4) }} · {{ c.retrievalMethod }}</span>
              </div>
              <p class="text-slate-600 leading-relaxed">{{ c.content }}</p>
            </div>
          </div>
        </details>
      </div>
    </div>

    <!-- 增量加载 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">知识库增量更新</h3>
      <p class="text-xs text-slate-500 mb-3">扫描知识库目录，增量加载新增/修改的文档，无需重启服务</p>
      <button
        @click="incrementalLoad"
        :disabled="kbLoading"
        class="text-xs px-4 py-2 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 disabled:opacity-50 transition-colors"
      >{{ kbLoading ? '加载中…' : '📚 执行增量加载' }}</button>
    </div>
  </div>
</template>
