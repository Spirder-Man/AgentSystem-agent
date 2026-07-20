// ============================================================
// FE-1: 合规审核 API 模块
// 对齐后端 ComplianceController 端点。
// ============================================================

import { get, post } from './client';
import type {
  ComplianceRequest,
  ComplianceResponse,
  HazardQueryRequest,
  HazardQueryResponse,
  StorageCompatibilityRequest,
  StorageCompatibilityResponse,
  ComplianceSummary,
} from '@/types/api';

export const complianceApi = {
  /** 通用合规审核查询 */
  check: (data: ComplianceRequest) =>
    post<ComplianceResponse>('/api/compliance/check', data),

  /** 危化品危险类别查询 */
  hazardQuery: (data: HazardQueryRequest) =>
    post<HazardQueryResponse>('/api/compliance/hazard-query', data),

  /** 储存配伍禁忌查询 */
  storageCompatibility: (data: StorageCompatibilityRequest) =>
    post<StorageCompatibilityResponse>('/api/compliance/storage-compatibility', data),

  /** 合规总览（仪表盘用） */
  summary: () =>
    get<ComplianceSummary>('/api/compliance/summary'),
};
