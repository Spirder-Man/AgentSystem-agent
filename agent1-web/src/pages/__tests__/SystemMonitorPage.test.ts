/**
 * P2-4: SystemMonitorPage 系统运维看板测试
 *
 * 覆盖: 健康加载/数据库状态/Ollama 状态/LLM 指标/审计统计/自动刷新/Error
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
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
  ElTag: { name: 'ElTag', template: '<span><slot /></span>' },
}));

// Mock axios
const mockGet = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { get: (...args: unknown[]) => mockGet(...args) },
}));

// Mock SkeletonCard / EmptyState
vi.mock('@/components/common/SkeletonCard.vue', () => ({
  default: { name: 'SkeletonCard', template: '<div class="skeleton-card"></div>', props: { count: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String }, emits: ['action'] },
}));

import SystemMonitorPage from '../SystemMonitorPage.vue';

const healthData = {
  status: 'healthy',
  version: '2.5.0',
  timestamp: new Date().toISOString(),
  checks: {
    database: 'connected',
    ollama: 'reachable',
    knowledge_base_docs: 142,
    llm_calls: 3200,
    llm_error_rate: '1.2%',
  },
};

const auditStatsData = {
  totalCount: 5200,
  byOperation: { LOGIN: 1200, EXPORT: 300, COMPLIANCE_CHECK: 2500, QUERY: 1200 },
  byUser: { admin: 3500, auditor: 1700 },
  lastLogAt: new Date().toISOString(),
};

function mountPage() {
  return mount(SystemMonitorPage, {
    global: { stubs: { 'el-pagination': true, 'el-progress': true } },
  });
}

describe('SystemMonitorPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    vi.useFakeTimers(); // 控制 setInterval
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('健康状态加载', () => {
    it('加载时应显示骨架屏', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await nextTick();

      expect(wrapper.find('.skeleton-card').exists()).toBe(true);
    });

    it('加载成功应显示系统版本和状态徽章', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('2.5.0');
      expect(wrapper.text()).toContain('健康');
    });

    it('数据库已连接应显示绿色状态', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('已连接');
      expect(wrapper.text()).toContain('142');
    });

    it('数据库断开应显示红色状态', async () => {
      const degradedHealth = {
        ...healthData,
        checks: { ...healthData.checks, database: 'disconnected' },
      };
      mockGet.mockResolvedValueOnce({ data: degradedHealth });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('断开');
    });

    it('Ollama 不可达应显示不可达', async () => {
      const degradedHealth = {
        ...healthData,
        checks: { ...healthData.checks, ollama: 'unreachable' },
      };
      mockGet.mockResolvedValueOnce({ data: degradedHealth });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('不可达');
    });

    it('加载失败应显示错误并支持重试', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('监控数据加载失败');

      // 重试
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });
      const retryBtn = wrapper.find('.empty-state button');
      await retryBtn.trigger('click');
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(4); // 原始2 + 重试2
    });
  });

  describe('LLM 指标展示', () => {
    it('应显示累计调用次数和错误率', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('LLM 推理指标');
      expect(wrapper.text()).toContain('3200');
      expect(wrapper.text()).toContain('1.2%');
    });
  });

  describe('审计统计展示', () => {
    it('应显示操作分布和用户活跃度', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('操作分布');
      expect(wrapper.text()).toContain('用户活跃度');
      expect(wrapper.text()).toContain('5200');
      expect(wrapper.text()).toContain('LOGIN');
    });
  });

  describe('自动刷新', () => {
    it('应在 30 秒后自动刷新', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      mountPage();
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(2); // 初始加载

      // 前进 30 秒
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });
      vi.advanceTimersByTime(30_000);
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(4); // 再调 2 次
    });

    it('组件卸载时应停止定时器', async () => {
      mockGet.mockResolvedValueOnce({ data: healthData });
      mockGet.mockResolvedValueOnce({ data: auditStatsData });

      const wrapper = mountPage();
      await flushPromises();

      wrapper.unmount();

      // 前进 30 秒，不应再触发
      vi.advanceTimersByTime(30_000);
      await flushPromises();

      // 仍然是初始的 2 次
      expect(mockGet).toHaveBeenCalledTimes(2);
    });
  });
});
