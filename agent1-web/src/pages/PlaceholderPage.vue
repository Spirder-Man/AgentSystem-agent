<script setup lang="ts">
import { useRoute } from 'vue-router';

const route = useRoute();
</script>

<template>
  <div class="flex items-center justify-center min-h-[60vh]">
    <div class="text-center">
      <div class="text-4xl mb-4">🚧</div>
      <h2 class="text-lg font-semibold text-gray-600 mb-2">页面开发中</h2>
      <p class="text-sm text-gray-400">
        {{ (route.meta.title as string) || route.path }} — 即将上线
      </p>
    </div>
  </div>
</template>
