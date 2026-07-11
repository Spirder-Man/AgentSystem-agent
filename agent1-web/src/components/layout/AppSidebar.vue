<script setup lang="ts">
// ============================================================
// AppSidebar — 角色感知的侧边栏导航
//
// 权限对齐:
//   admin    → 全部菜单
//   auditor  → 除 Settings 外的全部菜单
//   viewer   → 无菜单 (仅 header 显示"无访问权限")
// ============================================================

import { computed } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import {
  Monitor,
  Document,
  Clock,
  List,
  Warning,
  Box,
  Setting,
} from '@element-plus/icons-vue';

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

interface NavItem {
  path: string;
  title: string;
  icon: typeof Monitor;
  roles?: string[];
}

const allNavItems: NavItem[] = [
  { path: '/dashboard', title: '仪表盘', icon: Monitor, roles: ['admin', 'auditor', 'viewer'] },
  { path: '/compliance', title: '合规检查', icon: Document, roles: ['admin', 'auditor'] },
  { path: '/compliance/history', title: '合规历史', icon: Clock, roles: ['admin', 'auditor', 'viewer'] },
  { path: '/inspection/plans', title: '巡检计划', icon: List, roles: ['admin', 'auditor'] },
  { path: '/inspection/rounds', title: '巡检记录', icon: List, roles: ['admin', 'auditor', 'viewer'] },
  { path: '/tickets', title: '工单管理', icon: Warning, roles: ['admin', 'auditor', 'viewer'] },
  { path: '/assets', title: '资产台账', icon: Box, roles: ['admin', 'auditor', 'viewer'] },
  { path: '/audit', title: '审计日志', icon: Document, roles: ['admin'] },
  { path: '/settings', title: '系统设置', icon: Setting, roles: ['admin'] },
];

const visibleItems = computed(() => {
  if (!auth.role) return [];
  // viewer: 无任何业务菜单
  if (auth.isViewer) return [];
  return allNavItems.filter(
    (item) => !item.roles || item.roles.includes(auth.role!)
  );
});

const activePath = computed(() => route.path);

function navigateTo(path: string) {
  router.push(path);
}
</script>

<template>
  <div class="app-sidebar h-full flex flex-col bg-white border-r border-gray-200">
    <!-- Logo / 标题 -->
    <div class="px-5 py-4 border-b border-gray-100">
      <span class="text-lg font-bold text-blue-700">Agent1</span>
      <span class="text-xs text-gray-400 ml-2">化工合规</span>
    </div>

    <!-- 导航菜单 -->
    <nav class="flex-1 py-3 overflow-y-auto">
      <div
        v-for="item in visibleItems"
        :key="item.path"
        class="nav-item flex items-center px-5 py-2.5 mx-2 my-0.5 rounded-md cursor-pointer transition-colors text-sm"
        :class="{
          'bg-blue-50 text-blue-700 font-medium': activePath === item.path || activePath.startsWith(item.path + '/'),
          'text-gray-600 hover:bg-gray-50': !(activePath === item.path || activePath.startsWith(item.path + '/')),
        }"
        @click="navigateTo(item.path)"
      >
        <el-icon class="mr-3"><component :is="item.icon" /></el-icon>
        {{ item.title }}
      </div>
    </nav>

    <!-- 底部用户信息 -->
    <div class="px-5 py-3 border-t border-gray-100 text-xs text-gray-400">
      {{ auth.username }} · {{ auth.role }}
    </div>
  </div>
</template>

<style scoped>
.app-sidebar {
  width: 220px;
  min-width: 220px;
}

.nav-item:active {
  transform: scale(0.98);
}
</style>
