// ============================================================
// 整改工单 Mock 数据
// ============================================================

import type { TicketListResponse, TicketItem } from '../../types/api';

export const mockTickets: TicketItem[] = [
  {
    id: 1,
    issue: '苯与丙酮同库储存违规',
    action: '立即分库储存 — 苯移至甲类仓库A区1号位，丙酮移至A区2号位',
    priority: 'Critical',
    status: 'New',
    assignee: '',
    regulationRef: 'GB 15603-2022 §4.2.2',
    suggestedDeadline: '2026-06-25T00:00:00Z',
    isOpen: true,
    logCount: 0,
  },
  {
    id: 2,
    issue: '甲醇存量超临界量80%',
    action: '降低甲醇存量至临界量500吨的50%以下，或提交重大危险源备案',
    priority: 'High',
    status: 'Confirmed',
    assignee: '李四',
    regulationRef: 'GB 18218-2018',
    suggestedDeadline: '2026-07-01T00:00:00Z',
    isOpen: true,
    logCount: 1,
  },
  {
    id: 3,
    issue: '消防通道标识不清',
    action: '按 GB 50016 §7.1.8 要求重新划设消防通道标识线和警示牌',
    priority: 'Medium',
    status: 'InProgress',
    assignee: '王五',
    regulationRef: 'GB 50016 §7.1.8',
    suggestedDeadline: '2026-07-15T00:00:00Z',
    isOpen: false,
    logCount: 2,
  },
  {
    id: 4,
    issue: '甲类仓库通风系统老化',
    action: '更换甲类仓库A区和B区的防爆通风机，确保换气次数 ≥ 12次/小时',
    priority: 'High',
    status: 'Confirmed',
    assignee: '张三',
    regulationRef: 'GB 15603-2022 §5.1.3',
    suggestedDeadline: '2026-07-10T00:00:00Z',
    isOpen: true,
    logCount: 0,
  },
  {
    id: 5,
    issue: '储罐区防雷接地电阻超标',
    action: '重新敷设接地极，确保接地电阻 ≤ 4Ω，委托第三方检测',
    priority: 'High',
    status: 'InProgress',
    assignee: '赵六',
    regulationRef: 'GB 50057-2010',
    suggestedDeadline: '2026-07-05T00:00:00Z',
    isOpen: true,
    logCount: 3,
  },
];

export const mockTicketList: TicketListResponse = {
  total: mockTickets.length,
  open: mockTickets.filter((t) => t.isOpen).length,
  tickets: mockTickets,
};

// ── 状态流转引擎 ──
// New → Confirmed → InProgress → Remediated → VerifiedClosed
//                    ↘ Closed (无效工单)
// New → FalsePositive

const stateTransitions: Record<string, string[]> = {
  New: ['Confirmed', 'FalsePositive'],
  Confirmed: ['InProgress', 'Closed'],
  InProgress: ['Remediated', 'Closed'],
  Remediated: ['VerifiedClosed'],
  VerifiedClosed: [],
  Closed: [],
  FalsePositive: [],
};

export function applyTicketStatusUpdate(
  ticketId: number,
  action: string,
  assignee?: string
): TicketItem | null {
  const ticket = mockTickets.find((t) => t.id === ticketId);
  if (!ticket) return null;

  const allowedNext = stateTransitions[ticket.status];
  if (!allowedNext) return null;

  // action → 目标状态映射
  const actionToStatus: Record<string, string> = {
    accept: 'Confirmed',
    start: 'InProgress',
    complete: 'Remediated',
    verify: 'VerifiedClosed',
    close: 'Closed',
    reject: 'FalsePositive',
  };

  const newStatus = actionToStatus[action];
  if (!newStatus || !allowedNext.includes(newStatus)) return null;

  ticket.status = newStatus;
  ticket.logCount += 1;
  if (assignee) ticket.assignee = assignee;

  return ticket;
}
