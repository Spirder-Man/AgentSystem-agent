// ============================================================
// Vue Router 4 — 路由配置 & 角色守卫
//
// 权限对齐后端 Program.cs 授权策略:
//   Admin   = admin only
//   Auditor = admin + auditor (业务 Controller 实际策略)
//   Viewer  = admin + auditor + viewer (已定义，待后端启用)
//
// viewer 当前只能访问:
//   /login  (登录页)
//   /403    (无权限提示)
// ============================================================

import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
import type { UserRole } from '@/types/api';
import { useAuthStore } from '@/stores/auth';

// 扩展 RouteMeta 类型声明
declare module 'vue-router' {
  interface RouteMeta {
    title?: string;
    requiresAuth?: boolean;
    /** 允许访问的角色列表；空数组 = 无限制 */
    roles?: UserRole[];
  }
}

// ── 页面懒加载 (占位符 — 页面组件逐步实现) ──
const LoginPage = () => import('@/pages/LoginPage.vue');
const DashboardPage = () => import('@/pages/DashboardPage.vue');
const ForbiddenPage = () => import('@/pages/ForbiddenPage.vue');
// 以下页面尚未实现，使用统一的占位组件
const CompliancePage = () => import('@/pages/PlaceholderPage.vue');
const ComplianceHistoryPage = () => import('@/pages/PlaceholderPage.vue');
const InspectionPlansPage = () => import('@/pages/PlaceholderPage.vue');
const InspectionRoundsPage = () => import('@/pages/PlaceholderPage.vue');
const TicketListPage = () => import('@/pages/PlaceholderPage.vue');
const TicketDetailPage = () => import('@/pages/PlaceholderPage.vue');
const AssetsPage = () => import('@/pages/PlaceholderPage.vue');
const AuditPage = () => import('@/pages/PlaceholderPage.vue');
const SettingsPage = () => import('@/pages/PlaceholderPage.vue');

// ═══════════════════════════════════════
// 路由表
// ═══════════════════════════════════════

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    redirect: '/dashboard',
  },

  // ── 公共路由 (无需认证) ──
  {
    path: '/login',
    name: 'Login',
    component: LoginPage,
    meta: { title: '登录' },
  },
  {
    path: '/403',
    name: 'Forbidden',
    component: ForbiddenPage,
    meta: { title: '无权限' },
  },

  // ── 业务路由 (admin / auditor 可访问) ──
  // ⚠️ viewer 不在 roles 中 → 后端 401/403，前端导航守卫拦截
  {
    path: '/dashboard',
    name: 'Dashboard',
    component: DashboardPage,
    meta: { title: '仪表盘', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/compliance',
    name: 'Compliance',
    component: CompliancePage,
    meta: { title: '合规检查', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/compliance/history',
    name: 'ComplianceHistory',
    component: ComplianceHistoryPage,
    meta: { title: '合规历史', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/inspection/plans',
    name: 'InspectionPlans',
    component: InspectionPlansPage,
    meta: { title: '巡检计划', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/inspection/rounds',
    name: 'InspectionRounds',
    component: InspectionRoundsPage,
    meta: { title: '巡检记录', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/tickets',
    name: 'Tickets',
    component: TicketListPage,
    meta: { title: '工单管理', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/tickets/:id',
    name: 'TicketDetail',
    component: TicketDetailPage,
    meta: { title: '工单详情', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/assets',
    name: 'Assets',
    component: AssetsPage,
    meta: { title: '资产台账', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/audit',
    name: 'Audit',
    component: AuditPage,
    meta: { title: '审计日志', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/settings',
    name: 'Settings',
    component: SettingsPage,
    meta: { title: '系统设置', requiresAuth: true, roles: ['admin'] },
  },

  // ── 404 兜底 ──
  {
    path: '/:pathMatch(.*)*',
    redirect: '/dashboard',
  },
];

// ═══════════════════════════════════════
// Router 实例
// ═══════════════════════════════════════

export const router = createRouter({
  history: createWebHistory(),
  routes,
});

// ═══════════════════════════════════════
// 全局前置守卫 — 角色拦截
// ═══════════════════════════════════════

router.beforeEach((to, _from, next) => {
  // 无限制路由直接放行
  if (!to.meta.requiresAuth && (!to.meta.roles || to.meta.roles.length === 0)) {
    return next();
  }

  const auth = useAuthStore();

  // 未登录 → 重定向到登录页
  if (!auth.isAuthenticated) {
    return next({ name: 'Login', query: { redirect: to.fullPath } });
  }

  // Token 过期 → 清除并重定向
  if (auth.isExpired) {
    auth.clearAuth();
    return next({ name: 'Login', query: { redirect: to.fullPath } });
  }

  // 角色检查
  if (to.meta.roles && to.meta.roles.length > 0) {
    if (!auth.canAccessRoute(to.meta.roles)) {
      // viewer 或其他无权限角色 → 跳转 403
      return next({ name: 'Forbidden' });
    }
  }

  next();
});
