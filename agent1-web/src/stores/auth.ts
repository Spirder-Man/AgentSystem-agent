// ============================================================
// Auth Store — 认证状态管理 & 角色权限守卫
// ============================================================

import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { UserRole, LoginResponse, LoginRequest, ApiError } from '@/types/api';
import apiClient from '@/lib/axios';
import { ElMessage } from 'element-plus';

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(null);
  const refreshToken = ref<string | null>(null);
  const username = ref<string>('');
  const role = ref<UserRole | null>(null);
  const expiresAt = ref<string | null>(null);

  const isAuthenticated = computed(() => !!token.value && !!role.value);
  const isExpired = computed(() => {
    if (!expiresAt.value) return true;
    return new Date(expiresAt.value) <= new Date();
  });
  const isAdmin = computed(() => role.value === 'admin');
  const isAuditor = computed(() => role.value === 'auditor');
  const isViewer = computed(() => role.value === 'viewer');

  function hasPermission(requiredRoles: UserRole[]): boolean {
    if (!isAuthenticated.value || isExpired.value) return false;
    if (isAdmin.value) return true;
    if (isAuditor.value) return requiredRoles.includes('auditor') || requiredRoles.includes('viewer');
    // viewer 只能访问标记了 'viewer' 角色的路由（只读业务页面）
    if (isViewer.value) return requiredRoles.includes('viewer');
    return false;
  }

  function canAccessRoute(requiredRoles?: UserRole[]): boolean {
    if (!requiredRoles || requiredRoles.length === 0) return true;
    return hasPermission(requiredRoles);
  }

  async function login(credentials: LoginRequest): Promise<boolean> {
    try {
      const { data } = await apiClient.post<LoginResponse>('/api/Auth/login', credentials);
      setAuth(data);
      return true;
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: ApiError; status?: number } };
      ElMessage.error(axiosErr.response?.data?.error || '登录失败');
      return false;
    }
  }

  async function logout(): Promise<void> {
    try { await apiClient.post('/api/Auth/logout'); } catch { /* ignore */ }
    clearAuth();
  }

  function setAuth(data: LoginResponse) {
    token.value = data.token;
    refreshToken.value = data.refreshToken;
    username.value = data.username;
    role.value = data.role;
    expiresAt.value = data.expiresAt;
    localStorage.setItem('auth_token', data.token);
    localStorage.setItem('auth_refresh', data.refreshToken);
    localStorage.setItem('auth_user', JSON.stringify({ username: data.username, role: data.role, expiresAt: data.expiresAt }));
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
      if (isExpired.value) { clearAuth(); return false; }
      return true;
    } catch { clearAuth(); return false; }
  }

  function clearAuth() {
    token.value = null; refreshToken.value = null;
    username.value = ''; role.value = null; expiresAt.value = null;
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_refresh');
    localStorage.removeItem('auth_user');
  }

  return {
    token, refreshToken, username, role, expiresAt,
    isAuthenticated, isExpired, isAdmin, isAuditor, isViewer,
    hasPermission, canAccessRoute,
    login, logout, setAuth, restoreAuth, clearAuth,
  };
});
