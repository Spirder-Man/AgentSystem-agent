// ============================================================
// P2: 审计日志 API 模块
// 对齐后端 AuditController 端点（仅 Admin 角色可访问）。
// ============================================================

import { get } from './client';
import type {
  AuditLogListResponse,
  AuditIntegrityResponse,
  AuditStatsResponse,
} from '../types/api';

export interface AuditLogsParams {
  from?: string;
  to?: string;
  user?: string;
  page?: number;
  pageSize?: number;
}

export const auditApi = {
  /** 查询审计日志列表（支持时间范围 + 用户筛选 + 分页） */
  getLogs: (params?: AuditLogsParams) =>
    get<AuditLogListResponse>('/api/Audit/logs', { params }),

  /** 验证 SHA256 哈希链完整性 */
  verifyIntegrity: () =>
    get<AuditIntegrityResponse>('/api/Audit/integrity'),

  /** 导出审计报告 */
  exportReport: (from: string, to: string) =>
    get<{ report: string; generatedAt: string }>('/api/Audit/export', {
      params: { from, to },
    }),

  /** 审计统计摘要 */
  getStats: () => get<AuditStatsResponse>('/api/Audit/stats'),
};
