/**
 * P2-7: RegulatoryAuditPage 法规审计页面测试
 *
 * 覆盖: 渲染/空校验/API 调用/成功展示/失败展示/预设按钮
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

import RegulatoryAuditPage from '../RegulatoryAuditPage.vue';

describe('RegulatoryAuditPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  function mountPage() {
    return mount(RegulatoryAuditPage, {
      global: { stubs: { 'EmptyState': true } },
    });
  }

  it('应渲染页面标题', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('法规审计');
  });

  it('空查询不应调用 API', async () => {
    const wrapper = mountPage();
    const buttons = wrapper.findAll('button');
    const auditBtn = buttons.find(b => b.text().includes('执行审计'));
    if (auditBtn) {
      await auditBtn.trigger('click');
      await flushPromises();
      expect(mockPost).not.toHaveBeenCalled();
    }
  });

  it('审计应调用 POST /api/regulatory/audit', async () => {
    mockPost.mockResolvedValue({
      data: { query: '审查甲类仓库', output: '合规检查结果...' },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('审查甲类仓库的消防安全合规性');

    const buttons = wrapper.findAll('button');
    const auditBtn = buttons.find(b => b.text().includes('执行审计'));
    if (auditBtn) {
      await auditBtn.trigger('click');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/regulatory/audit', {
        query: '审查甲类仓库的消防安全合规性',
      });
    }
  });

  it('成功应展示审计结果', async () => {
    mockPost.mockResolvedValue({
      data: {
        query: '检查危化品储存区',
        output: '经审查，危化品储存区存在以下不合规项：\n1. 缺少防泄漏围堰\n2. 通风设施不足',
      },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('检查危化品储存区');

    const buttons = wrapper.findAll('button');
    const auditBtn = buttons.find(b => b.text().includes('执行审计'));
    if (auditBtn) {
      await auditBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('审计结果');
      expect(wrapper.text()).toContain('缺少防泄漏围堰');
    }
  });

  it('失败应显示错误信息', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '审计服务不可用' } },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('测试审计');

    const buttons = wrapper.findAll('button');
    const auditBtn = buttons.find(b => b.text().includes('执行审计'));
    if (auditBtn) {
      await auditBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('审计服务不可用');
    }
  });

  it('应显示预设审计项按钮', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('审查甲类仓库的消防安全合规性');
    expect(wrapper.text()).toContain('检查危化品储存区的防泄漏措施');
  });

  it('Enter 键应触发审计', async () => {
    mockPost.mockResolvedValue({
      data: { query: '测试', output: '结果' },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('测试审计内容');
    await input.trigger('keyup.enter');
    await flushPromises();

    expect(mockPost).toHaveBeenCalledWith('/api/regulatory/audit', {
      query: '测试审计内容',
    });
  });
});
