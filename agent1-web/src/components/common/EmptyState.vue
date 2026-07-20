<script setup lang="ts">
withDefaults(defineProps<{
  title?: string;
  description?: string;
  icon?: 'empty' | 'search' | 'error' | 'lock';
}>(), {
  title: '暂无数据',
  description: '',
  icon: 'empty',
});

const emit = defineEmits<{
  action: [];
}>();

const iconPaths: Record<string, string> = {
  empty: 'M20 13V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7m16 0v5a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-5m16 0h-2.586a1 1 0 0 0-.707.293l-2.414 2.414a1 1 0 0 1-.707.293h-3.172a1 1 0 0 1-.707-.293l-2.414-2.414A1 1 0 0 0 6.586 13H4',
  search: 'M21 21l-6-6m2-5a7 7 0 1 1-14 0 7 7 0 0 1 14 0z',
  error: 'M12 9v2m0 4h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z',
  lock: 'M12 15v2m-6 4h12a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2zm10-10V7a4 4 0 0 0-8 0v4h8z',
};
</script>

<template>
  <div class="flex flex-col items-center justify-center py-16 px-4">
    <svg
      class="mb-4 text-slate-300"
      width="56" height="56" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" stroke-width="1.5"
      stroke-linecap="round" stroke-linejoin="round"
    >
      <path :d="iconPaths[icon]" />
    </svg>
    <p class="text-sm font-medium text-slate-600 mb-1">{{ title }}</p>
    <p v-if="description" class="text-xs text-slate-400 mb-4">{{ description }}</p>
    <slot name="action">
      <button
        v-if="$slots.action || emit"
        class="px-3 py-1.5 text-xs border border-slate-300 rounded text-slate-600 hover:bg-slate-50"
        @click="emit('action')"
      >
        <slot name="action-text">刷新</slot>
      </button>
    </slot>
  </div>
</template>
