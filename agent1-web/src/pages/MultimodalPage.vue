<script setup lang="ts">
import { ref } from 'vue';
import apiClient from '@/lib/axios';
import type { MultimodalResult, AnalysisType } from '@/types/api';
import { ElMessage } from 'element-plus';
import { useLoadingBar } from '@/lib/useLoadingBar';

const analysisType = ref<AnalysisType>('hazard-label');
const file = ref<File | null>(null);
const imgPreview = ref('');
const result = ref<MultimodalResult | null>(null);
const loading = ref(false);
const error = ref('');
const { start, stop } = useLoadingBar();

function onFileChange(e: Event) {
  const target = e.target as HTMLInputElement;
  const f = target.files?.[0];
  if (!f) return;
  if (f.size > 20 * 1024 * 1024) { ElMessage.error('文件不能超过 20MB'); return; }
  file.value = f;
  const reader = new FileReader();
  reader.onload = () => { imgPreview.value = reader.result as string; };
  reader.readAsDataURL(f);
}

async function analyze() {
  if (!file.value) return;
  error.value = '';
  result.value = null;
  loading.value = true;
  start('正在进行图像分析…');
  try {
    const fd = new FormData();
    fd.append('image', file.value);
    fd.append('analysisType', analysisType.value);
    const { data } = await apiClient.post<MultimodalResult>('/api/multimodal/analyze', fd, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    result.value = data;
    ElMessage.success('分析完成');
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '分析失败';
  } finally { loading.value = false; stop(); }
}

const typeLabels: Record<AnalysisType, string> = {
  'hazard-label': '危化品标签识别',
  'storage-scene': '储存场景检测',
  'custom': '自定义分析',
};
</script>

<template>
  <div class="space-y-6 max-w-3xl">
    <h1 class="text-xl font-bold text-slate-900">多模态分析</h1>
    <p class="text-xs text-slate-500 -mt-4">上传图片，AI 识别危化品标签、储存场景或自定义分析</p>

    <!-- 上传区 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">上传图片</h3>
      <div class="flex flex-wrap gap-2 mb-3">
        <button
          v-for="t in (['hazard-label','storage-scene','custom'] as AnalysisType[])" :key="t"
          @click="analysisType = t"
          class="text-xs px-3 py-1.5 rounded border transition-colors"
          :class="analysisType === t ? 'border-blue-300 bg-blue-50 text-blue-700' : 'border-slate-200 text-slate-500 hover:bg-slate-50'"
        >{{ typeLabels[t] }}</button>
      </div>
      <label class="block border-2 border-dashed border-slate-300 rounded-lg p-8 text-center cursor-pointer hover:border-blue-400 transition-colors">
        <input type="file" accept="image/*" @change="onFileChange" class="hidden" />
        <div v-if="!imgPreview" class="text-slate-400 text-sm">
          <div class="text-3xl mb-2">📷</div>
          点击选择图片（最大 20MB）
        </div>
        <img v-else :src="imgPreview" class="max-h-48 mx-auto rounded" />
      </label>
      <button
        v-if="file"
        @click="analyze"
        :disabled="loading"
        class="mt-3 px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
      >{{ loading ? 'AI 分析中…' : '🔍 开始分析' }}</button>
    </div>

    <div v-if="error" class="bg-red-50 border border-red-200 rounded p-4 text-sm text-red-700">{{ error }}</div>

    <div v-if="result" class="bg-white border border-slate-200 rounded p-5">
      <div class="flex items-center justify-between mb-3">
        <h3 class="text-sm font-semibold text-slate-700">分析结果</h3>
        <span class="text-xs text-slate-400">{{ typeLabels[result.analysisType] }}</span>
      </div>
      <div class="text-sm whitespace-pre-wrap leading-relaxed text-slate-700 p-4 bg-slate-50 rounded border border-slate-100">
        {{ result.result }}
      </div>
    </div>
  </div>
</template>
