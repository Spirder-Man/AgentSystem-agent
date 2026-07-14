/**
 * P1-4: HazardQueryPage 危化品查询页面测试
 *
 * 验证: 渲染/API 调用/结果展示/加载状态/错误状态
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { nextTick } from 'vue';

// Mock vue-router
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
}));

// Mock useLoadingBar
vi.mock('@/lib/useLoadingBar', () => ({
  useLoadingBar: () => ({ start: vi.fn(), stop: vi.fn() }),
}));

// Mock axios
const mockPost = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { post: (...args: unknown[]) => mockPost(...args) },
}));

import HazardQueryPage from '../HazardQueryPage.vue';

describe('HazardQueryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  it('应渲染页面标题和输入区', () => {
    const wrapper = mount(HazardQueryPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    expect(wrapper.text()).toContain('危化品查询');
    expect(wrapper.find('input').exists()).toBe(true);
  });

  it('空输入提交不应调用 API', async () => {
    const wrapper = mount(HazardQueryPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const queryBtn = wrapper.find('button');
    await queryBtn.trigger('click');
    await flushPromises();

    expect(mockPost).not.toHaveBeenCalled();
  });

  it('查询应调用 POST /api/Compliance/hazard/query', async () => {
    mockPost.mockResolvedValue({
      data: {
        substanceName: '苯',
        response: '苯属于易燃液体，类别2（GB 30000.7-2013）。',
        toolsUsed: ['CheckHazardCategory'],
      },
    });

    const wrapper = mount(HazardQueryPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('苯');
    const queryBtn = wrapper.find('button');
    await queryBtn.trigger('click');
    await flushPromises();

    expect(mockPost).toHaveBeenCalledWith('/api/Compliance/hazard/query', {
      substanceName: '苯',
    });
  });

  it('成功查询应展示物质名称和响应', async () => {
    mockPost.mockResolvedValue({
      data: {
        substanceName: '丙酮',
        response: '丙酮为易燃液体（GB 30000.7-2013），闪点-20°C。',
        toolsUsed: ['CheckHazardCategory'],
      },
    });

    const wrapper = mount(HazardQueryPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('丙酮');
    const queryBtn = wrapper.find('button');
    await queryBtn.trigger('click');
    await flushPromises();

    // 应展示物质名称
    const h2 = wrapper.find('h2');
    expect(h2.exists()).toBe(true);
    expect(h2.text()).toBe('丙酮');

    // 应展示工具调用信息
    expect(wrapper.text()).toContain('CheckHazardCategory');
  });

  it('API 错误应显示错误信息', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '查询处理失败' } },
    });

    const wrapper = mount(HazardQueryPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('未知物质');
    const queryBtn = wrapper.find('button');
    await queryBtn.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('查询处理失败');
  });
});
