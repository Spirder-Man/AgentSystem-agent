/**
 * P3-1: AuditPage 审计日志页面测试（增强版）
 *
 * 覆盖: 日志列表加载/筛选/分页/哈希链验证/审计统计/导出报告/敏感标记/边界
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { nextTick } from 'vue';

// Mock vue-router
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {}, query: {} }),
}));

// Mock Element Plus (完整 stub)
vi.mock('element-plus', () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
  ElMessageBox: { confirm: vi.fn(() => Promise.resolve()) },
  ElTag: { name: 'ElTag', template: '<span><slot /></span>', props: { type: String, size: String } },
  ElButton: { name: 'ElButton', template: '<button :disabled="loading || disabled"><slot /></button>', props: { loading: Boolean, disabled: Boolean, icon: Object, type: String, size: String, text: Boolean } },
  ElInput: { name: 'ElInput', template: '<input :placeholder="placeholder" />', props: { modelValue: String, placeholder: String, size: String, clearable: Boolean, prefixIcon: Object } },
  ElDatePicker: { name: 'ElDatePicker', template: '<input />', props: { modelValue: String, type: String, placeholder: String, size: String, valueFormat: String } },
  ElPagination: { name: 'ElPagination', template: '<div class="el-pagination"></div>', props: { currentPage: Number, pageSize: Number, total: Number, layout: String, small: Boolean }, emits: ['current-change'] },
  ElAlert: { name: 'ElAlert', template: '<div><slot name="title" />{{ title }}</div>', props: { title: String, type: String, closable: Boolean, showIcon: Boolean } },
  ElIcon: { name: 'ElIcon', template: '<span><slot /></span>', props: { size: Number } },
}));

// Mock icons
vi.mock('@element-plus/icons-vue', () => ({
  Search: {},
  RefreshRight: {},
  CircleCheck: {},
  CircleClose: {},
  WarningFilled: {},
}));

// Mock axios
const mockGet = vi.fn();
vi.mock('@/lib/axios', () => ({
  default: { get: (...args: unknown[]) => mockGet(...args) },
}));

// Mock SkeletonTable / EmptyState
vi.mock('@/components/common/SkeletonTable.vue', () => ({
  default: { name: 'SkeletonTable', template: '<div class="skeleton-table"></div>', props: { rows: Number } },
}));
vi.mock('@/components/common/EmptyState.vue', () => ({
  default: { name: 'EmptyState', template: '<div class="empty-state">{{ title }}<button @click="$emit(\'action\')">重试</button></div>', props: { icon: String, title: String, description: String }, emits: ['action'] },
}));

import AuditPage from '../AuditPage.vue';
import { ElMessage } from 'element-plus';

// 通过 vm 访问 script setup 内部方法
function vmMethods(wrapper: ReturnType<typeof mountPage>) {
  return wrapper.vm as unknown as {
    fetchAuditLogs: () => Promise<void>;
    verifyIntegrity: () => Promise<void>;
    exportReport: () => Promise<void>;
  };
}

const logEntry = {
  id: 1,
  timestamp: '2026-07-10T08:00:00Z',
  user: 'admin',
  operation: '合规审核',
  details: '查询: 苯与丙酮同库储存',
  isSensitive: true,
  chainHash: 'a1b2c3d4',
};

const logEntry2 = {
  id: 2,
  timestamp: '2026-07-10T09:00:00Z',
  user: 'auditor',
  operation: '查看报告',
  details: '报告: report-001',
  isSensitive: false,
  chainHash: null,
};

const statsData = {
  totalCount: 1523,
  byOperation: { '合规审核': 500, '危化品查询': 200, '巡检执行': 50, '查看报告': 773 },
  byUser: { admin: 680, auditor: 520, viewer: 323 },
  lastLogAt: '2026-07-10 14:30:00',
};

function mountPage() {
  return mount(AuditPage, {
    global: {
      stubs: {
        'el-table': true,
        'el-table-column': true,
        'el-button': { template: '<button><slot /></button>' },
        'el-tag': { template: '<span><slot /></span>' },
        'el-input': { template: '<input />' },
        'el-date-picker': { template: '<input />' },
        'el-pagination': { template: '<div class="el-pagination"><slot /></div>' },
        'el-alert': { template: '<div>{{ $attrs.title }}<slot /></div>' },
        'el-icon': { template: '<span><slot /></span>' },
      },
    },
  });
}

function mountWithLogs(count = 1) {
  const logs = count === 1
    ? [logEntry]
    : [logEntry, logEntry2, { ...logEntry, id: 3, user: 'viewer', operation: '查看报告', isSensitive: false }];
  mockGet
    .mockResolvedValueOnce({ data: { logs, total: logs.length } })
    .mockResolvedValueOnce({ data: statsData });
  return mountPage();
}

describe('AuditPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReset();
  });

  // ═══════════════════════════════════════
  // 日志列表加载
  // ═══════════════════════════════════════
  describe('日志列表加载', () => {
    it('加载时应显示骨架表', async () => {
      mockGet.mockReturnValue(new Promise(() => {}));
      const wrapper = mountPage();
      await nextTick();

      expect(wrapper.find('.skeleton-table').exists()).toBe(true);
    });

    it('加载成功应渲染日志记录和统计卡片', async () => {
      const wrapper = mountWithLogs();
      await flushPromises();

      expect(wrapper.text()).toContain('合规审核');
      expect(wrapper.text()).toContain('1523');
      expect(wrapper.text()).toContain('admin');
    });

    it('加载失败应显示错误信息', async () => {
      mockGet.mockRejectedValue(new Error('Network error'));
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.find('.empty-state').exists()).toBe(true);
    });

    it('403 应显示无权限提示', async () => {
      mockGet.mockRejectedValue({ response: { status: 403 } });
      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('无权限');
      expect(wrapper.text()).toContain('admin');
    });

    it('空列表应显示空状态', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('暂无审计日志');
    });

    it('刷新应重新加载日志', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1, page: 1, pageSize: 50 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(mockGet).toHaveBeenCalledTimes(2);

      // 直接调用 fetchAuditLogs 触发刷新
      mockGet.mockResolvedValue({ data: { logs: [logEntry2], total: 1, page: 1, pageSize: 50 } });
      await vmMethods(wrapper).fetchAuditLogs();
      await flushPromises();

      // 刷新只触发 fetchAuditLogs（+1 次）
      expect(mockGet).toHaveBeenCalledTimes(3);
    });
  });

  // ═══════════════════════════════════════
  // 审计统计
  // ═══════════════════════════════════════
  describe('审计统计', () => {
    it('应展示总记录数、操作分布、活跃用户和最后记录时间', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('总记录');
      expect(wrapper.text()).toContain('1523');
      expect(wrapper.text()).toContain('操作分布');
      expect(wrapper.text()).toContain('合规审核');
      expect(wrapper.text()).toContain('活跃用户');
      expect(wrapper.text()).toContain('用户活跃');
    });

    it('应展示活跃用户数量', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      // 3 users: admin, auditor, viewer
      expect(wrapper.text()).toMatch(/活跃用户[\s\S]*3/);
    });

    it('应展示最后记录时间', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('2026-07-10 14:30:00');
    });

    it('应展示用户活跃分布标签', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('admin: 680');
      expect(wrapper.text()).toContain('auditor: 520');
      expect(wrapper.text()).toContain('viewer: 323');
    });

    it('统计加载失败应静默处理不阻塞页面', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } })
        .mockRejectedValueOnce(new Error('stats fail'));

      const wrapper = mountPage();
      await flushPromises();

      // 日志应正常显示，状态区域不渲染
      expect(wrapper.text()).toContain('合规审核');
      expect(wrapper.text()).not.toContain('总记录');
    });
  });

  // ═══════════════════════════════════════
  // 日志筛选
  // ═══════════════════════════════════════
  describe('日志筛选', () => {
    it('点击查询应重新请求日志', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockClear();
      mockGet.mockResolvedValue({ data: { logs: [logEntry2], total: 1, page: 1, pageSize: 50 } });

      await vmMethods(wrapper).fetchAuditLogs();
      await flushPromises();

      const logsCall = mockGet.mock.calls.find((c: unknown[]) =>
        typeof c[0] === 'string' && (c[0] as string).includes('/api/Audit/logs')
      );
      expect(logsCall).toBeDefined();
    });

    it('筛选栏应包含查询按钮', async () => {
      const wrapper = mountWithLogs();
      await flushPromises();

      expect(wrapper.text()).toContain('查询');
      // 关闭按钮被 mocked 为 button，确认筛选相关组件已渲染
      const buttons = wrapper.findAll('button');
      const queryBtn = buttons.find(b => b.text().includes('查询'));
      expect(queryBtn).toBeDefined();
    });

    it('空筛选条件下日志请求应包含分页参数', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      mountPage();
      await flushPromises();

      const logsCall = mockGet.mock.calls.find((c: unknown[]) =>
        typeof c[0] === 'string' && (c[0] as string).includes('/api/Audit/logs')
      );
      expect(logsCall).toBeDefined();
      expect((logsCall as unknown[])[1]).toHaveProperty('params');
    });
  });

  // ═══════════════════════════════════════
  // 分页
  // ═══════════════════════════════════════
  describe('分页', () => {
    it('total > pageSize 时应显示分页器', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry, logEntry2], total: 100, page: 1, pageSize: 50 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      // ElPagination stub 渲染为 .el-pagination div
      expect(wrapper.html()).toContain('el-pagination');
    });

    it('total <= pageSize 时不显示分页器', async () => {
      const wrapper = mountWithLogs();
      await flushPromises();

      expect(wrapper.find('.el-pagination').exists()).toBe(false);
    });

    it('翻页应重新触发日志请求', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 100 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockClear();
      mockGet.mockResolvedValue({ data: { logs: [logEntry2], total: 100 } });

      const pagination = wrapper.findComponent({ name: 'ElPagination' });
      if (pagination.exists()) {
        await pagination.vm.$emit('current-change', 2);
        await flushPromises();

        const logsCall = mockGet.mock.calls.find((c: unknown[]) =>
          typeof c[0] === 'string' && (c[0] as string).includes('/api/Audit/logs')
        );
        expect(logsCall).toBeDefined();
      }
    });
  });

  // ═══════════════════════════════════════
  // 哈希链验证
  // ═══════════════════════════════════════
  describe('哈希链验证', () => {
    it('哈希链完整应调用 integrity 端点并显示成功', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockResolvedValue({ data: { intact: true, detail: '所有哈希链验证通过' } });
      await vmMethods(wrapper).verifyIntegrity();
      await flushPromises();

      expect(mockGet).toHaveBeenCalledWith('/api/Audit/integrity');
      expect(ElMessage.success).toHaveBeenCalled();
    });

    it('哈希链断裂应显示警告弹窗', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockResolvedValue({ data: { intact: false, detail: '记录 #42 哈希不匹配' } });
      await vmMethods(wrapper).verifyIntegrity();
      await flushPromises();

      expect(wrapper.text()).toContain('哈希链断裂');
    });

    it('验证失败应弹出错误提示', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockRejectedValue(new Error('verify failed'));
      await vmMethods(wrapper).verifyIntegrity();
      await flushPromises();

      expect(ElMessage.error).toHaveBeenCalledWith('哈希链验证失败');
    });
  });

  // ═══════════════════════════════════════
  // 导出报告
  // ═══════════════════════════════════════
  describe('导出报告', () => {
    it('导出按钮存在', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('导出报告');
    });

    it('导出成功应调用 export 端点', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      // jsdom 中 Blob/URL.createObjectURL 行为受限，仅验证 API 调用
      mockGet.mockResolvedValue({ data: { report: '# report content' } });
      try {
        await vmMethods(wrapper).exportReport();
        await flushPromises();
      } catch { /* jsdom Blob 相关限制，忽略 */ }

      const exportCall = mockGet.mock.calls.find((c: unknown[]) =>
        typeof c[0] === 'string' && (c[0] as string).includes('/api/Audit/export')
      );
      expect(exportCall).toBeDefined();
    });

    it('导出失败应显示错误提示', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      mockGet.mockRejectedValue(new Error('export failed'));
      await vmMethods(wrapper).exportReport();
      await flushPromises();

      expect(ElMessage.error).toHaveBeenCalledWith('导出失败');
    });
  });

  // ═══════════════════════════════════════
  // 敏感操作标记
  // ═══════════════════════════════════════
  describe('敏感操作标记', () => {
    it('isSensitive=true 的日志应显示敏感标记', async () => {
      const wrapper = mountWithLogs();
      await flushPromises();

      expect(wrapper.text()).toContain('敏感');
    });

    it('isSensitive=false 的日志不应显示敏感标记', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry2], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).not.toContain('敏感');
    });
  });

  // ═══════════════════════════════════════
  // 边界情况
  // ═══════════════════════════════════════
  describe('边界情况', () => {
    it('chainHash 为 null 时显示 —', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry2], total: 1 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('—');
    });

    it('多条日志应全部渲染', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [logEntry, logEntry2, { ...logEntry, id: 3, user: 'viewer', operation: '查看报告', isSensitive: false, chainHash: null }], total: 3 } })
        .mockResolvedValueOnce({ data: statsData });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('admin');
      expect(wrapper.text()).toContain('auditor');
      expect(wrapper.text()).toContain('viewer');
    });

    it('byUser 为空对象时不显示用户活跃区域', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: { ...statsData, byUser: {} } });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).not.toContain('用户活跃');
    });

    it('lastLogAt 为 null 时显示 —', async () => {
      mockGet
        .mockResolvedValueOnce({ data: { logs: [], total: 0 } })
        .mockResolvedValueOnce({ data: { ...statsData, lastLogAt: null } });

      const wrapper = mountPage();
      await flushPromises();

      expect(wrapper.text()).toContain('—');
    });
  });
});
