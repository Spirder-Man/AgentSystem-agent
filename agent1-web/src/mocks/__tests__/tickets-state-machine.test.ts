/**
 * P4-2: Tickets 状态机深度测试
 *
 * 对齐后端 TicketFollowupModule.TicketStatus 枚举:
 *   New → Accepted → InProgress → Completed → Verified
 *   New → Rejected
 *   Accepted/InProgress → Closed
 *
 * 覆盖:
 *   - 所有合法状态流转 (6 paths)
 *   - 所有非法状态流转 (边界)
 *   - isOpen 标志在终态自动关闭
 *   - assignee 赋值
 *   - logCount 递增
 *   - 不存在工单 → null
 */
import { describe, it, expect, beforeEach } from 'vitest';
import type { TicketItem } from '../../types/api';
import { mockTickets, applyTicketStatusUpdate } from '../data/tickets';

// 每个测试前重置工单到初始状态
const initialTickets: TicketItem[] = JSON.parse(JSON.stringify(mockTickets));

beforeEach(() => {
  // 深拷贝重置
  mockTickets.length = 0;
  mockTickets.push(
    ...JSON.parse(JSON.stringify(initialTickets))
  );
});

// ═══════════════════════════════════════
// 完整正向状态流转路径
// ═══════════════════════════════════════

describe('完整正向流转: New → Accepted → InProgress → Completed → Verified', () => {
  it('New → accept → Accepted', () => {
    const ticket = applyTicketStatusUpdate(1, 'accept', '张三');
    expect(ticket).not.toBeNull();
    expect(ticket!.status).toBe('Accepted');
    expect(ticket!.assignee).toBe('张三');
    expect(ticket!.logCount).toBe(1);
    expect(ticket!.isOpen).toBe(true);
  });

  it('Accepted → start → InProgress', () => {
    applyTicketStatusUpdate(1, 'accept');
    const ticket = applyTicketStatusUpdate(1, 'start');
    expect(ticket!.status).toBe('InProgress');
    expect(ticket!.logCount).toBe(2);
    expect(ticket!.isOpen).toBe(true);
  });

  it('InProgress → complete → Completed', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'start');
    const ticket = applyTicketStatusUpdate(1, 'complete');
    expect(ticket!.status).toBe('Completed');
    expect(ticket!.logCount).toBe(3);
    expect(ticket!.isOpen).toBe(true); // Completed 未关闭
  });

  it('Completed → verify → Verified (终态 → isOpen=false)', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'start');
    applyTicketStatusUpdate(1, 'complete');
    const ticket = applyTicketStatusUpdate(1, 'verify');
    expect(ticket!.status).toBe('Verified');
    expect(ticket!.logCount).toBe(4);
    expect(ticket!.isOpen).toBe(false); // 终态关闭
  });
});

// ═══════════════════════════════════════
// 拒绝 + 关闭路径
// ═══════════════════════════════════════

describe('拒绝与强制关闭', () => {
  it('New → reject → Rejected (终态)', () => {
    const ticket = applyTicketStatusUpdate(1, 'reject', '管理员');
    expect(ticket!.status).toBe('Rejected');
    expect(ticket!.isOpen).toBe(false);
  });

  it('Accepted → close → Closed (终态)', () => {
    applyTicketStatusUpdate(1, 'accept');
    const ticket = applyTicketStatusUpdate(1, 'close');
    expect(ticket!.status).toBe('Closed');
    expect(ticket!.isOpen).toBe(false);
  });

  it('InProgress → close → Closed (终态)', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'start');
    const ticket = applyTicketStatusUpdate(1, 'close');
    expect(ticket!.status).toBe('Closed');
    expect(ticket!.isOpen).toBe(false);
  });
});

// ═══════════════════════════════════════
// 非法状态流转 (边界)
// ═══════════════════════════════════════

describe('非法状态流转', () => {
  it('Verified 工单不能再流转', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'start');
    applyTicketStatusUpdate(1, 'complete');
    applyTicketStatusUpdate(1, 'verify');
    expect(applyTicketStatusUpdate(1, 'verify')).toBeNull();
    expect(applyTicketStatusUpdate(1, 'accept')).toBeNull();
  });

  it('Closed 工单不能再流转', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'close');
    expect(applyTicketStatusUpdate(1, 'start')).toBeNull();
    expect(applyTicketStatusUpdate(1, 'reject')).toBeNull();
  });

  it('Rejected 工单不能再流转', () => {
    applyTicketStatusUpdate(1, 'reject');
    expect(applyTicketStatusUpdate(1, 'accept')).toBeNull();
    expect(applyTicketStatusUpdate(1, 'start')).toBeNull();
  });

  it('Accepted 不能直接 → Completed (缺少 InProgress)', () => {
    applyTicketStatusUpdate(1, 'accept');
    expect(applyTicketStatusUpdate(1, 'complete')).toBeNull();
  });

  it('New 不能 → Verified (越级)', () => {
    expect(applyTicketStatusUpdate(1, 'verify')).toBeNull();
  });

  it('InProgress 不能 → reject (只有 New 可 reject)', () => {
    applyTicketStatusUpdate(1, 'accept');
    applyTicketStatusUpdate(1, 'start');
    expect(applyTicketStatusUpdate(1, 'reject')).toBeNull();
  });

  it('不存在工单 ID → null', () => {
    expect(applyTicketStatusUpdate(999, 'accept')).toBeNull();
  });

  it('无效 action → null', () => {
    expect(applyTicketStatusUpdate(1, 'invalid_action' as any)).toBeNull();
  });
});

// ═══════════════════════════════════════
// assignee 与 logCount
// ═══════════════════════════════════════

describe('assignee 与 logCount 行为', () => {
  it('无 assignee 时状态流转成功但 assignee 不变', () => {
    applyTicketStatusUpdate(1, 'accept');
    const ticket = mockTickets.find((t) => t.id === 1)!;
    expect(ticket.status).toBe('Accepted');
    expect(ticket.assignee).toBe(''); // 原来的 ''

    applyTicketStatusUpdate(1, 'start', '李四');
    expect(ticket.assignee).toBe('李四');
  });

  it('logCount 每次成功流转 +1', () => {
    expect(mockTickets.find((t) => t.id === 1)!.logCount).toBe(0);
    applyTicketStatusUpdate(1, 'accept');
    expect(mockTickets.find((t) => t.id === 1)!.logCount).toBe(1);
    applyTicketStatusUpdate(1, 'start');
    expect(mockTickets.find((t) => t.id === 1)!.logCount).toBe(2);
  });

  it('非法流转不增加 logCount', () => {
    const before = mockTickets.find((t) => t.id === 1)!.logCount;
    applyTicketStatusUpdate(1, 'verify'); // 非法 → null
    expect(mockTickets.find((t) => t.id === 1)!.logCount).toBe(before);
  });
});

// ═══════════════════════════════════════
// 所有工单初始状态验证
// ═══════════════════════════════════════

describe('工单初始数据集完整性', () => {
  it('mockTickets 共 5 条', () => {
    expect(mockTickets.length).toBe(5);
  });

  it('每条工单都有关键字段', () => {
    for (const t of mockTickets) {
      expect(t.id).toBeGreaterThan(0);
      expect(t.issue).toBeTruthy();
      expect(t.action).toBeTruthy();
      expect(t.priority).toMatch(/^(Critical|High|Medium|Low|Info)$/);
      expect(t.status).toMatch(
        /^(New|Accepted|InProgress|Completed|Verified|Closed|Rejected)$/
      );
      expect(t.regulationRef).toBeTruthy();
      expect(t.suggestedDeadline).toBeDefined();
      expect(typeof t.isOpen).toBe('boolean');
      expect(typeof t.logCount).toBe('number');
    }
  });
});
