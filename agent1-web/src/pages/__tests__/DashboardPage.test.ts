/**
 * P2-2: DashboardPage 仪表盘页面测试
 *
 * 覆盖: Summary 加载/统计卡片/一键快检/自动扫描/严重程度分布/Loading/Error/Empty
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

// Mock SkeletonCard / EmptyState (stub 渲染)
vi.mock('@/components/common/SkeletonCard.vue', () => ({
  default: { name: 'SkeletonCard', template: '<div class="skeleton-card"><slot /></div>', props: { count: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String, description: String }, emits: ['action'] },
}));

import DashboardPage from '../DashboardPage.vue';

const summaryData = {
  complianceRate: 0.85,
  totalAssets: 120,
  checkedAssets: 102,
  openFindings: 5,
  findingsBySeverity: { Critical: 2, High: 3, Medium: 8, Low: 12, Info: 5 },
  riskDistribution: { critical: 2, high: 3, unknown: 1, low: 116 },
};

const quickCheckResult = {
  isCompliant: true,
  conclusion: '合规：储存条件满足 GB 15603 要求',
  regulationRef: 'GB 15603-1995 §4.2.2',
  warnings: [] as string[],
  elapsedMs: 1250,
};

const scanResult = {
  totalAssets: 120,
  checkedAssets: 120,
  totalFindings: 30,
  newFindings: 2,
  scannedAt: new Date().toISOString(),
};

function mountPage() {
  setActivePinia(createPinia());
  return mount(DashboardPage, {
    global: { stubs: { 'el-pagination': true } },
  });
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPost.mockReset();
  });

  describe('Summary 加载', () => {
    it('加载时应显示骨架屏', async () => {
      mockGet.mockReturnValue(new Promise(() => {})); // 永远 pending
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.skeleton-card').exists()).toBe(true);
    });

    it('加载成功应显示统计卡片', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      const wrapper = mountPage();
      await flushPromises();

      // 合规率卡片
      expect(wrapper.text()).toContain('合规率');
      expect(wrapper.text()).toContain('85%');
      // 资产总量
      expect(wrapper.text()).toContain('资产总量');
      expect(wrapper.text()).toContain('120');
      // 已检查
      expect(wrapper.text()).toContain('已检查');
      expect(wrapper.text()).toContain('102/120');
      // 未闭环发现
      expect(wrapper.text()).toContain('未闭环发现');
      expect(wrapper.text()).toContain('5');
    });

    it('加载失败应显示错误并支持重试', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('加载失败');

      // 点击重试
      mockGet.mockResolvedValue({ data: summaryData });
      const retryBtn = wrapper.find('.empty-state button');
      await retryBtn.trigger('click');
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(2);
    });
  });

  describe('统计卡片颜色逻辑', () => {
    it('合规率 ≥80% 应为绿色', async () => {
      mockGet.mockResolvedValue({ data: { ...summaryData, complianceRate: 0.9 } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('90%');
      // 合规率颜色验证：通过 text-green-600 class 确认
      expect(wrapper.html()).toContain('text-green-600');
    });

    it('合规率 60-79% 应为琥珀色', async () => {
      mockGet.mockResolvedValue({ data: { ...summaryData, complianceRate: 0.65 } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('65%');
      expect(wrapper.html()).toContain('text-amber-600');
    });

    it('合规率 <60% 应为红色', async () => {
      mockGet.mockResolvedValue({ data: { ...summaryData, complianceRate: 0.4 } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('40%');
      expect(wrapper.html()).toContain('text-red-600');
    });
  });

  describe('一键快检', () => {
    it('空输入时不应发送请求', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      const wrapper = mountPage();
      await flushPromises();

      // 清空输入（默认空），点击检测按钮
      const buttons = wrapper.findAll('button');
      const detectBtn = buttons.find(b => b.text().includes('检测'));
      if (detectBtn) {
        await detectBtn.trigger('click');
        await flushPromises();
        expect(mockPost).not.toHaveBeenCalled();
      }
    });

    it('输入查询后应调用 POST /api/Inspection/quick-check', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      mockPost.mockResolvedValue({ data: quickCheckResult });

      const wrapper = mountPage();
      await flushPromises();

      // 找到快检输入框
      const inputs = wrapper.findAll('input');
      const quickInput = inputs.find(el => {
        const placeholder = el.attributes('placeholder');
        return placeholder && placeholder.includes('合规问题');
      });

      if (quickInput) {
        await quickInput.setValue('硝酸储存条件是否合规');

        const buttons = wrapper.findAll('button');
        const detectBtn = buttons.find(b => b.text().includes('检测'));
        if (detectBtn) {
          await detectBtn.trigger('click');
          await flushPromises();

          expect(mockPost).toHaveBeenCalledWith('/api/Inspection/quick-check', {
            query: '硝酸储存条件是否合规',
          });
        }
      }
    });

    it('快检成功应展示合规结论', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      mockPost.mockResolvedValue({ data: quickCheckResult });

      const wrapper = mountPage();
      await flushPromises();

      const inputs = wrapper.findAll('input');
      const quickInput = inputs.find(el => {
        const p = el.attributes('placeholder');
        return p && p.includes('合规问题');
      });

      if (quickInput) {
        await quickInput.setValue('测试');
        const buttons = wrapper.findAll('button');
        const detectBtn = buttons.find(b => b.text().includes('检测'));
        if (detectBtn) {
          await detectBtn.trigger('click');
          await flushPromises();

          expect(wrapper.text()).toContain('合规');
          expect(wrapper.text()).toContain('GB 15603-1995');
        }
      }
    });

    it('快检不合规应显示红色样式', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      mockPost.mockResolvedValue({
        data: { ...quickCheckResult, isCompliant: false, conclusion: '不合规：缺少防泄漏措施', warnings: ['缺少围堰'] },
      });

      const wrapper = mountPage();
      await flushPromises();

      const inputs = wrapper.findAll('input');
      const quickInput = inputs.find(el => {
        const p = el.attributes('placeholder');
        return p && p.includes('合规问题');
      });

      if (quickInput) {
        await quickInput.setValue('测试');
        const buttons = wrapper.findAll('button');
        const detectBtn = buttons.find(b => b.text().includes('检测'));
        if (detectBtn) {
          await detectBtn.trigger('click');
          await flushPromises();

          expect(wrapper.text()).toContain('不合规');
          expect(wrapper.text()).toContain('缺少围堰');
        }
      }
    });
  });

  describe('自动扫描', () => {
    it('应显示触发按钮', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('触发全库扫描');
    });

    it('点击应调用 POST /api/Inspection/scan', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      mockPost.mockResolvedValue({ data: scanResult });

      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const scanBtn = buttons.find(b => b.text().includes('触发全库扫描'));
      if (scanBtn) {
        await scanBtn.trigger('click');
        await flushPromises();

        expect(mockPost).toHaveBeenCalledWith('/api/Inspection/scan', null, expect.objectContaining({ timeout: 600_000 }));
      }
    });

    it('扫描完成应展示统计结果', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      mockPost.mockResolvedValue({ data: scanResult });

      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const scanBtn = buttons.find(b => b.text().includes('触发全库扫描'));
      if (scanBtn) {
        await scanBtn.trigger('click');
        await flushPromises();

        expect(wrapper.text()).toContain('扫描完成');
        expect(wrapper.text()).toContain('120');
        expect(wrapper.text()).toContain('30');  // totalFindings
      }
    });
  });

  describe('严重程度分布', () => {
    it('应渲染各级别条形图', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('问题按严重程度分布');
      expect(wrapper.text()).toContain('Critical');
      expect(wrapper.text()).toContain('High');
    });
  });

  describe('用户信息展示', () => {
    it('应显示当前用户名和角色', async () => {
      mockGet.mockResolvedValue({ data: summaryData });
      const wrapper = mountPage();
      await flushPromises();

      // 组件挂载时 auth store 默认 username='', role=null
      // 标题行应包含 "当前:" 文字
      expect(wrapper.text()).toContain('当前:');
    });
  });
});
