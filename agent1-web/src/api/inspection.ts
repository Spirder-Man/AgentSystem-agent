import { get, post, put, del } from './client';
import type {
  CreatePlanRequest,
  InspectionPlanListItem,
  InspectionPlan,
  InspectionRoundListItem,
  InspectionRoundDetail,
  InspectionReport,
  ChemicalAsset,
  ScanResult,
  QuickCheckRequest,
  QuickCheckResult,
} from '../types/api';

export const inspectionApi = {
  /** 创建巡检计划 */
  createPlan: (data: CreatePlanRequest) =>
    post<InspectionPlan>('/api/Inspection/plans', data),

  /** 获取巡检计划列表 */
  listPlans: () =>
    get<InspectionPlanListItem[]>('/api/Inspection/plans'),

  /** 获取巡检计划详情 */
  getPlan: (planId: string) =>
    get<InspectionPlan>(`/api/Inspection/plans/${planId}`),

  /** 删除巡检计划 */
  deletePlan: (planId: string) =>
    del<void>(`/api/Inspection/plans/${planId}`),

  /** 更新巡检计划 */
  updatePlan: (planId: string, data: Partial<CreatePlanRequest>) =>
    put<InspectionPlan>(`/api/Inspection/plans/${planId}`, data),

  /** 执行巡检计划 */
  executePlan: (planId: string) =>
    post<InspectionRoundListItem>(`/api/Inspection/plans/${planId}/execute`),

  /** 获取巡检轮次列表 */
  listRounds: (planId?: string) => {
    const params = planId ? { planId } : undefined;
    return get<InspectionRoundListItem[]>('/api/Inspection/rounds', { params });
  },

  /** 获取巡检轮次详情 */
  getRound: (roundId: string) =>
    get<InspectionRoundDetail>(`/api/Inspection/rounds/${roundId}`),

  /** 获取巡检报告 */
  getReport: (roundId: string) =>
    get<InspectionReport>(`/api/Inspection/reports/${roundId}`),

  /** 导出巡检报告 */
  exportReport: (roundId: string) =>
    get<Blob>(`/api/Inspection/reports/${roundId}/export`, { responseType: 'blob' }),

  /** 获取资产列表 */
  listAssets: () =>
    get<ChemicalAsset[]>('/api/Inspection/assets'),

  /** 获取资产详情 */
  getAsset: (assetId: string) =>
    get<ChemicalAsset>(`/api/Inspection/assets/${assetId}`),

  /** 触发扫描 */
  scan: () =>
    post<ScanResult>('/api/Inspection/scan'),

  /** 快速预检 */
  quickCheck: (data: QuickCheckRequest) =>
    post<QuickCheckResult>('/api/Inspection/quick-check', data),
};
