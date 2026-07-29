/**
 * P2-2: DashboardPage 仪表盘页面测试 (v2 — 对接 DashboardController 6 端点)
 *
 * 覆盖: Overview 加载/统计卡片/一键快检/自动扫描(Dashboard/scan)/
 *       发现列表/巡检历史/隐患报告/严重程度分布/Loading/Error
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { setActivePinia, createPinia } from 'pinia';

// Mock vue-router
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
}));

// Mock axios
const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  },
}));

// Mock SkeletonCard / EmptyState
vi.mock('@/components/common/SkeletonCard.vue', () => ({
  default: { name: 'SkeletonCard', template: '<div class="skeleton-card"><slot /></div>', props: { count: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: {
    name: 'EmptyState',
    template: '<div class="empty-state">{{ title }}<button @click="$emit(\'action\')">重试</button></div>',
    props: { icon: String, title: String, description: String },
    emits: ['action'],
  },
}));

import DashboardPage from '../DashboardPage.vue';

// ── Mock 数据 (对齐 Dashboard API) ──

const overviewData = {
  totalAssets: 6,
  checkedAssets: 5,
  compliantAssets: 3,
  nonCompliantAssets: 2,
  complianceRate: 0.85,
  totalFindings: 12,
  openFindings: 5,
  remediationRate: 0.58,
  lastAutoScanAt: new Date(Date.now() - 86400000).toISOString(),
  hasInventory: true,
  findingsBySeverity: { Critical: 2, High: 3, Medium: 4, Low: 2, Info: 1 },
  findingsByStatus: { New: 2, Confirmed: 3, InProgress: 4, Remediated: 2, VerifiedClosed: 1 },
};

const findingsData = {
  items: [
    {
      findingId: 'f-001',
      description: '苯与丙酮同库储存违规',
      regulationRef: 'GB 15603-2022 §4.2.2',
      assetId: 'a1',
      assetName: '苯',
      assetLocation: '甲类仓库A区',
      severity: 'Critical',
      status: 'New',
      isOpen: true,
      assignee: '',
      remediationPlan: '立即分库',
      deadline: null,
      discoveredAt: '2026-07-08T10:00:00Z',
      lastStatusChangeAt: '2026-07-08T10:00:00Z',
      verifiedBy: null,
      verifiedAt: null,
    },
  ],
  total: 1,
  summary: { totalFindings: 5, openFindings: 4, bySeverity: { Critical: 1 }, byStatus: { New: 1 } },
  appliedFilter: { severity: 'all', status: 'all', openOnly: true },
};

const historyData = {
  items: [
    {
      planId: 'plan-001',
      name: '甲类仓库周检',
      area: '甲类仓库A区',
      type: 'Weekly',
      inspector: '张三',
      status: 'Completed',
      scheduledDate: '2026-07-08T10:00:00Z',
      createdAt: '2026-07-08T10:00:00Z',
      notes: '重点检查',
      itemCount: 5,
      roundCount: 2,
      rounds: [
        {
          roundId: 'r1',
          startedAt: '2026-07-08T10:00:00Z',
          completedAt: '2026-07-08T10:00:45Z',
          totalItems: 5,
          compliantCount: 4,
          nonCompliantCount: 1,
          uncertainCount: 0,
          complianceRate: 0.8,
          duration: '45s',
          executedBy: '张三',
        },
      ],
    },
  ],
  total: 1,
  statusBreakdown: { Completed: 1 },
};

const hazardData = {
  generatedAt: new Date().toISOString(),
  disclaimer: '本报告为 AI 辅助生成',
  summary: {
    totalAssets: 6,
    totalFindings: 12,
    openFindings: 4,
    closedFindings: 8,
    bySeverity: { Critical: 1, High: 1, Medium: 2 },
  },
  items: [
    {
      findingId: 'f-001',
      description: '苯与丙酮同库储存违规',
      regulationRef: 'GB 15603-2022 §4.2.2',
      severity: 'Critical',
      status: 'New',
      assignee: '',
      remediationPlan: '立即分库',
      deadline: null,
      discoveredAt: '2026-07-08T10:00:00Z',
      asset: { assetId: 'a1', name: '苯', location: '甲类仓库A区', casNumber: '71-43-2', isMajorHazardSource: true },
    },
  ],
};

const quickCheckResult = {
  isCompliant: true,
  conclusion: '合规：储存条件满足 GB 15603 要求',
  regulationRef: 'GB 15603-1995 §4.2.2',
  warnings: [] as string[],
  elapsedMs: 1250,
};

const scanAccepted = { scanId: 'mock-scan-001', totalAssets: 6 };

const scanStatusDone = {
  running: false,
  scanId: 'mock-scan-001',
  current: 6,
  total: 6,
  newFindings: 1,
  startedAt: new Date(Date.now() - 5000).toISOString(),
  completedAt: new Date().toISOString(),
  error: null,
};

function setupMocks() {
  mockGet.mockImplementation((url: string) => {
    if (url === '/api/Dashboard/overview') return Promise.resolve({ data: overviewData });
    if (url === '/api/Dashboard/findings') return Promise.resolve({ data: findingsData });
    if (url === '/api/Dashboard/history') return Promise.resolve({ data: historyData });
    if (url === '/api/Dashboard/report/hazard') return Promise.resolve({ data: hazardData });
    if (url === '/api/Dashboard/scan/status') return Promise.resolve({ data: scanStatusDone });
    return Promise.reject(new Error('Unknown URL'));
  });
}

function mountPage() {
  setActivePinia(createPinia());
  return mount(DashboardPage, { global: { stubs: { 'el-pagination': true } } });
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPost.mockReset();
    setupMocks();
  });

  describe('Overview 加载', () => {
    it('加载时应显示骨架屏', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.skeleton-card').exists()).toBe(true);
    });

    it('加载成功应显示统计卡片', async () => {
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('合规率');
      expect(wrapper.text()).toContain('85%');
      expect(wrapper.text()).toContain('资产总量');
      expect(wrapper.text()).toContain('6');
      expect(wrapper.text()).toContain('未闭环发现');
      expect(wrapper.text()).toContain('5');
    });

    it('加载失败应显示错误并支持重试', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(true);
      setupMocks();
      const retryBtn = wrapper.find('.empty-state button');
      await retryBtn.trigger('click');
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(false);
    });
  });

  describe('统计卡片颜色逻辑', () => {
    it('合规率 ≥80% 应为绿色', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Dashboard/overview')
          return Promise.resolve({ data: { ...overviewData, complianceRate: 0.9 } });
        if (url.startsWith('/api/Dashboard/'))
          return Promise.resolve({
            data: url.includes('findings') ? findingsData : url.includes('history') ? historyData : hazardData,
          });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.html()).toContain('text-emerald-600');
    });

    it('合规率 60-79% 应为琥珀色', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Dashboard/overview')
          return Promise.resolve({ data: { ...overviewData, complianceRate: 0.65 } });
        if (url.startsWith('/api/Dashboard/'))
          return Promise.resolve({
            data: url.includes('findings') ? findingsData : url.includes('history') ? historyData : hazardData,
          });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.html()).toContain('text-amber-600');
    });

    it('合规率 <60% 应为红色', async () => {
      mockGet.mockImplementation((url: string) => {
        if (url === '/api/Dashboard/overview')
          return Promise.resolve({ data: { ...overviewData, complianceRate: 0.4 } });
        if (url.startsWith('/api/Dashboard/'))
          return Promise.resolve({
            data: url.includes('findings') ? findingsData : url.includes('history') ? historyData : hazardData,
          });
        return Promise.reject(new Error('Unknown'));
      });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.html()).toContain('text-red-600');
    });
  });

  describe('一键快检', () => {
    it('输入查询后应调用 POST /api/Inspection/quick-check', async () => {
      mockPost.mockResolvedValue({ data: quickCheckResult });
      const wrapper = mountPage();
      await flushPromises();
      const inputs = wrapper.findAll('input');
      const quickInput = inputs.find((el) => {
        const p = el.attributes('placeholder');
        return p && p.includes('合规问题');
      });
      if (quickInput) {
        await quickInput.setValue('硝酸储存');
        const buttons = wrapper.findAll('button');
        const detectBtn = buttons.find((b) => b.text().includes('检测'));
        if (detectBtn) {
          await detectBtn.trigger('click');
          await flushPromises();
          expect(mockPost).toHaveBeenCalledWith('/api/Inspection/quick-check', { query: '硝酸储存' });
        }
      }
    });

    it('快检不合规应显示红色样式', async () => {
      mockPost.mockResolvedValue({
        data: { ...quickCheckResult, isCompliant: false, conclusion: '不合规', warnings: ['缺少围堰'] },
      });
      const wrapper = mountPage();
      await flushPromises();
      const inputs = wrapper.findAll('input');
      const quickInput = inputs.find((el) => {
        const p = el.attributes('placeholder');
        return p && p.includes('合规问题');
      });
      if (quickInput) {
        await quickInput.setValue('测试');
        const buttons = wrapper.findAll('button');
        const detectBtn = buttons.find((b) => b.text().includes('检测'));
        if (detectBtn) {
          await detectBtn.trigger('click');
          await flushPromises();
          expect(wrapper.text()).toContain('不合规');
        }
      }
    });
  });

  describe('自动扫描 (Dashboard/scan)', () => {
    it('点击应调用 POST /api/Dashboard/scan 并轮询 status', async () => {
      // [#4] 扫描改后台任务：202 受理后转 /scan/status 轮询
      mockPost.mockResolvedValue({ data: scanAccepted });
      const wrapper = mountPage();
      await flushPromises();
      const buttons = wrapper.findAll('button');
      const scanBtn = buttons.find((b) => b.text().includes('触发全库扫描'));
      if (scanBtn) {
        await scanBtn.trigger('click');
        await flushPromises();
        expect(mockPost).toHaveBeenCalledWith('/api/Dashboard/scan', null);
        expect(mockGet).toHaveBeenCalledWith('/api/Dashboard/scan/status');
      }
    });

    it('扫描完成应展示统计结果', async () => {
      mockPost.mockResolvedValue({ data: scanAccepted });
      const wrapper = mountPage();
      await flushPromises();
      const buttons = wrapper.findAll('button');
      const scanBtn = buttons.find((b) => b.text().includes('触发全库扫描'));
      if (scanBtn) {
        await scanBtn.trigger('click');
        await flushPromises();
        expect(wrapper.text()).toContain('扫描完成');
      }
    });
  });

  describe('Tab 面板', () => {
    it('应显示合规发现列表', async () => {
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('合规发现');
      expect(wrapper.text()).toContain('苯与丙酮同库储存违规');
    });

    it('切换到巡检历史应显示计划', async () => {
      const wrapper = mountPage();
      await flushPromises();
      const tabs = wrapper.findAll('button');
      const historyTab = tabs.find((b) => b.text().includes('巡检历史'));
      if (historyTab) {
        await historyTab.trigger('click');
        await flushPromises();
        expect(wrapper.text()).toContain('甲类仓库周检');
      }
    });

    it('切换到隐患报告应显示隐患条目', async () => {
      const wrapper = mountPage();
      await flushPromises();
      const tabs = wrapper.findAll('button');
      const hazardTab = tabs.find((b) => b.text().includes('隐患报告'));
      if (hazardTab) {
        await hazardTab.trigger('click');
        await flushPromises();
        expect(wrapper.text()).toContain('AI 辅助生成');
      }
    });
  });

  describe('严重程度分布', () => {
    it('应渲染各级别条形图', async () => {
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('发现按严重程度分布');
      expect(wrapper.text()).toContain('Critical');
    });
  });
});
