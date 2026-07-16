<script setup lang="ts">
// ============================================================
// AppSidebar — 分组可折叠导航（按业务域分类）
//
// 权限对齐:
//   admin    → 全部菜单
//   auditor  → 除系统设置外的全部菜单
//   viewer   → 仪表盘 / 合规历史 / 巡检记录 / 工单管理 / 资产台账（只读）
// ============================================================

import { computed, ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuthStore } from '@/stores/auth';
import {
  Monitor, Document, Clock, List, Warning, Box, Setting, Search, Link,
  ChatDotRound, Odometer, Cpu, DataAnalysis, Picture, Aim, FirstAidKit, Connection,
  TrendCharts, Checked, FolderOpened, MagicStick, Tools,
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

interface NavGroup {
  title: string;
  icon: typeof Monitor;
  items: NavItem[];
  roles?: string[];
}

const allGroups: NavGroup[] = [
  {
    title: '总览', icon: TrendCharts,
    items: [
      { path: '/dashboard', title: '仪表盘', icon: Monitor, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/system', title: '运维看板', icon: Odometer, roles: ['admin'] },
    ],
  },
  {
    title: '合规管理', icon: Checked,
    items: [
      { path: '/compliance', title: '合规检查', icon: Document, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/compliance/history', title: '合规历史', icon: Clock, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/eval', title: '合规评测', icon: DataAnalysis, roles: ['admin', 'auditor', 'viewer'] },
    ],
  },
  {
    title: '巡检与资产', icon: FolderOpened,
    items: [
      { path: '/inspection/plans', title: '巡检计划', icon: List, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/inspection/rounds', title: '巡检记录', icon: Clock, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/tickets', title: '工单管理', icon: Warning, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/assets', title: '资产台账', icon: Box, roles: ['admin', 'auditor', 'viewer'] },
    ],
  },
  {
    title: '危化品管理', icon: Search,
    items: [
      { path: '/hazard', title: '危化品查询', icon: Search, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/storage/compatibility', title: '储存兼容性', icon: Link, roles: ['admin', 'auditor', 'viewer'] },
    ],
  },
  {
    title: 'AI 智能工具', icon: MagicStick,
    items: [
      { path: '/chat', title: 'AI 合规助手', icon: ChatDotRound, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/regulatory', title: '法规审计', icon: Aim, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/emergency', title: '应急响应', icon: FirstAidKit, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/knowledgegraph', title: '知识图谱', icon: Connection, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/multimodal', title: '多模态分析', icon: Picture, roles: ['admin', 'auditor', 'viewer'] },
    ],
  },
  {
    title: '系统管理', icon: Tools,
    items: [
      { path: '/knowledgebase', title: '知识库', icon: FolderOpened, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/diagnostics', title: '工具诊断', icon: Cpu, roles: ['admin', 'auditor', 'viewer'] },
      { path: '/audit', title: '审计日志', icon: Document, roles: ['admin'] },
      { path: '/settings', title: '系统设置', icon: Setting, roles: ['admin'] },
    ],
  },
];

// 过滤：角色内至少有一个子项的组才显示
const visibleGroups = computed(() => {
  if (!auth.role) return [];
  return allGroups
    .map((g) => ({
      ...g,
      items: g.items.filter((i) => !i.roles || i.roles.includes(auth.role!)),
    }))
    .filter((g) => g.items.length > 0);
});

// 折叠状态：默认全部展开
const collapsed = ref<Record<string, boolean>>({});

function toggleGroup(title: string) {
  collapsed.value[title] = !collapsed.value[title];
}

// 自动展开包含当前路径的组
const activePath = computed(() => route.path);

function isGroupActive(group: NavGroup): boolean {
  return group.items.some(
    (item) => activePath.value === item.path || activePath.value.startsWith(item.path + '/')
  );
}

function navigateTo(path: string) {
  router.push(path);
}
</script>

<template>
  <div class="app-sidebar h-full flex flex-col bg-white border-r border-gray-200">
    <!-- Logo / 标题 -->
    <div class="px-5 py-4 border-b border-gray-100 flex items-center gap-2">
      <span class="text-lg font-bold text-blue-700">Agent1</span>
      <span class="text-xs text-gray-400">化工合规</span>
    </div>

    <!-- 分组导航菜单 -->
    <nav class="flex-1 py-2 overflow-y-auto">
      <div v-for="group in visibleGroups" :key="group.title" class="mb-1">
        <!-- 分组标题（可点击折叠） -->
        <div
          class="flex items-center px-3 py-1.5 mx-3 cursor-pointer select-none"
          @click="toggleGroup(group.title)"
        >
          <span class="text-xs text-gray-400 mr-1.5 w-3 inline-block">
            {{ collapsed[group.title] ? '▸' : '▾' }}
          </span>
          <span
            class="text-xs font-semibold uppercase tracking-wider"
            :class="isGroupActive(group) ? 'text-blue-600' : 'text-gray-400'"
          >
            {{ group.title }}
          </span>
          <span class="ml-auto text-xs text-gray-300">{{ group.items.length }}</span>
        </div>

        <!-- 子菜单项 -->
        <div v-show="!collapsed[group.title]" class="px-1">
          <div
            v-for="item in group.items"
            :key="item.path"
            class="nav-item flex items-center px-5 py-2 ml-2 mr-1 rounded-md cursor-pointer transition-colors text-sm"
            :class="{
              'bg-blue-50 text-blue-700 font-medium': activePath === item.path || activePath.startsWith(item.path + '/'),
              'text-gray-600 hover:bg-gray-50': !(activePath === item.path || activePath.startsWith(item.path + '/')),
            }"
            @click="navigateTo(item.path)"
          >
            <el-icon class="mr-3 text-base"><component :is="item.icon" /></el-icon>
            {{ item.title }}
          </div>
        </div>
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
