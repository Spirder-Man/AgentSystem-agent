// ============================================================
// FE-1: 应急响应 API 模块
// 对齐后端 EmergencyController 端点。
// ============================================================

import { post } from './client';
import type { EmergencyRequest, EmergencyResult } from '@/types/api';

/** 扩展请求参数 —— 补充前端类型中缺失的 quantityKg 字段 */
export interface EmergencyResponseRequest extends EmergencyRequest {
  quantityKg?: number;
}

export const emergencyApi = {
  /** 生成应急响应方案（泄漏/火灾/爆炸/中毒） */
  generateResponse: (data: EmergencyResponseRequest) =>
    post<EmergencyResult>('/api/Emergency/response', data),
};
