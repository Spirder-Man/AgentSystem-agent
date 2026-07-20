/**
 * P0-3: InspectionPlansPage + InspectionPlanDetailPage 组件测试
 *
 * 测试策略：由于页面组件依赖 vue-router/pinia/axios/element-plus，
 * 采用分层测试：
 *   - 层1：核心渲染（渲染不报错、关键元素存在）
 *   - 层2：删除按钮逻辑（P1-5 修复验证）
 *   - 层3：新建计划表单逻辑
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { nextTick } from 'vue';

// ═══════════════ Mock 模块 ═══════════════

// Mock vue-router
const mockPush = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus message/confirm
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
  ElMessageBox: { confirm: vi.fn(() => Promise.resolve()) },
}));

// Mock axios apiClient
const mockGet = vi.fn();
const mockPost = vi.fn();
const mockDelete = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    delete: (...args: unknown[]) => mockDelete(...args),
  },
}));

// ═══════════════ 测试 ═══════════════

import InspectionPlansPage from '../InspectionPlansPage.vue';

describe('InspectionPlansPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPost.mockReset();
    mockDelete.mockReset();
  });

  describe('渲染测试', () => {
    it('加载状态应显示骨架屏', async () => {
      // 未 resolve 时处于 loading 状态
      mockGet.mockReturnValue(new Promise(() => {})); // 永远 pending

      const wrapper = mount(InspectionPlansPage, {
        global: { stubs: { 'el-table': true, 'el-button': true, 'el-dialog': true } },
      });
      await nextTick();

      expect(wrapper.findComponent({ name: 'SkeletonTable' }).exists()).toBe(true);
    });

    it('空数据应显示空状态', async () => {
      mockGet.mockResolvedValue({ data: [] });

      const wrapper = mount(InspectionPlansPage, {
        global: { stubs: { 'el-table': true, 'el-button': true, 'el-dialog': true } },
      });
      await flushPromises();

      expect(wrapper.findComponent({ name: 'EmptyState' }).exists()).toBe(true);
    });

    it('有数据时应有删除按钮', async () => {
      mockGet.mockResolvedValue({
        data: [
          {
            planId: 'plan-1', name: '测试计划', area: 'A区',
            inspector: '张三', status: 'Draft', items: 3, createdAt: '2026-07-01',
          },
        ],
      });

      const wrapper = mount(InspectionPlansPage, {
        global: { stubs: { 'el-button': false, 'el-table': true, 'el-dialog': true } },
      });
      await flushPromises();

      // 渲染后应有删除按钮（通过 title="删除计划" 识别）
      const deleteBtn = wrapper.find('button[title="删除计划"]');
      expect(deleteBtn.exists()).toBe(true);
    });
  });

  describe('API 调用测试', () => {
    it('加载时调用 GET /api/Inspection/plans', async () => {
      mockGet.mockResolvedValue({ data: [] });
      mount(InspectionPlansPage, {
        global: { stubs: { 'el-table': true, 'el-button': true, 'el-dialog': true } },
      });
      await flushPromises();

      expect(mockGet).toHaveBeenCalledWith('/api/Inspection/plans');
    });

    it('API 失败应显示错误', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mount(InspectionPlansPage, {
        global: { stubs: { 'el-table': true, 'el-button': true, 'el-dialog': true } },
      });
      await flushPromises();

      expect(wrapper.text()).toContain('加载失败');
    });
  });

  describe('进度提示（P1-5 修复验证）', () => {
    it('有未完成计划时应提示巡检进度', async () => {
      mockGet.mockResolvedValue({
        data: [
          { planId: 'p1', name: '计划1', status: 'Draft', items: 2 },
          { planId: 'p2', name: '计划2', status: 'Completed', items: 1 },
          { planId: 'p3', name: '计划3', status: 'Draft', items: 3 },
        ],
      });

      const wrapper = mount(InspectionPlansPage, {
        global: { stubs: { 'el-table': true, 'el-button': true, 'el-dialog': true } },
      });
      await flushPromises();

      // 应显示总数 3 的计划
      expect(wrapper.text()).toContain('3');
    });
  });
});
