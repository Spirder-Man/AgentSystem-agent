// ============================================================
// P2: 系统管理 API 模块
// 对齐后端 AdminController 端点（仅 Admin 角色可访问）。
// ============================================================

import { get } from './client';
import type { DbInfoResponse, DbValidateResponse } from '../types/api';

export const adminApi = {
  /** 获取数据库基本信息 */
  getDbInfo: () => get<DbInfoResponse>('/api/Admin/db/info'),

  /** 数据库连接验证 + 完整诊断信息 */
  validateDb: () => get<DbValidateResponse>('/api/Admin/db/validate'),
};
