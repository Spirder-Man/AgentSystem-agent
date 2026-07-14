/**
 * P2-8b: EmergencyPage 应急响应页面测试
 *
 * 覆盖: 渲染/场景选择/API 调用/成功展示/失败/空校验
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

import EmergencyPage from '../EmergencyPage.vue';

describe('EmergencyPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  function mountPage() {
    return mount(EmergencyPage, {
      global: { stubs: { 'EmptyState': true } },
    });
  }

  it('应渲染页面标题', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('应急响应');
  });

  it('应显示四种事故类型按钮', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('泄漏');
    expect(wrapper.text()).toContain('火灾');
    expect(wrapper.text()).toContain('爆炸');
    expect(wrapper.text()).toContain('中毒');
  });

  it('切换场景应自动填充示例数据', async () => {
    const wrapper = mountPage();
    const buttons = wrapper.findAll('button');

    // 找到火灾按钮
    const fireBtn = buttons.find(b => b.text().includes('火灾'));
    if (fireBtn) {
      await fireBtn.trigger('click');
      await flushPromises();

      // 应自动填充甲醇和储罐区2号罐
      const inputs = wrapper.findAll('input');
      if (inputs.length >= 2) {
        expect((inputs[0].element as HTMLInputElement).value).toBe('甲醇');
        expect((inputs[1].element as HTMLInputElement).value).toBe('储罐区2号罐');
      }
    }
  });

  it('空物质名不应调用 API', async () => {
    const wrapper = mountPage();
    // 清空物质名输入
    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('');

    const buttons = wrapper.findAll('button');
    const respondBtn = buttons.find(b => b.text().includes('生成应急方案'));
    if (respondBtn) {
      await respondBtn.trigger('click');
      await flushPromises();
      expect(mockPost).not.toHaveBeenCalled();
    }
  });

  it('应急响应应调用 POST /api/emergency/response', async () => {
    mockPost.mockResolvedValue({
      data: { scenario: '泄漏', substance: '苯', output: '应急方案...' },
    });

    const wrapper = mountPage();
    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('苯');

    // 可选位置
    if (inputs.length >= 2) {
      await inputs[1].setValue('甲类仓库A区');
    }

    const buttons = wrapper.findAll('button');
    const respondBtn = buttons.find(b => b.text().includes('生成应急方案'));
    if (respondBtn) {
      await respondBtn.trigger('click');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/emergency/response', {
        scenario: 'leak',
        substance: '苯',
        location: '甲类仓库A区',
      });
    }
  });

  it('成功应展示应急方案', async () => {
    mockPost.mockResolvedValue({
      data: {
        scenario: '泄漏',
        substance: '苯',
        output: '【疏散与隔离】\n1. 立即疏散无关人员\n2. 设立警戒区\n【PPE】\n穿戴防化服',
      },
    });

    const wrapper = mountPage();
    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('苯');

    const buttons = wrapper.findAll('button');
    const respondBtn = buttons.find(b => b.text().includes('生成应急方案'));
    if (respondBtn) {
      await respondBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('应急响应方案');
      expect(wrapper.text()).toContain('疏散与隔离');
    }
  });

  it('失败应显示错误', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '应急方案生成失败' } },
    });

    const wrapper = mountPage();
    const inputs = wrapper.findAll('input');
    await inputs[0].setValue('未知物质');

    const buttons = wrapper.findAll('button');
    const respondBtn = buttons.find(b => b.text().includes('生成应急方案'));
    if (respondBtn) {
      await respondBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('应急方案生成失败');
    }
  });
});
