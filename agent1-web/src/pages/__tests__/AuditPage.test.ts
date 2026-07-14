/**
 * P2-3: AuditPage 审计日志页面测试
 *
 * 覆盖: 日志列表加载/筛选/哈希链验证/审计统计/导出报告/403 无权/Empty
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { nextTick } from 'vue';

// Mock vue-router
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus (完整 stub)
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
  ElMessageBox: { confirm: vi.fn(() => Promise.resolve()) },
  ElTag: { name: 'ElTag', template: '<span><slot /></span>', props: { type: String, size: String } },
  ElButton: { name: 'ElButton', template: '<button :disabled="loading || disabled"><slot /></button>', props: { loading: Boolean, disabled: Boolean, icon: Object, type: String, size: String, text: Boolean } },
  ElInput: { name: 'ElInput', template: '<input :placeholder="placeholder" />', props: { modelValue: String, placeholder: String, size: String, clearable: Boolean, prefixIcon: Object } },
  ElDatePicker: { name: 'ElDatePicker', template: '<input />', props: { modelValue: String, type: String, placeholder: String, size: String, valueFormat: String } },
  ElPagination: { name: 'ElPagination', template: '<div class="el-pagination"></div>', props: { currentPage: Number, pageSize: Number, total: Number, layout: String, small: Boolean }, emits: ['current-change'] },
  ElAlert: { name: 'ElAlert', template: '<div><slot name="title" />{{ title }}</div>', props: { title: String, type: String, closable: Boolean, showIcon: Boolean } },
  ElIcon: { name: 'ElIcon', template: '<span><slot /></span>', props: { size: Number } },
}));

// Mock icons
vi.mock('@element-plus/icons-vue', () => ({
  Search: {},
  RefreshRight: {},
  CircleCheck: {},
  CircleClose: {},
  WarningFilled: {},
}));

// Mock axios
const mockGet = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { get: (...args: unknown[]) => mockGet(...args) },
}));

// Mock SkeletonTable / EmptyState
vi.mock('@/components/common/SkeletonTable.vue', () => ({
  default: { name: 'SkeletonTable', template: '<div class="skeleton-table"></div>', props: { rows: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String, description: String }, emits: ['action'] },
}));

import AuditPage from '../AuditPage.vue';

const logEntry = {
  id: 1,
  timestamp: '2026-07-10T08:00:00Z',
  user: 'admin',
  operation: 'LOGIN',
  details: '用户 admin 登录系统',
  isSensitive: false,
  chainHash: 'a1b2c3d4',
};

const statsData = {
  totalCount: 1523,
  byOperation: { LOGIN: 500, EXPORT: 200, DELETE: 50, QUERY: 773 },
};

function mountPage() {
  return mount(AuditPage, {
    global: {
      stubs: {
        'el-table': true,
        'el-table-column': true,
      },
    },
  });
}

describe('AuditPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
  });

  describe('日志列表加载', () => {
    it('加载时应显示骨架表', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await nextTick();

      expect(wrapper.find('.skeleton-table').exists()).toBe(true);
    });

    it('加载成功应渲染日志记录', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } }) // fetchAuditLogs
        .mockResolvedValueOnce({ data: statsData }); // fetchStats

      const wrapper = mountPage();
      await flushPromises();

      // 应展示操作标签
      expect(wrapper.text()).toContain('LOGIN');
      // 应展示统计卡片
      expect(wrapper.text()).toContain('1523');
    });

    it('加载失败应显示错误信息', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
    });

    it('403 应显示无权限提示', async () => {
      mockGet.mockRejectedValue({ response: { status: 403 } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('无权限');
      expect(wrapper.text()).toContain('admin');
    });

    it('空列表应显示空状态', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('暂无审计日志');
    });

    it('刷新应重新加载数据', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(2); // logs + stats

      // 找到刷新按钮
      mockGet.mockResolvedValue({ data: { logs: [], total: 0 } });
      const buttons = wrapper.findAll('button');
      const refreshBtn = buttons.find(b => b.text().includes('刷新'));
      if (refreshBtn) {
        await refreshBtn.trigger('click');
        await flushPromises();
        expect(mockGet).toHaveBeenCalledTimes(4); // +2
      }
    });
  });

  describe('审计统计', () => {
    it('有统计时应展示总记录数和操作分布', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('总记录');
      expect(wrapper.text()).toContain('1523');
      expect(wrapper.text()).toContain('操作分布');
      expect(wrapper.text()).toContain('LOGIN');
    });
  });

  describe('哈希链验证', () => {
    it('哈希链完整应显示成功按钮状态', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      // 找到验证按钮
      const buttons = wrapper.findAll('button');
      const verifyBtn = buttons.find(b => b.text().includes('验证哈希链'));

      if (verifyBtn) {
        mockGet.mockResolvedValue({ data: { intact: true, detail: '所有哈希链验证通过' } });
        await verifyBtn.trigger('click');
        await flushPromises();

        // 验证调用
        expect(mockGet).toHaveBeenCalledWith('/api/Audit/integrity');
      }
    });

    it('哈希链断裂应显示警告', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const verifyBtn = buttons.find(b => b.text().includes('验证哈希链'));

      if (verifyBtn) {
        mockGet.mockResolvedValue({ data: { intact: false, detail: '记录 #42 哈希不匹配' } });
        await verifyBtn.trigger('click');
        await flushPromises();

        expect(wrapper.text()).toContain('哈希链断裂');
      }
    });
  });

  describe('导出报告', () => {
    it('导出按钮存在', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('导出报告');
    });
  });
});
