// ============================================================
// Auth Store — 认证状态管理 & 角色权限守卫
// 对齐后端 Program.cs 授权策略:
//   Admin   = admin only
//   Auditor = admin + auditor (所有业务 Controller 的策略)
//   Viewer  = admin + auditor + viewer (已定义，待后端开放 GET 端点后启用)
// ============================================================

import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { UserRole, LoginResponse } from '@/types/api';

export const useAuthStore = defineStore('auth', () => {
  // ══ State ══
  const token = ref<string | null>(null);
  const refreshToken = ref<string | null>(null);
  const username = ref<string>('');
  const role = ref<UserRole | null>(null);
  const expiresAt = ref<string | null>(null);

  // ══ Getters ══

  /** 是否已登录 */
  const isAuthenticated = computed(() => !!token.value && !!role.value);

  /** 是否已过期 */
  const isExpired = computed(() => {
    if (!expiresAt.value) return true;
    return new Date(expiresAt.value) <= new Date();
  });

  const isAdmin = computed(() => role.value === 'admin');
  const isAuditor = computed(() => role.value === 'auditor');
  const isViewer = computed(() => role.value === 'viewer');

  /**
   * 角色权限检查 — 核心守卫逻辑
   *
   * 当前策略 (对齐后端实际情况):
   *   - viewer 角色对任何业务操作返回 false
   *   - 原因: 后端所有业务 Controller 使用 [Authorize(Policy = "Auditor")]
   *   - viewer 仅能访问 /login (登录前) 和 /403 (被拒绝后)
   *
   * 后端增开 Viewer 策略的 GET 端点后，将以下代码取消注释:
   *   if (isViewer.value && requiredRoles.includes('viewer')) return true;
   */
  function hasPermission(requiredRoles: UserRole[]): boolean {
    if (!isAuthenticated.value || isExpired.value) return false;

    // admin 可以访问所有
    if (isAdmin.value) return true;

    // auditor 可以访问 Auditor/Viewer 级别
    if (isAuditor.value) return requiredRoles.includes('auditor')
      || requiredRoles.includes('viewer');

    // viewer — 当前后端无 Viewer 策略端点，拒绝所有业务访问
    // 待后端在 GET 端点增加 [Authorize(Policy = "Viewer")] 后启用:
    // if (isViewer.value) return requiredRoles.includes('viewer');
    return false;
  }

  /** 判断当前用户是否能访问某条路由 (router meta.roles) */
  function canAccessRoute(requiredRoles?: UserRole[]): boolean {
    if (!requiredRoles || requiredRoles.length === 0) return true; // 无限制路由
    return hasPermission(requiredRoles);
  }

  // ══ Actions ══

  function setAuth(data: LoginResponse) {
    token.value = data.token;
    refreshToken.value = data.refreshToken;
    username.value = data.username;
    role.value = data.role;
    expiresAt.value = data.expiresAt;

    // 持久化到 localStorage (注意: Token 安全见 docs/frontend/ 安全加固章节)
    localStorage.setItem('auth_token', data.token);
    localStorage.setItem('auth_refresh', data.refreshToken);
    localStorage.setItem('auth_user', JSON.stringify({
      username: data.username,
      role: data.role,
      expiresAt: data.expiresAt,
    }));
  }

  function restoreAuth(): boolean {
    const savedToken = localStorage.getItem('auth_token');
    const savedUser = localStorage.getItem('auth_user');
    if (!savedToken || !savedUser) return false;

    try {
      const user = JSON.parse(savedUser);
      token.value = savedToken;
      refreshToken.value = localStorage.getItem('auth_refresh');
      username.value = user.username ?? '';
      role.value = user.role ?? null;
      expiresAt.value = user.expiresAt ?? null;

      // 检查是否过期
      if (isExpired.value) {
        clearAuth();
        return false;
      }
      return true;
    } catch {
      clearAuth();
      return false;
    }
  }

  function clearAuth() {
    token.value = null;
    refreshToken.value = null;
    username.value = '';
    role.value = null;
    expiresAt.value = null;

    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_refresh');
    localStorage.removeItem('auth_user');
  }

  return {
    // state
    token,
    refreshToken,
    username,
    role,
    expiresAt,
    // getters
    isAuthenticated,
    isExpired,
    isAdmin,
    isAuditor,
    isViewer,
    hasPermission,
    canAccessRoute,
    // actions
    setAuth,
    restoreAuth,
    clearAuth,
  };
});
