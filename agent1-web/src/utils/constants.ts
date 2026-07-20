// ============================================================
// Agent1 前端常量定义 — 状态映射、颜色映射、枚举值
// ============================================================

import type { TicketStatus } from '@/types/api';
import type { TicketStatusUpdateRequest } from '@/types/api';

// ═══════════════════════════════════════
// 工单状态 → 操作按钮映射
// 对齐后端 TicketFollowupModule.TicketItem 状态机:
//   New → Accepted → InProgress → Completed → Verified
//   New → Rejected   Accepted/InProgress → Closed
// ═══════════════════════════════════════

interface TicketActionItem {
  action: TicketStatusUpdateRequest['action'];
  label: string;
  type: 'primary' | 'success' | 'warning' | 'danger' | 'info';
}

export const TICKET_ACTIONS_BY_STATUS: Record<TicketStatus, TicketActionItem[]> = {
  New: [
    { action: 'accept', label: '受理', type: 'primary' },
    { action: 'reject', label: '驳回', type: 'danger' },
  ],
  Accepted: [
    { action: 'start', label: '开始处理', type: 'primary' },
    { action: 'close', label: '关闭工单', type: 'info' },
  ],
  InProgress: [
    { action: 'complete', label: '完成整改', type: 'success' },
    { action: 'close', label: '关闭工单', type: 'info' },
  ],
  Completed: [
    { action: 'verify', label: '验收通过', type: 'success' },
  ],
  Verified: [],
  Closed: [],
  Rejected: [],
};

// ═══════════════════════════════════════
// 工单状态 → Element Plus Tag 颜色映射
// ═══════════════════════════════════════

export const TICKET_STATUS_COLOR_MAP: Record<TicketStatus, string> = {
  New: 'info',
  Accepted: 'warning',
  InProgress: '',
  Completed: 'success',
  Verified: 'success',
  Closed: 'info',
  Rejected: 'danger',
};

// ═══════════════════════════════════════
// 工单状态 → 中文展示名
// ═══════════════════════════════════════

export const TICKET_STATUS_LABEL_MAP: Record<TicketStatus, string> = {
  New: '新建',
  Accepted: '已受理',
  InProgress: '处理中',
  Completed: '已整改',
  Verified: '已验证',
  Closed: '已关闭',
  Rejected: '已驳回',
};

// ═══════════════════════════════════════
// action → 目标状态映射 (用于 Mock/乐观更新)
// ═══════════════════════════════════════

export const ACTION_TO_STATUS: Record<TicketStatusUpdateRequest['action'], TicketStatus> = {
  accept: 'Accepted',
  start: 'InProgress',
  complete: 'Completed',
  verify: 'Verified',
  close: 'Closed',
  reject: 'Rejected',
};

// ═══════════════════════════════════════
// 终态列表 (这些状态下 isOpen = false)
// ═══════════════════════════════════════

export const TERMINAL_TICKET_STATUSES: readonly TicketStatus[] = [
  'Verified',
  'Closed',
  'Rejected',
] as const;

/** 判断给定状态是否为终态 */
export function isTerminalStatus(status: TicketStatus): boolean {
  return (TERMINAL_TICKET_STATUSES as readonly string[]).includes(status);
}

// ═══════════════════════════════════════
// 工单优先级
// ═══════════════════════════════════════

export const TICKET_PRIORITY_OPTIONS = [
  { value: 'Critical', label: '严重' },
  { value: 'High', label: '高' },
  { value: 'Medium', label: '中' },
  { value: 'Low', label: '低' },
] as const;

export const TICKET_PRIORITY_COLOR_MAP: Record<string, string> = {
  Critical: 'danger',
  High: 'warning',
  Medium: '',
  Low: 'info',
};
