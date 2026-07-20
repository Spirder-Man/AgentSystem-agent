/**
 * P2-1: DatabaseDiagnosticsPage 数据库诊断页面测试
 *
 * 覆盖: 加载态/数据库信息渲染/表列表/连接验证/验证结果/
 *       验证错误/错误重试/刷新按钮
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';

// Mock vue-router
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
}));

// Mock axios
const mockGet = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: {
    get: (...args: unknown[]) => mockGet(...args),
  },
}));

// Mock SkeletonCard / EmptyState
vi.mock('@/components/common/SkeletonCard.vue', () => ({
  default: { name: 'SkeletonCard', template: '<div class="skeleton-card"><slot /></div>', props: { count: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button class="retry-btn" @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String }, emits: ['action'] },
}));

import DatabaseDiagnosticsPage from '../DatabaseDiagnosticsPage.vue';

// ── Mock 数据 ──

const dbInfoData = {
  info: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', version: 'PostgreSQL 16.3' },
  tables: ['chemical_substances', 'chemical_substance_categories', 'audit_log', 'inspection_plans'],
  retrievedAt: '2026-07-10T10:00:00Z',
};

const validateResultData = {
  connected: true,
  server: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', user: 'agent_admin' },
  info: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', version: 'PostgreSQL 16.3' },
  tableCount: 10,
  tables: ['chemical_substances', 'audit_log', 'inspection_plans'],
  elapsedMs: 35,
  verifiedAt: '2026-07-10T10:01:00Z',
};

function mountPage() {
  return mount(DatabaseDiagnosticsPage, {
    global: {
      stubs: {
        'el-button': { template: '<button :disabled="$attrs.disabled" @click="$emit(\'click\')"><slot /></button>', inheritAttrs: true },
        'el-icon': { template: '<i class="el-icon"><slot /></i>' },
      },
    },
  });
}

describe('DatabaseDiagnosticsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
  });

  // ═══════════════════════════════════
  // 加载与错误
  // ═══════════════════════════════════

  describe('加载态', () => {
    it('挂载时应显示骨架屏', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.skeleton-card').exists()).toBe(true);
    });

    it('加载完成后隐藏骨架屏', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.skeleton-card').exists()).toBe(false);
    });
  });

  describe('错误态', () => {
    it('API 失败应显示错误信息', async () => {
      mockGet.mockRejectedValue(new Error('Network Error'));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('加载数据库信息失败');
    });

    it('点击重试应重新加载', async () => {
      mockGet.mockRejectedValueOnce(new Error('Fail'));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('加载数据库信息失败');

      mockGet.mockResolvedValueOnce({ data: dbInfoData });
      const retryBtn = wrapper.find('.retry-btn');
      await retryBtn.trigger('click');
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(false);
      expect(wrapper.text()).toContain('PostgreSQL');
    });
  });

  // ═══════════════════════════════════
  // 数据库信息
  // ═══════════════════════════════════

  describe('数据库信息渲染', () => {
    it('应调用 GET /api/Admin/db/info', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      mountPage();
      await flushPromises();
      expect(mockGet).toHaveBeenCalledWith('/api/Admin/db/info', undefined);
    });

    it('应显示主机、端口、数据库名、版本', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('localhost');
      expect(wrapper.text()).toContain('5432');
      expect(wrapper.text()).toContain('chemical_park_ai_agent');
      expect(wrapper.text()).toContain('PostgreSQL 16.3');
    });

    it('应显示表数量和表名列表', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('4'); // table count
      expect(wrapper.text()).toContain('chemical_substances');
      expect(wrapper.text()).toContain('audit_log');
    });

    it('应显示刷新按钮', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('刷新');
    });
  });

  // ═══════════════════════════════════
  // 连接验证
  // ═══════════════════════════════════

  describe('连接验证', () => {
    it('应显示执行验证按钮', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('执行连接验证');
    });

    it('点击应调用 GET /api/Admin/db/validate', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Admin/db/info') return Promise.resolve({ data: dbInfoData });
        if (url === '/api/Admin/db/validate') return Promise.resolve({ data: validateResultData });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const validateBtn = buttons.find(b => b.text().includes('执行连接验证'));
      expect(validateBtn).toBeTruthy();
      await validateBtn!.trigger('click');
      await flushPromises();

      expect(mockGet).toHaveBeenCalledWith('/api/Admin/db/validate', undefined);
    });

    it('验证成功应显示连接正常状态', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Admin/db/info') return Promise.resolve({ data: dbInfoData });
        if (url === '/api/Admin/db/validate') return Promise.resolve({ data: validateResultData });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const validateBtn = buttons.find(b => b.text().includes('执行连接验证'));
      await validateBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('连接正常');
      expect(wrapper.text()).toContain('35ms');
      expect(wrapper.text()).toContain('agent_admin');
      expect(wrapper.text()).toContain('10'); // tableCount
    });

    it('验证成功应显示 ElMessage 成功提示', async () => {
      const { ElMessage } = await import('element-plus');
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Admin/db/info') return Promise.resolve({ data: dbInfoData });
        if (url === '/api/Admin/db/validate') return Promise.resolve({ data: validateResultData });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const validateBtn = buttons.find(b => b.text().includes('执行连接验证'));
      await validateBtn!.trigger('click');
      await flushPromises();

      expect(ElMessage.success).toHaveBeenCalledWith(expect.stringContaining('35ms'));
    });

    it('验证失败应显示错误提示', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Admin/db/info') return Promise.resolve({ data: dbInfoData });
        if (url === '/api/Admin/db/validate') return Promise.reject(new Error('Timeout'));
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const validateBtn = buttons.find(b => b.text().includes('执行连接验证'));
      await validateBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('数据库验证失败');
    });
  });

  // ═══════════════════════════════════
  // 页面标题
  // ═══════════════════════════════════

  describe('页面标题', () => {
    it('应显示数据库诊断标题', async () => {
      mockGet.mockResolvedValue({ data: dbInfoData });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('数据库诊断');
      expect(wrapper.text()).toContain('Admin only');
    });
  });
});
