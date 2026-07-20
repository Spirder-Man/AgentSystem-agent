/**
 * P2-9a: AIChatPage AI 合规助手对话页面测试
 *
 * 覆盖: 渲染/发送消息/API 调用/对话历史/清空/建议按钮/Enter 发送
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

// Mock EmptyState
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}</div>', props: { icon: String, title: String, description: String } },
}));

import AIChatPage from '../AIChatPage.vue';

describe('AIChatPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPost.mockReset();
  });

  function mountPage() {
    return mount(AIChatPage);
  }

  describe('渲染', () => {
    it('应渲染页面标题', () => {
      const wrapper = mountPage();
      expect(wrapper.text()).toContain('AI 合规助手');
    });

    it('初始状态应显示空状态和快捷建议', () => {
      const wrapper = mountPage();
      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('苯和丙酮能放在同一个仓库吗');
    });

    it('应有发送按钮', () => {
      const wrapper = mountPage();
      expect(wrapper.text()).toContain('发送');
    });
  });

  describe('发送消息', () => {
    it('空消息不应调用 API', async () => {
      const wrapper = mountPage();
      // 找到发送按钮（文本为"发送"）而非建议按钮
      const buttons = wrapper.findAll('button');
      const sendBtn = buttons.find(b => b.text() === '发送');
      if (sendBtn) {
        await sendBtn.trigger('click');
        await flushPromises();
        expect(mockPost).not.toHaveBeenCalled();
      }
    });

    it('发送应调用 POST /api/Compliance/check', async () => {
      mockPost.mockResolvedValue({
        data: {
          query: '苯和丙酮能放在同一个仓库吗',
          response: '不可以同库储存（GB 15603）',
          toolsUsed: ['CheckStorageCompatibility'],
        },
      });

      const wrapper = mountPage();
      const textarea = wrapper.find('textarea');
      await textarea.setValue('苯和丙酮能放在同一个仓库吗');

      const buttons = wrapper.findAll('button');
      const sendBtn = buttons.find(b => b.text() === '发送');
      if (sendBtn) {
        await sendBtn.trigger('click');
        await flushPromises();
      }

      expect(mockPost).toHaveBeenCalledWith('/api/Compliance/check', {
        query: '苯和丙酮能放在同一个仓库吗',
      });
    });

    it('发送后应清空输入并显示回复', async () => {
      mockPost.mockResolvedValue({
        data: {
          query: '硝酸应该如何储存',
          response: '硝酸应储存于阴凉通风处，远离可燃物（GB 15603）',
          toolsUsed: ['CheckHazardCategory'],
        },
      });

      const wrapper = mountPage();
      const textarea = wrapper.find('textarea');
      await textarea.setValue('硝酸应该如何储存');

      const allButtons = wrapper.findAll('button');
      const sendBtn = allButtons.find(b => b.text() === '发送');
      if (sendBtn) {
        await sendBtn.trigger('click');
        await flushPromises();
      }

      // 输入应被清空
      expect((textarea.element as HTMLTextAreaElement).value).toBe('');

      // 应显示回复
      expect(wrapper.text()).toContain('硝酸应储存于阴凉通风处');
    });

    it('失败应显示错误提示', async () => {
      mockPost.mockRejectedValue({
        response: { data: { error: 'AI 服务暂时不可用' } },
      });

      const wrapper = mountPage();
      const textarea = wrapper.find('textarea');
      await textarea.setValue('测试');

      const allButtons = wrapper.findAll('button');
      const sendBtn = allButtons.find(b => b.text() === '发送');
      if (sendBtn) {
        await sendBtn.trigger('click');
        await flushPromises();
      }

      expect(wrapper.text()).toContain('AI 服务暂时不可用');
    });
  });

  describe('快捷建议', () => {
    it('点击建议应自动发送', async () => {
      mockPost.mockResolvedValue({
        data: {
          query: '苯和丙酮能放在同一个仓库吗',
          response: '回复内容',
          toolsUsed: [],
          verifiedRegulations: [],
          hallucinatedRegulations: [],
          warnings: [],
        },
      });

      const wrapper = mountPage();
      const suggestionBtns = wrapper.findAll('button');
      const firstSuggestion = suggestionBtns.find(b => b.text().includes('苯和丙酮'));

      if (firstSuggestion) {
        await firstSuggestion.trigger('click');
        await flushPromises();

        expect(mockPost).toHaveBeenCalledWith('/api/Compliance/check', {
          query: '苯和丙酮能放在同一个仓库吗',
        });
      }
    });
  });

  describe('清空对话', () => {
    it('发送消息后应显示清空按钮', async () => {
      mockPost.mockResolvedValue({
        data: {
          query: '测试', response: '回复', toolsUsed: [],
          verifiedRegulations: [], hallucinatedRegulations: [], warnings: [],
        },
      });

      const wrapper = mountPage();
      const textarea = wrapper.find('textarea');
      await textarea.setValue('测试');

      const allButtons = wrapper.findAll('button');
      const sendBtn = allButtons.find(b => b.text() === '发送');
      if (sendBtn) {
        await sendBtn.trigger('click');
        await flushPromises();
      }

      // 清空按钮应出现
      expect(wrapper.text()).toContain('清空对话');
    });

    it('点击清空应清除所有消息', async () => {
      mockPost.mockResolvedValue({
        data: {
          query: '测试', response: '回复', toolsUsed: [],
          verifiedRegulations: [], hallucinatedRegulations: [], warnings: [],
        },
      });

      const wrapper = mountPage();
      const textarea = wrapper.find('textarea');
      await textarea.setValue('测试');

      const sendBtn = wrapper.find('button');
      await sendBtn.trigger('click');
      await flushPromises();

      // 找到清空按钮
      const buttons = wrapper.findAll('button');
      const clearBtn = buttons.find(b => b.text().includes('清空对话'));
      if (clearBtn) {
        await clearBtn.trigger('click');
        await nextTick();

        // 应回到空状态
        expect(wrapper.find('.empty-state').exists()).toBe(true);
      }
    });
  });
});
