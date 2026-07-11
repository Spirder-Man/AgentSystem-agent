<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue';
import { WarningFilled, Refresh } from '@element-plus/icons-vue';

const error = ref<string | null>(null);
const errorStack = ref<string>('');

onErrorCaptured((err: unknown, _instance, info: string) => {
  error.value = err instanceof Error ? err.message : String(err);
  errorStack.value = info;
  console.error('[ErrorBoundary] 捕获到子组件错误:', err, info);
  return false; // 阻止错误继续向上传播，防止白屏
});

function retry() {
  error.value = null;
  errorStack.value = '';
}
</script>

<template>
  <div v-if="error" class="flex items-center justify-center min-h-[400px]">
    <div class="bg-white border border-red-200 rounded-lg p-8 max-w-lg w-full text-center shadow-sm">
      <div class="mb-4">
        <el-icon :size="48" class="text-red-400"><WarningFilled /></el-icon>
      </div>
      <h2 class="text-lg font-semibold text-slate-800 mb-2">页面发生异常</h2>
      <p class="text-sm text-slate-500 mb-1">组件渲染过程中出现未预期的错误</p>
      <p class="text-xs text-slate-400 font-mono bg-slate-50 rounded px-3 py-2 mb-4 max-h-20 overflow-y-auto">{{ error }}</p>
      <el-button type="primary" :icon="Refresh" size="small" @click="retry">重试</el-button>
    </div>
  </div>
  <slot v-else />
</template>
