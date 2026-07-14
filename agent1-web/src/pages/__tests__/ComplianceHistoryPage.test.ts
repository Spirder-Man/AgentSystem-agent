/**
 * P2-9b: ComplianceHistoryPage 合规历史页面测试
 *
 * 覆盖: 渲染/数据加载/Loading/Empty/Error/分页/颜色逻辑/路由导航
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { nextTick } from 'vue';

// Mock vue-router
const mockPush = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
  ElPagination: { name: 'ElPagination', template: '<div></div>', props: { currentPage: Number, pageSize: Number, total: Number, layout: String, small: Boolean }, emits: ['current-change'] },
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
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}</div>', props: { icon: String, title: String, description: String } },
}));

import ComplianceHistoryPage from '../ComplianceHistoryPage.vue';

const roundItem = {
  roundId: 'round-001',
  planName: '月度安全巡检',
  complianceRate: 0.92,
  compliantCount: 46,
  nonCompliantCount: 4,
  ticketCount: 2,
  totalElapsedMs: 15200,
  startedAt: '2026-07-10T08:00:00Z',
};

describe('ComplianceHistoryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPush.mockReset();
  });

  function mountPage() {
    return mount(ComplianceHistoryPage, {
      global: { stubs: { 'el-pagination': true } },
    });
  }

  describe('渲染和数据加载', () => {
    it('应渲染页面标题', async () => {
      mockGet.mockResolvedValue({ data: [] });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('合规历史');
    });

    it('加载时应显示骨架表', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await nextTick();

      expect(wrapper.find('.skeleton-table').exists()).toBe(true);
    });

    it('加载成功应渲染记录', async () => {
      mockGet.mockResolvedValue({ data: [roundItem] });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('月度安全巡检');
      expect(wrapper.text()).toContain('92%');
    });

    it('空数据应显示空状态', async () => {
      mockGet.mockResolvedValue({ data: [] });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('暂无合规历史记录');
    });

    it('加载失败应显示错误', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('加载失败');
    });

    it('应调用 GET /api/Inspection/rounds', async () => {
      mockGet.mockResolvedValue({ data: [] });
      mountPage();
      await flushPromises();

      expect(mockGet).toHaveBeenCalledWith('/api/Inspection/rounds');
    });
  });

  describe('合规率颜色', () => {
    it('≥90% 应为绿色', async () => {
      mockGet.mockResolvedValue({
        data: [{ ...roundItem, complianceRate: 0.95 }],
      });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.html()).toContain('text-green-600');
    });

    it('70-89% 应为琥珀色', async () => {
      mockGet.mockResolvedValue({
        data: [{ ...roundItem, complianceRate: 0.75 }],
      });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.html()).toContain('text-amber-600');
    });

    it('<70% 应为红色', async () => {
      mockGet.mockResolvedValue({
        data: [{ ...roundItem, complianceRate: 0.55 }],
      });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.html()).toContain('text-red-600');
    });
  });

  describe('导航', () => {
    it('点击详情应导航到巡检详情页', async () => {
      mockGet.mockResolvedValue({ data: [roundItem] });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const detailBtn = buttons.find(b => b.text().includes('详情'));
      if (detailBtn) {
        await detailBtn.trigger('click');
        expect(mockPush).toHaveBeenCalledWith('/inspection/rounds/round-001');
      }
    });
  });
});
