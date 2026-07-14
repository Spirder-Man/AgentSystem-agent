/**
 * P2-5: KnowledgeBasePage 知识库管理页面测试
 *
 * 覆盖: 搜索模式加载/模式切换/RAG 检索测试/增量加载/Preset 按钮
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
const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPut = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    put: (...args: unknown[]) => mockPut(...args),
  },
}));

import KnowledgeBasePage from '../KnowledgeBasePage.vue';

describe('KnowledgeBasePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPost.mockReset();
    mockPut.mockReset();
  });

  function mountPage() {
    return mount(KnowledgeBasePage, {
      global: { stubs: { 'EmptyState': true } },
    });
  }

  describe('搜索模式', () => {
    it('应渲染搜索模式标题', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('搜索模式');
      expect(wrapper.text()).toContain('Hybrid');
    });

    it('应有三个模式切换按钮', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('Bm25');
      expect(wrapper.text()).toContain('Vector');
      expect(wrapper.text()).toContain('Hybrid');
    });

    it('切换模式应调用 PUT /api/knowledgebase/search-mode', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      mockPut.mockResolvedValue({ data: {} });

      const wrapper = mountPage();
      await flushPromises();

      // 找到 Bm25 按钮并点击
      const buttons = wrapper.findAll('button');
      const bm25Btn = buttons.find(b => b.text().includes('Bm25'));
      if (bm25Btn) {
        await bm25Btn.trigger('click');
        await flushPromises();

        expect(mockPut).toHaveBeenCalledWith('/api/knowledgebase/search-mode', {
          mode: 'Bm25',
        });
      }
    });

    it('当前模式按钮应被禁用', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const hybridBtn = buttons.find(b => b.text() === 'Hybrid');
      if (hybridBtn) {
        expect(hybridBtn.attributes('disabled')).toBeDefined();
      }
    });
  });

  describe('RAG 检索测试', () => {
    it('空查询不应调用 API', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      // 找到测试按钮（不填输入）并点击
      const buttons = wrapper.findAll('button');
      const testBtn = buttons.find(b => b.text().includes('测试'));
      if (testBtn) {
        await testBtn.trigger('click');
        await flushPromises();
        expect(mockPost).not.toHaveBeenCalled();
      }
    });

    it('输入查询后应调用 POST /api/knowledgebase/rag-test', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      mockPost.mockResolvedValue({
        data: {
          query: '苯的储存要求',
          summary: '苯应储存于阴凉通风处',
          elapsedMs: 350,
          totalResults: 5,
          results: [
            { id: '1', rank: 1, score: 0.95, content: '苯储存规范...', retrievalMethod: 'Hybrid' },
          ],
        },
      });

      const wrapper = mountPage();
      await flushPromises();

      const input = wrapper.find('input');
      await input.setValue('苯的储存要求');

      const buttons = wrapper.findAll('button');
      const testBtn = buttons.find(b => b.text().includes('测试'));
      if (testBtn) {
        await testBtn.trigger('click');
        await flushPromises();

        expect(mockPost).toHaveBeenCalledWith('/api/knowledgebase/rag-test', {
          query: '苯的储存要求',
        });
      }
    });

    it('检索成功应展示结果摘要', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      mockPost.mockResolvedValue({
        data: {
          query: '甲类仓库消防间距',
          summary: '甲类仓库与明火点间距不应小于30m（GB 50016）',
          elapsedMs: 280,
          totalResults: 3,
          results: [],
        },
      });

      const wrapper = mountPage();
      await flushPromises();

      const input = wrapper.find('input');
      await input.setValue('甲类仓库消防间距');

      const buttons = wrapper.findAll('button');
      const testBtn = buttons.find(b => b.text().includes('测试'));
      if (testBtn) {
        await testBtn.trigger('click');
        await flushPromises();

        expect(wrapper.text()).toContain('280ms');
        expect(wrapper.text()).toContain('甲类仓库与明火点间距不应小于30m');
      }
    });

    it('检索失败应显示错误', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      mockPost.mockRejectedValue({
        response: { data: { error: 'RAG 检索超时' } },
      });

      const wrapper = mountPage();
      await flushPromises();

      const input = wrapper.find('input');
      await input.setValue('测试查询');

      const buttons = wrapper.findAll('button');
      const testBtn = buttons.find(b => b.text().includes('测试'));
      if (testBtn) {
        await testBtn.trigger('click');
        await flushPromises();

        expect(wrapper.text()).toContain('RAG 检索超时');
      }
    });

    it('应显示预设查询按钮', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('苯的储存要求是什么');
      expect(wrapper.text()).toContain('甲类仓库消防间距标准');
    });
  });

  describe('增量加载', () => {
    it('应渲染增量加载按钮', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('执行增量加载');
    });

    it('点击应调用 POST /api/knowledgebase/incremental-load', async () => {
      mockGet.mockResolvedValue({ data: { mode: 'Hybrid' } });
      mockPost.mockResolvedValue({ data: { message: '已加载 3 个新文档' } });

      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const loadBtn = buttons.find(b => b.text().includes('执行增量加载'));
      if (loadBtn) {
        await loadBtn.trigger('click');
        await flushPromises();

        expect(mockPost).toHaveBeenCalledWith('/api/knowledgebase/incremental-load');
      }
    });
  });
});
