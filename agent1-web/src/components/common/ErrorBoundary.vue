<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue';

const hasError = ref(false);
const errorMessage = ref('');

onErrorCaptured((err: unknown) => {
  hasError.value = true;
  errorMessage.value = err instanceof Error ? err.message : '未知运行时错误';
  return false; // 阻止向上冒泡
});

function reset() {
  hasError.value = false;
  errorMessage.value = '';
}
</script>

<template>
  <div v-if="hasError" class="flex items-center justify-center min-h-[200px] p-8">
    <div class="text-center max-w-md">
      <svg
        class="mx-auto mb-4"
        width="48" height="48" viewBox="0 0 24 24" fill="none"
        stroke="#dc2626" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="8" x2="12" y2="12" />
        <line x1="12" y1="16" x2="12.01" y2="16" />
      </svg>
      <h3 class="text-base font-semibold text-slate-900 mb-2">组件渲染异常</h3>
      <p class="text-sm text-slate-500 mb-4 font-mono">{{ errorMessage }}</p>
      <button
        @click="reset"
        class="px-4 py-1.5 text-sm border border-slate-300 rounded text-slate-700 hover:bg-slate-50"
      >
        重试
      </button>
    </div>
  </div>
  <slot v-else />
</template>
