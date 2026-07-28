/**
 * P2-8a: KnowledgeGraphPage 知识图谱页面测试
 *
 * 覆盖: 渲染/API 调用/成功展示/失败/预设
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

import KnowledgeGraphPage from '../KnowledgeGraphPage.vue';

describe('KnowledgeGraphPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  function mountPage() {
    return mount(KnowledgeGraphPage, {
      global: { stubs: { EmptyState: true } },
    });
  }

  it('应渲染页面标题', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('知识图谱');
  });

  it('空查询不应调用 API', async () => {
    const wrapper = mountPage();
    const buttons = wrapper.findAll('button');
    const queryBtn = buttons.find((b) => b.text().includes('查询'));
    if (queryBtn) {
      await queryBtn.trigger('click');
      await flushPromises();
      expect(mockPost).not.toHaveBeenCalled();
    }
  });

  it('查询应调用 POST /api/KnowledgeGraph/query', async () => {
    mockPost.mockResolvedValue({
      data: { query: '苯的关联法规和事故案例', output: '查询结果...' },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('苯的关联法规和事故案例');

    const buttons = wrapper.findAll('button');
    const queryBtn = buttons.find((b) => b.text().includes('查询'));
    if (queryBtn) {
      await queryBtn.trigger('click');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/KnowledgeGraph/query', {
        query: '苯的关联法规和事故案例',
      });
    }
  });

  it('成功应展示图谱查询结果', async () => {
    mockPost.mockResolvedValue({
      data: {
        query: '苯的关联法规和事故案例',
        output: '实体: 苯 (CAS 71-43-2)\n关联法规: GB 30000.7-2013\n关联事故: 2起',
      },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('苯的关联法规和事故案例');

    const buttons = wrapper.findAll('button');
    const queryBtn = buttons.find((b) => b.text().includes('查询'));
    if (queryBtn) {
      await queryBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('图谱查询结果');
      expect(wrapper.text()).toContain('GB 30000.7-2013');
    }
  });

  it('失败应显示错误', async () => {
    mockPost.mockRejectedValue({
      response: { data: { error: '图谱查询失败' } },
    });

    const wrapper = mountPage();
    const input = wrapper.find('input');
    await input.setValue('测试');

    const buttons = wrapper.findAll('button');
    const queryBtn = buttons.find((b) => b.text().includes('查询'));
    if (queryBtn) {
      await queryBtn.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('图谱查询失败');
    }
  });

  it('应显示预设查询按钮', () => {
    const wrapper = mountPage();
    expect(wrapper.text()).toContain('苯的关联法规和事故案例');
    expect(wrapper.text()).toContain('甲类仓库涉及哪些化学品和处罚记录');
  });
});
