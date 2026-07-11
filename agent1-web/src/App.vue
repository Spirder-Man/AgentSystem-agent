<script setup lang="ts">
// ============================================================
// App.vue — 根组件，根据路由决定是否显示侧边栏布局
// ============================================================

import { computed } from 'vue';
import { useRoute } from 'vue-router';
import AppSidebar from '@/components/layout/AppSidebar.vue';
import GlobalLoadingBar from '@/components/common/GlobalLoadingBar.vue';

const route = useRoute();

/** 不需要侧边栏的路由名称 */
const NO_SIDEBAR_ROUTES = new Set(['Login', 'Forbidden']);

const showSidebar = computed(() => {
  return !NO_SIDEBAR_ROUTES.has(route.name as string);
});
</script>

<template>
  <GlobalLoadingBar />

  <!-- 无侧边栏: 登录页 / 403 等 -->
  <router-view v-if="!showSidebar" />

  <!-- 有侧边栏: 业务页面布局 -->
  <div v-else class="flex h-screen bg-gray-50">
    <AppSidebar />
    <main class="flex-1 overflow-y-auto p-6">
      <router-view />
    </main>
  </div>
</template>
