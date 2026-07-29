// ============================================================
// FE-1: 合规总览仪表盘 API 模块
// 对齐后端 DashboardController 6 个端点。
// ============================================================

import { get, post } from './client';
import type {
  DashboardOverview,
  DashboardAssetItem,
  DashboardFindingsResponse,
  DashboardScanAccepted,
  DashboardScanStatus,
  DashboardHistoryResponse,
  DashboardHazardReport,
} from '@/types/api';

/** DashboardAssetItem 的顶层响应包装 */
interface DashboardAssetsResponse {
  items: DashboardAssetItem[];
  summary: {
    totalAssets: number;
    checkedAssets: number;
    compliantAssets: number;
    nonCompliantAssets: number;
    uncheckedAssets: number;
  };
}

export const dashboardApi = {
  /** 合规总览概览 —— 资产/发现/整改率聚合指标 */
  overview: () => get<DashboardOverview>('/api/Dashboard/overview'),

  /** 化学品资产台账 —— 园区内所有化学资产清单及合规状态 */
  assets: () => get<DashboardAssetsResponse>('/api/Dashboard/assets'),

  /** 自动合规扫描 —— [#4] 后台任务化：启动返回 202 { scanId }（需 Auditor+ 角色） */
  scan: () => post<DashboardScanAccepted>('/api/Dashboard/scan'),

  /** 扫描进度查询 —— [#4] 2s 轮询直至 running=false */
  scanStatus: () => get<DashboardScanStatus>('/api/Dashboard/scan/status'),

  /** 合规发现列表 —— 支持按严重级别和状态筛选 */
  findings: (params?: { severity?: string; status?: string; openOnly?: boolean }) =>
    get<DashboardFindingsResponse>('/api/Dashboard/findings', { params }),

  /** 历史巡检记录 —— 巡检计划及其关联轮次记录 */
  history: () => get<DashboardHistoryResponse>('/api/Dashboard/history'),

  /** 安全隐患报告 —— 所有未关闭合规发现，按严重级别排序 */
  hazardReport: () => get<DashboardHazardReport>('/api/Dashboard/report/hazard'),
};
