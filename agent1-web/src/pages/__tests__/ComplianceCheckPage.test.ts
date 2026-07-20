/**
 * P1-3: ComplianceCheckPage 合规检查页面测试
 *
 * 验证: 渲染/空提交校验/API 调用路径/加载状态/成功响应展示/错误展示
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

import ComplianceCheckPage from '../ComplianceCheckPage.vue';

describe('ComplianceCheckPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  it('应渲染页面标题和输入区', () => {
    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    expect(wrapper.text()).toContain('合规检查');
    expect(wrapper.find('input').exists()).toBe(true);
  });

  it('空输入提交不应调用 API', async () => {
    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const submitBtn = wrapper.find('button');
    await submitBtn.trigger('click');
    await flushPromises();

    // 空输入时不应调用 API
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('提交应调用 POST /api/Compliance/check', async () => {
    mockPost.mockResolvedValue({
      data: {
        query: '苯的危险类别',
        response: '苯属于易燃液体',
        toolsUsed: ['CheckHazardCategory'],
        verifiedRegulations: ['GB 30000.7-2013'],
        hallucinatedRegulations: [],
        warnings: [],
      },
    });

    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    // 设置输入值
    const input = wrapper.find('input');
    await input.setValue('苯的危险类别');

    // 点击提交
    const submitBtn = wrapper.find('button');
    await submitBtn.trigger('click');
    await flushPromises();

    expect(mockPost).toHaveBeenCalledWith('/api/Compliance/check', {
      query: '苯的危险类别',
    });
  });

  it('加载中应显示 AI 分析状态', async () => {
    // 永不 resolve 来保持 loading 状态
    mockPost.mockReturnValue(new Promise(() => {}));

    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('测试查询');
    const submitBtn = wrapper.find('button');
    await submitBtn.trigger('click');
    await nextTick();

    // 按钮应显示 loading 文本
    expect(submitBtn.text()).toContain('AI 分析中');
  });

  it('成功响应应展示法规引用和警告标签', async () => {
    mockPost.mockResolvedValue({
      data: {
        query: '苯和丙酮能同库储存吗',
        response: '经核查，苯与丙酮不得同库储存。详见 GB 15603。',
        toolsUsed: ['CheckStorageCompatibility', 'CheckHazardCategory'],
        verifiedRegulations: ['GB 15603-1995 §4.2.2'],
        hallucinatedRegulations: [],
        warnings: ['苯为易燃液体，需加强通风'],
      },
    });

    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('苯和丙酮能同库储存吗');
    const submitBtn = wrapper.find('button');
    await submitBtn.trigger('click');
    await flushPromises();

    // 结果区域应有审核结论
    expect(wrapper.text()).toContain('审核结论');
    // 应有法规引用标签
    expect(wrapper.text()).toContain('法规引用');
    // 应有安全警告标签
    expect(wrapper.text()).toContain('安全警告');
  });

  it('API 错误应显示错误信息', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '服务繁忙，请稍后重试', retryAfter: 10 } },
    });

    const wrapper = mount(ComplianceCheckPage, {
      global: { stubs: { 'EmptyState': true } },
    });

    const input = wrapper.find('input');
    await input.setValue('测试');
    const submitBtn = wrapper.find('button');
    await submitBtn.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('服务繁忙');
    expect(wrapper.text()).toContain('10');
  });
});
