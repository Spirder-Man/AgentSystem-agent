/**
 * P1-3: AssetDetailPage 资产详情页测试
 *
 * 覆盖: 加载态/成功渲染/错误重试/合规状态显示/化学品属性查询/
 *       权限拦截(403)/返回导航/边界值
 */
import type { UserRole } from '@/types/api';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { setActivePinia, createPinia } from 'pinia';
import { useAuthStore } from '@/stores/auth';

// Mock vue-router — useRoute 返回被测 assetId
const mockPush = vi.fn();
const mockRouteParams = { assetId: 'a1' };
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: mockRouteParams }),
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

// Mock v-permission directive (mounted 时从 DOM 移除无权限元素)
const vPermissionMock = {
  mounted(el: HTMLElement, binding: { value: UserRole[] }) {
    const auth = useAuthStore();
    if (!auth.hasPermission(binding.value)) {
      el.parentNode?.removeChild(el);
    }
  },
};

// Mock SkeletonCard / EmptyState
vi.mock('@/components/common/SkeletonCard.vue', () => ({
  default: { name: 'SkeletonCard', template: '<div class="skeleton-card"><slot /></div>' },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button class="retry-btn" @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String }, emits: ['action'] },
}));

import AssetDetailPage from '../AssetDetailPage.vue';

// ── Mock 数据 ──

const compliantAsset = {
  assetId: 'a1',
  name: '苯',
  casNumber: '71-43-2',
  location: '甲类仓库A区1号位',
  quantityTons: 15,
  storageCondition: '常温常压, 避光, 通风',
  responsiblePerson: '张三',
  isMajorHazardSource: true,
  lastCheckResult: true,
  lastCheckedAt: '2026-06-23T10:30:00Z',
};

const nonCompliantAsset = {
  ...compliantAsset,
  assetId: 'a2',
  name: '丙酮',
  lastCheckResult: false,
};

const uncheckedAsset = {
  ...compliantAsset,
  assetId: 'a3',
  name: '硫酸',
  lastCheckResult: null,
  lastCheckedAt: null,
};

const hazardResult = {
  substanceName: '苯',
  toolsUsed: ['hazard_query', 'regulation_lookup'],
  response: '危险类别: 易燃液体, 类别2\n国标: GB 30000.7-2013',
};

function mountPage(assetId = 'a1', role: UserRole | null = null) {
  mockRouteParams.assetId = assetId;
  const pinia = createPinia();
  setActivePinia(pinia);
  const auth = useAuthStore();
  if (role) {
    auth.setAuth({
      token: 'mock-token',
      refreshToken: 'mock-refresh',
      username: 'test',
      role,
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }
  return mount(AssetDetailPage, {
    global: {
      directives: {
        permission: vPermissionMock,
      },
      stubs: {
        'el-button': { template: '<button :disabled="$attrs.disabled" @click="$emit(\'click\')"><slot /></button>', inheritAttrs: true },
        'el-icon': { template: '<i class="el-icon"><slot /></i>' },
        'el-tag': { template: '<span class="el-tag" :class="$attrs.type"><slot /></span>', inheritAttrs: true },
        'el-pagination': true,
      },
    },
  });
}

describe('AssetDetailPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
    mockPost.mockReset();
    mockPush.mockReset();
  });

  afterEach(() => {
    // 每次测试后重置 auth store
  });

  // ═══════════════════════════════════
  // 加载与错误
  // ═══════════════════════════════════

  describe('加载态', () => {
    it('挂载时显示骨架屏', async () => {
      mockGet.mockReturnValue(new Promise(() => {})); // 永不 resolve
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.skeleton-card').exists()).toBe(true);
    });

    it('加载完成后隐藏骨架屏', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.skeleton-card').exists()).toBe(false);
    });
  });

  describe('错误态', () => {
    it('API 失败应显示错误信息', async () => {
      mockGet.mockRejectedValue(new Error('Network Error'));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(true);
      expect(wrapper.text()).toContain('加载资产信息失败');
    });

    it('点击重试应重新加载', async () => {
      mockGet.mockRejectedValueOnce(new Error('Fail'));
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('加载资产信息失败');

      mockGet.mockResolvedValueOnce({ data: compliantAsset });
      const retryBtn = wrapper.find('.retry-btn');
      await retryBtn.trigger('click');
      await flushPromises();
      expect(wrapper.find('.empty-state').exists()).toBe(false);
      expect(wrapper.text()).toContain('苯');
    });
  });

  // ═══════════════════════════════════
  // 成功渲染
  // ═══════════════════════════════════

  describe('成功渲染', () => {
    it('应显示资产名称和 CAS 号', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('苯');
      expect(wrapper.text()).toContain('71-43-2');
    });

    it('应显示全部基本信息字段', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('甲类仓库A区1号位');
      expect(wrapper.text()).toContain('15');
      expect(wrapper.text()).toContain('常温常压');
      expect(wrapper.text()).toContain('张三');
    });

    it('重大危险源应显示标签', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.html()).toContain('danger'); // isMajorHazardSource=true → danger
    });

    it('非重大危险源应显示 success 标签', async () => {
      mockGet.mockResolvedValue({ data: { ...compliantAsset, isMajorHazardSource: false } });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.html()).toContain('success');
    });

    it('已检查资产应显示检查时间', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('2026-06-23');
    });

    it('未检查资产应显示"从未检查"', async () => {
      mockGet.mockResolvedValue({ data: uncheckedAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('从未检查');
    });
  });

  // ═══════════════════════════════════
  // 合规状态
  // ═══════════════════════════════════

  describe('合规状态显示', () => {
    it('合规资产 → 显示"合规"标签（green）', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('合规');
      expect(wrapper.html()).toContain('text-green-600');
    });

    it('不合规资产 → 显示"不合规"标签（red）', async () => {
      mockGet.mockResolvedValue({ data: nonCompliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('不合规');
      expect(wrapper.html()).toContain('text-red-600');
    });

    it('未检查资产 → 显示"未检查"标签（gray）', async () => {
      mockGet.mockResolvedValue({ data: uncheckedAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('未检查');
    });

    it('合规状态区域应显示最近检查说明', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('合规检查');
      expect(wrapper.text()).toContain('合规');
    });

    it('未检查资产应显示引导性说明', async () => {
      mockGet.mockResolvedValue({ data: uncheckedAsset });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('尚未进行合规检查');
      expect(wrapper.text()).toContain('资产台账');
      expect(wrapper.text()).toContain('自动扫描');
    });
  });

  // ═══════════════════════════════════
  // 化学品属性查询
  // ═══════════════════════════════════

  describe('化学品属性查询', () => {
    it('初始状态应显示查询按钮', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();
      expect(wrapper.text()).toContain('查询化学品属性');
    });

    it('点击查询应调用 POST /api/Compliance/hazard/query', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockResolvedValue({ data: hazardResult });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      expect(queryBtn).toBeTruthy();
      await queryBtn!.trigger('click');
      await flushPromises();

      expect(mockPost).toHaveBeenCalledWith('/api/Compliance/hazard/query', {
        substanceName: '苯',
      });
    });

    it('查询成功应显示工具标签和响应文本', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockResolvedValue({ data: hazardResult });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      await queryBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('hazard_query');
      expect(wrapper.text()).toContain('regulation_lookup');
      expect(wrapper.text()).toContain('GB 30000.7-2013');
    });

    it('查询成功后应显示重新查询按钮', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockResolvedValue({ data: hazardResult });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      await queryBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('重新查询');
    });

    it('403 权限错误应显示提示', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockRejectedValue({ response: { status: 403 } });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      await queryBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('需要 auditor 权限');
    });

    it('通用查询错误应显示重试按钮', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockRejectedValue(new Error('Server Error'));
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      await queryBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('属性查询失败');
      expect(wrapper.text()).toContain('重试');
    });

    it('点击重试应重新查询', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      mockPost.mockRejectedValueOnce(new Error('First Fail'));
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询化学品属性'));
      await queryBtn!.trigger('click');
      await flushPromises();
      expect(wrapper.text()).toContain('属性查询失败');

      mockPost.mockResolvedValueOnce({ data: hazardResult });
      const retryBtn = wrapper.findAll('button').find(b => b.text().includes('重试'));
      await retryBtn!.trigger('click');
      await flushPromises();

      expect(wrapper.text()).toContain('GB 30000.7-2013');
    });
  });

  // ═══════════════════════════════════
  // 导航
  // ═══════════════════════════════════

  describe('返回导航', () => {
    it('点击"返回资产列表"应跳转 /assets', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage();
      await flushPromises();

      const buttons = wrapper.findAll('button');
      const backBtn = buttons.find(b => b.text().includes('返回资产列表'));
      expect(backBtn).toBeTruthy();
      await backBtn!.trigger('click');

      expect(mockPush).toHaveBeenCalledWith('/assets');
    });
  });

  // ═══════════════════════════════════
  // 边界值
  // ═══════════════════════════════════

  describe('边界值', () => {
    it('不同 assetId 参数应调用对应 API', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage('a99');
      await flushPromises();
      expect(mockGet).toHaveBeenCalledWith('/api/Inspection/assets/a99');
    });

    it('quantityTons=0 应正常显示', async () => {
      mockGet.mockResolvedValue({ data: { ...compliantAsset, quantityTons: 0 } });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('0');
    });

    it('无 CAS 号的资产应正常渲染', async () => {
      mockGet.mockResolvedValue({ data: { ...compliantAsset, casNumber: '' } });
      const wrapper = mountPage();
      await flushPromises();
      expect(wrapper.text()).toContain('苯');
    });
  });

  // ═══════════════════════════════════
  // 权限控制 (P1-3 增强)
  // ═══════════════════════════════════

  describe('权限控制（化学品属性查询）', () => {
    it('viewer 应看到权限不足提示', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage('a1', 'viewer');
      await flushPromises();
      expect(wrapper.text()).toContain('需要 auditor 或更高权限');
      // viewer 不应看到查询按钮（按钮被 v-permission 移除）
      const buttons = wrapper.findAll('button');
      expect(buttons.find(b => b.text().includes('查询化学品属性'))).toBeUndefined();
    });

    it('auditor 应看到查询按钮', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage('a1', 'auditor');
      await flushPromises();
      expect(wrapper.text()).toContain('查询化学品属性');
      expect(wrapper.text()).not.toContain('需要 auditor 或更高权限');
    });

    it('admin 应看到查询按钮', async () => {
      mockGet.mockResolvedValue({ data: compliantAsset });
      const wrapper = mountPage('a1', 'admin');
      await flushPromises();
      expect(wrapper.text()).toContain('查询化学品属性');
    });
  });
});
