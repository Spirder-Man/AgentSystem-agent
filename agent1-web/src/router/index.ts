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

// ── 页面懒加载 ──
const LoginPage = () => import('@/pages/LoginPage.vue');
const DashboardPage = () => import('@/pages/DashboardPage.vue');
const ForbiddenPage = () => import('@/pages/ForbiddenPage.vue');
const CompliancePage = () => import('@/pages/ComplianceCheckPage.vue');
const ComplianceHistoryPage = () => import('@/pages/ComplianceHistoryPage.vue');
const TicketListPage = () => import('@/pages/TicketListPage.vue');
// 以下页面尚未实现
const InspectionPlansPage = () => import('@/pages/InspectionPlansPage.vue');
const InspectionPlanDetailPage = () => import('@/pages/InspectionPlanDetailPage.vue');
const InspectionRoundsPage = () => import('@/pages/InspectionRoundsPage.vue');
const InspectionRoundDetailPage = () => import('@/pages/InspectionRoundDetailPage.vue');
const TicketDetailPage = () => import('@/pages/TicketDetailPage.vue');
const AssetsPage = () => import('@/pages/AssetsPage.vue');
const AssetDetailPage = () => import('@/pages/AssetDetailPage.vue');
const AuditPage = () => import('@/pages/AuditPage.vue');
const SettingsPage = () => import('@/pages/SettingsPage.vue');
const InspectionReportPage = () => import('@/pages/InspectionReportPage.vue');
const HazardQueryPage = () => import('@/pages/HazardQueryPage.vue');
const StorageCompatibilityPage = () => import('@/pages/StorageCompatibilityPage.vue');
const AIChatPage = () => import('@/pages/AIChatPage.vue');
const SystemMonitorPage = () => import('@/pages/SystemMonitorPage.vue');

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
    meta: { title: '仪表盘', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
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
    meta: { title: '合规历史', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/inspection/plans/:planId',
    name: 'InspectionPlanDetail',
    component: InspectionPlanDetailPage,
    meta: { title: '计划详情', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/inspection/plans',
    name: 'InspectionPlans',
    component: InspectionPlansPage,
    meta: { title: '巡检计划', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/inspection/rounds/:roundId',
    name: 'InspectionRoundDetail',
    component: InspectionRoundDetailPage,
    meta: { title: '轮次详情', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/inspection/rounds',
    name: 'InspectionRounds',
    component: InspectionRoundsPage,
    meta: { title: '巡检记录', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/inspection/report/:roundId',
    name: 'InspectionReport',
    component: InspectionReportPage,
    meta: { title: '巡检报告', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/tickets',
    name: 'Tickets',
    component: TicketListPage,
    meta: { title: '工单管理', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
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
    meta: { title: '资产台账', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/assets/:assetId',
    name: 'AssetDetail',
    component: AssetDetailPage,
    meta: { title: '资产详情', requiresAuth: true, roles: ['admin', 'auditor', 'viewer'] },
  },
  {
    path: '/audit',
    name: 'Audit',
    component: AuditPage,
    meta: { title: '审计日志', requiresAuth: true, roles: ['admin'] },
  },
  {
    path: '/settings',
    name: 'Settings',
    component: SettingsPage,
    meta: { title: '系统设置', requiresAuth: true, roles: ['admin'] },
  },
  {
    path: '/hazard',
    name: 'HazardQuery',
    component: HazardQueryPage,
    meta: { title: '危化品查询', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/storage/compatibility',
    name: 'StorageCompatibility',
    component: StorageCompatibilityPage,
    meta: { title: '储存兼容性', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/chat',
    name: 'AIChat',
    component: AIChatPage,
    meta: { title: 'AI 合规助手', requiresAuth: true, roles: ['admin', 'auditor'] },
  },
  {
    path: '/system',
    name: 'SystemMonitor',
    component: SystemMonitorPage,
    meta: { title: '运维看板', requiresAuth: true, roles: ['admin'] },
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
