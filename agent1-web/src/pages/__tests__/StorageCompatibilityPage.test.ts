/**
 * P1-5: StorageCompatibilityPage 储存兼容性检查页面测试
 *
 * 验证: 渲染/双输入字段/API 调用/兼容性结果展示/错误状态
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

// Mock useLoadingBar
vi.mock('@/lib/useLoadingBar', () => ({
  useLoadingBar: () => ({ start: vi.fn(), stop: vi.fn() }),
}));

// Mock axios
const mockPost = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { post: (...args: unknown[]) => mockPost(...args) },
}));

import StorageCompatibilityPage from '../StorageCompatibilityPage.vue';

describe('StorageCompatibilityPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  it('应渲染页面标题和两个输入字段', () => {
    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    expect(wrapper.text()).toContain('储存兼容性检查');

    // 应有两个输入字段
    const inputs = wrapper.findAll('input');
    expect(inputs.length).toBeGreaterThanOrEqual(2);
  });

  it('输入字段有 A/B 标签', () => {
    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    expect(wrapper.text()).toContain('化学品 A');
    expect(wrapper.text()).toContain('化学品 B');
  });

  it('任一输入为空不应调用 API', async () => {
    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    // 只填 A 不填 B
    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('苯');

    const checkBtn = wrapper.find('button');
    await checkBtn.trigger('click');
    await flushPromises();

    expect(mockPost).not.toHaveBeenCalled();
  });

  it('检查应调用 POST /api/Compliance/storage/compatibility', async () => {
    mockPost.mockResolvedValue({
      data: {
        substanceA: '苯',
        substanceB: '丙酮',
        response: '苯与丙酮可同库储存，但需注意通风。',
        toolsUsed: ['CheckStorageCompatibility'],
      },
    });

    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('苯');
    await inputs[1].setValue('丙酮');

    const checkBtn = wrapper.find('button');
    await checkBtn.trigger('click');
    await flushPromises();

    expect(mockPost).toHaveBeenCalledWith('/api/Compliance/storage/compatibility', {
      substanceA: '苯',
      substanceB: '丙酮',
    });
  });

  it('成功响应应展示 A vs B 标题和工具信息', async () => {
    mockPost.mockResolvedValue({
      data: {
        substanceA: '硝酸',
        substanceB: '硫酸',
        response: '硝酸与硫酸可同库储存，需分开放置。',
        toolsUsed: ['CheckStorageCompatibility'],
      },
    });

    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('硝酸');
    await inputs[1].setValue('硫酸');

    const checkBtn = wrapper.find('button');
    await checkBtn.trigger('click');
    await flushPromises();

    // 应展示 A vs B
    const h2 = wrapper.find('h2');
    expect(h2.exists()).toBe(true);
    expect(h2.text()).toContain('硝酸');
    expect(h2.text()).toContain('硫酸');

    // 应展示工具信息
    expect(wrapper.text()).toContain('CheckStorageCompatibility');
  });

  it('API 错误应显示错误信息', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '兼容性检查失败' } },
    });

    const wrapper = mount(StorageCompatibilityPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('苯');
    await inputs[1].setValue('丙酮');
    const checkBtn = wrapper.find('button');
    await checkBtn.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('兼容性检查失败');
  });
});
