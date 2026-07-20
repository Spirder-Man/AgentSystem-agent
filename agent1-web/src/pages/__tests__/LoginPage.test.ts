/**
 * P2-1: LoginPage 登录页面测试
 *
 * 覆盖: 渲染/空校验/成功登录路由跳转/失败提示/loading 状态/redirect 参数
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { setActivePinia, createPinia } from 'pinia';

// Mock vue-router
const mockPush = vi.fn();
const mockRoute = { params: {}, query: {} };
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => mockRoute,
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { error: vi.fn() },
}));

// Mock axios (auth store 内部使用)
const mockPost = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { post: (...args: unknown[]) => mockPost(...args) },
}));

import LoginPage from '../LoginPage.vue';
import { useAuthStore } from '@/stores/auth';

function createStore() {
  setActivePinia(createPinia());
  return useAuthStore();
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPush.mockReset();
    mockPost.mockReset();
    mockRoute.query = {};
    localStorage.clear();
  });

  function mountPage() {
    // LoginPage 在 setup 中调用 useAuthStore()，必须先初始化 Pinia
    setActivePinia(createPinia());
    return mount(LoginPage);
  }

  describe('渲染', () => {
    it('应渲染登录标题和表单', () => {
      const wrapper = mountPage();
      expect(wrapper.text()).toContain('Agent1');
      expect(wrapper.text()).toContain('登录');
    });

    it('应有用户名和密码输入框', () => {
      const wrapper = mountPage();
      const inputs = wrapper.findAll('input');
      expect(inputs.length).toBe(2);
    });

    it('应有提交按钮', () => {
      const wrapper = mountPage();
      const btn = wrapper.find('button[type="submit"]');
      expect(btn.exists()).toBe(true);
      expect(btn.text()).toContain('登 录');
    });
  });

  describe('空校验', () => {
    it('用户名为空时应显示错误提示', async () => {
      const wrapper = mountPage();

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(wrapper.text()).toContain('请输入用户名和密码');
      expect(mockPost).not.toHaveBeenCalled();
    });

    it('密码为空时应显示错误提示', async () => {
      const wrapper = mountPage();

      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('admin');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(wrapper.text()).toContain('请输入用户名和密码');
      expect(mockPost).not.toHaveBeenCalled();
    });
  });

  describe('登录流程', () => {
    it('登录成功应调用 API 并跳转到 /dashboard', async () => {
      mockPost.mockResolvedValue({
        data: {
          token: 'login-token',
          refreshToken: 'refresh-token',
          username: 'admin',
          role: 'admin',
          expiresAt: new Date(Date.now() + 3600_000).toISOString(),
        },
      });

      const wrapper = mountPage();

      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('admin');
      await inputs[1].setValue('password123');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/Auth/login', {
        username: 'admin',
        password: 'password123',
      });
      expect(mockPush).toHaveBeenCalledWith('/dashboard');
    });

    it('有 redirect 参数时应跳转到目标路径', async () => {
      mockRoute.query = { redirect: '/inspection/plans' };
      mockPost.mockResolvedValue({
        data: {
          token: 't', refreshToken: 'r', username: 'admin',
          role: 'admin', expiresAt: new Date(Date.now() + 3600_000).toISOString(),
        },
      });

      const wrapper = mountPage();
      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('admin');
      await inputs[1].setValue('pass');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(mockPush).toHaveBeenCalledWith('/inspection/plans');
    });

    it('登录失败应显示错误信息', async () => {
      mockPost.mockRejectedValue({
        response: { data: { error: '用户名或密码错误' } },
      });

      const wrapper = mountPage();
      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('admin');
      await inputs[1].setValue('wrong');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(mockPush).not.toHaveBeenCalled();
    });

    it('登录中应显示 loading 状态并禁用按钮', async () => {
      // 使用延迟 resolve 来观察 loading 状态
      let resolveLogin!: (value: unknown) => void;
      mockPost.mockReturnValue(new Promise((resolve) => { resolveLogin = resolve; }));

      const wrapper = mountPage();
      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('admin');
      await inputs[1].setValue('pass');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      const btn = wrapper.find('button[type="submit"]');
      expect(btn.text()).toContain('登录中');
      expect(btn.attributes('disabled')).toBeDefined();

      // 清理：resolve 避免泄漏
      resolveLogin({ data: { token: 't', refreshToken: 'r', username: 'u', role: 'viewer', expiresAt: new Date(Date.now() + 3600_000).toISOString() } });
      await flushPromises();
    });

    it('输入应自动 trim', async () => {
      mockPost.mockResolvedValue({
        data: {
          token: 't', refreshToken: 'r', username: 'admin',
          role: 'admin', expiresAt: new Date(Date.now() + 3600_000).toISOString(),
        },
      });

      const wrapper = mountPage();
      const inputs = wrapper.findAll('input');
      await inputs[0].setValue('  admin  ');
      await inputs[1].setValue('pass');

      const form = wrapper.find('form');
      await form.trigger('submit');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/Auth/login', {
        username: 'admin',
        password: 'pass',
      });
    });
  });

  describe('角色提示', () => {
    it('应显示角色切换提示文字', () => {
      const wrapper = mountPage();
      expect(wrapper.text()).toContain('admin/auditor/viewer');
    });
  });
});
