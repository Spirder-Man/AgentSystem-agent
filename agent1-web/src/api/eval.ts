// ============================================================
// P2: 合规评测 API 模块
// 对齐后端 EvalController 端点。
// ============================================================

import { get, post, del } from './client';
import type { EvalRunResponse, EvalTaskStatus } from '../types/api';

export const evalApi = {
  /** 启动合规评测任务（异步后台执行 50 条业务评测） */
  run: () => post<EvalRunResponse>('/api/Eval/run'),

  /** 查询评测任务状态与结果 */
  getStatus: (taskId: string) =>
    get<EvalTaskStatus>(`/api/Eval/status/${taskId}`),

  /** 取消正在运行的评测任务 */
  cancel: (taskId: string) =>
    del<{ taskId: string; cancelled: boolean }>(
      `/api/Eval/status/${taskId}`,
    ),
};
