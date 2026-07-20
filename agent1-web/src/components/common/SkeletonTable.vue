<script setup lang="ts">
defineProps<{ rows?: number }>();
</script>

<template>
  <div class="bg-white border border-slate-200 rounded animate-pulse">
    <!-- header -->
    <div class="px-4 py-3 border-b border-slate-100 flex gap-6">
      <div v-for="i in 5" :key="'h' + i" class="h-3 bg-slate-200 rounded" :style="{ width: `${60 + (i % 3) * 30}px` }" />
    </div>
    <!-- rows -->
    <div
      v-for="r in (rows ?? 5)" :key="r"
      class="px-4 py-3 border-b border-slate-50 flex gap-6"
    >
      <div v-for="c in 5" :key="c" class="h-3 bg-slate-100 rounded" :style="{ width: `${80 + (c % 3) * 40}px` }" />
    </div>
  </div>
</template>
