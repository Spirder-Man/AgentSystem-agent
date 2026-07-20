<script setup lang="ts">
defineProps<{ count?: number }>();
</script>

<template>
  <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
    <div
      v-for="i in (count ?? 3)" :key="i"
      class="bg-white border border-slate-200 rounded p-4 animate-pulse"
    >
      <div class="h-4 bg-slate-200 rounded w-3/4 mb-3" />
      <div class="h-3 bg-slate-100 rounded w-full mb-2" />
      <div class="h-3 bg-slate-100 rounded w-2/3 mb-4" />
      <div class="h-6 bg-slate-100 rounded w-1/3" />
    </div>
  </div>
</template>
