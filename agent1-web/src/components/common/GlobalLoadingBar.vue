<script setup lang="ts">
import { useLoadingBar } from '@/lib/useLoadingBar';
const { visible, message } = useLoadingBar();
</script>

<template>
  <Transition
    enter-active-class="transition-opacity duration-200"
    leave-active-class="transition-opacity duration-300"
    enter-from-class="opacity-0"
    leave-to-class="opacity-0"
  >
    <div v-if="visible" class="fixed top-0 left-0 w-full z-[9999]">
      <div class="h-0.5 bg-gradient-to-r from-blue-500 to-blue-300 animate-progress" />
      <div class="bg-blue-50 border-b border-blue-100 px-4 py-1.5 flex items-center gap-2">
        <svg
          class="animate-spin shrink-0" width="14" height="14" viewBox="0 0 24 24"
          fill="none" stroke="#2563eb" stroke-width="2"
        >
          <circle cx="12" cy="12" r="10" stroke-dasharray="31.4 31.4" stroke-linecap="round" />
        </svg>
        <span class="text-xs text-blue-700">{{ message || '加载中...' }}</span>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
@keyframes progress {
  0%   { width: 0; }
  50%  { width: 70%; }
  100% { width: 95%; }
}
.animate-progress {
  animation: progress 8s ease-in-out infinite;
}
</style>
