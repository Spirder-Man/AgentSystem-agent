/**
 * P1-2: Auth Store 单元测试
 *
 * 覆盖: login/logout/setAuth/clearAuth/restoreAuth/角色计算/hasPermission/canAccessRoute
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';

// Mock axios
const mockPost = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { post: (...args: unknown[]) => mockPost(...args) },
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { error: vi.fn() },
}));

import { useAuthStore } from '@/stores/auth';

function createStore() {
  setActivePinia(createPinia());
  return useAuthStore();
}

describe('Auth Store', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  describe('初始状态', () => {
    it('未登录时 token/role 应为 null', () => {
      const auth = createStore();
      expect(auth.token).toBeNull();
      expect(auth.role).toBeNull();
      expect(auth.username).toBe('');
      expect(auth.isAuthenticated).toBe(false);
    });
  });

  describe('setAuth', () => {
    it('应正确设置认证状态并持久化到 localStorage', () => {
      const auth = createStore();
      const futureDate = new Date(Date.now() + 3600_000).toISOString();

      auth.setAuth({
        token: 'test-token-123',
        refreshToken: 'refresh-456',
        username: 'admin',
        role: 'admin',
        expiresAt: futureDate,
      });

      expect(auth.token).toBe('test-token-123');
      expect(auth.username).toBe('admin');
      expect(auth.role).toBe('admin');
      expect(auth.isAuthenticated).toBe(true);
      expect(localStorage.getItem('auth_token')).toBe('test-token-123');
    });
  });

  describe('clearAuth', () => {
    it('应清除所有状态和 localStorage', () => {
      const auth = createStore();
      auth.setAuth({
        token: 't', refreshToken: 'r', username: 'u',
        role: 'admin', expiresAt: new Date(Date.now() + 3600_000).toISOString(),
      });

      auth.clearAuth();

      expect(auth.token).toBeNull();
      expect(auth.role).toBeNull();
      expect(auth.username).toBe('');
      expect(localStorage.getItem('auth_token')).toBeNull();
    });
  });

  describe('角色计算属性', () => {
    it('isAdmin 仅在 role=admin 时为 true', () => {
      const auth = createStore();
      auth.role = 'admin';
      expect(auth.isAdmin).toBe(true);
      expect(auth.isAuditor).toBe(false);
      expect(auth.isViewer).toBe(false);
    });

    it('isAuditor 仅在 role=auditor 时为 true', () => {
      const auth = createStore();
      auth.role = 'auditor';
      expect(auth.isAdmin).toBe(false);
      expect(auth.isAuditor).toBe(true);
      expect(auth.isViewer).toBe(false);
    });

    it('isViewer 仅在 role=viewer 时为 true', () => {
      const auth = createStore();
      auth.role = 'viewer';
      expect(auth.isAdmin).toBe(false);
      expect(auth.isAuditor).toBe(false);
      expect(auth.isViewer).toBe(true);
    });
  });

  describe('isExpired', () => {
    it('未设置 expiresAt 时应视为过期', () => {
      const auth = createStore();
      auth.token = 'token';
      auth.role = 'viewer';
      expect(auth.isExpired).toBe(true);
    });

    it('过去的时间应视为过期', () => {
      const auth = createStore();
      auth.token = 'token';
      auth.role = 'viewer';
      auth.expiresAt = new Date('2020-01-01').toISOString();
      expect(auth.isExpired).toBe(true);
    });

    it('未来的时间不应过期', () => {
      const auth = createStore();
      auth.token = 'token';
      auth.role = 'viewer';
      auth.expiresAt = new Date(Date.now() + 3600_000).toISOString();
      expect(auth.isExpired).toBe(false);
    });
  });

  describe('hasPermission', () => {
    it('未认证时返回 false', () => {
      const auth = createStore();
      expect(auth.hasPermission(['viewer'])).toBe(false);
    });

    it('admin 对所有角色返回 true', () => {
      const auth = createStore();
      auth.token = 't';
      auth.role = 'admin';
      auth.expiresAt = new Date(Date.now() + 3600_000).toISOString();
      expect(auth.hasPermission(['admin'])).toBe(true);
      expect(auth.hasPermission(['auditor'])).toBe(true);
      expect(auth.hasPermission(['viewer'])).toBe(true);
    });

    it('auditor 对 auditor/viewer 返回 true，对 admin 返回 false', () => {
      const auth = createStore();
      auth.token = 't';
      auth.role = 'auditor';
      auth.expiresAt = new Date(Date.now() + 3600_000).toISOString();
      expect(auth.hasPermission(['auditor'])).toBe(true);
      expect(auth.hasPermission(['viewer'])).toBe(true);
      expect(auth.hasPermission(['admin'])).toBe(false);
    });

    it('viewer 仅对 viewer 返回 true', () => {
      const auth = createStore();
      auth.token = 't';
      auth.role = 'viewer';
      auth.expiresAt = new Date(Date.now() + 3600_000).toISOString();
      expect(auth.hasPermission(['viewer'])).toBe(true);
      expect(auth.hasPermission(['auditor'])).toBe(false);
      expect(auth.hasPermission(['admin'])).toBe(false);
    });
  });

  describe('canAccessRoute', () => {
    it('无 requiredRoles 时返回 true', () => {
      const auth = createStore();
      expect(auth.canAccessRoute()).toBe(true);
      expect(auth.canAccessRoute([])).toBe(true);
    });
  });

  describe('restoreAuth', () => {
    it('从 localStorage 恢复认证状态', () => {
      const futureDate = new Date(Date.now() + 3600_000).toISOString();
      localStorage.setItem('auth_token', 'restored-token');
      localStorage.setItem('auth_refresh', 'restored-refresh');
      localStorage.setItem('auth_user', JSON.stringify({
        username: 'restored-admin', role: 'admin', expiresAt: futureDate,
      }));

      const auth = createStore();
      const restored = auth.restoreAuth();

      expect(restored).toBe(true);
      expect(auth.token).toBe('restored-token');
      expect(auth.username).toBe('restored-admin');
      expect(auth.role).toBe('admin');
    });

    it('无 localStorage 数据时返回 false', () => {
      const auth = createStore();
      expect(auth.restoreAuth()).toBe(false);
    });

    it('token 已过期时清除并返回 false', () => {
      localStorage.setItem('auth_token', 'expired-token');
      localStorage.setItem('auth_user', JSON.stringify({
        username: 'u', role: 'viewer', expiresAt: '2020-01-01T00:00:00Z',
      }));

      const auth = createStore();
      const restored = auth.restoreAuth();

      expect(restored).toBe(false);
      expect(auth.token).toBeNull();
      expect(localStorage.getItem('auth_token')).toBeNull();
    });
  });

  describe('login', () => {
    it('登录成功应设置状态', async () => {
      const auth = createStore();
      mockPost.mockResolvedValue({
        data: {
          token: 'login-token',
          refreshToken: 'login-refresh',
          username: 'login-admin',
          role: 'admin',
          expiresAt: new Date(Date.now() + 3600_000).toISOString(),
        },
      });

      const result = await auth.login({ username: 'admin', password: 'pass' });

      expect(result).toBe(true);
      expect(auth.token).toBe('login-token');
      expect(auth.role).toBe('admin');
      expect(mockPost).toHaveBeenCalledWith('/api/Auth/login', {
        username: 'admin', password: 'pass',
      });
    });

    it('登录失败应返回 false', async () => {
      const auth = createStore();
      mockPost.mockRejectedValue({ response: { data: { error: 'Bad credentials' } } });

      const result = await auth.login({ username: 'admin', password: 'wrong' });

      expect(result).toBe(false);
      expect(auth.token).toBeNull();
    });
  });

  describe('logout', () => {
    it('应清除认证并调用 API', async () => {
      const auth = createStore();
      auth.setAuth({
        token: 't', refreshToken: 'r', username: 'u',
        role: 'admin', expiresAt: new Date(Date.now() + 3600_000).toISOString(),
      });
      mockPost.mockResolvedValue({ data: {} });

      await auth.logout();

      expect(auth.token).toBeNull();
      expect(mockPost).toHaveBeenCalledWith('/api/Auth/logout');
    });
  });
});
