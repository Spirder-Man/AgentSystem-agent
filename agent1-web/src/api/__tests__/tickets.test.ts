// ============================================================
// P1-2: Tickets API 单元测试
// ============================================================

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { ticketsApi } from '../tickets';

const server = setupServer(
  http.get('/api/Tickets', () => {
    return HttpResponse.json({
      total: 3,
      open: 2,
      tickets: [
        {
          id: 1,
          issue: '仓库安全距离不足',
          action: '调整储存位置',
          priority: 'High',
          status: 'New',
          assignee: '张三',
          regulationRef: 'GB 50160-2008',
          suggestedDeadline: '2026-08-01T00:00:00Z',
          isOpen: true,
          logCount: 0,
        },
        {
          id: 2,
          issue: 'MSDS 缺失',
          action: '补充安全数据表',
          priority: 'Medium',
          status: 'InProgress',
          assignee: '李四',
          regulationRef: 'GB/T 16483-2008',
          suggestedDeadline: '2026-07-20T00:00:00Z',
          isOpen: true,
          logCount: 1,
        },
      ],
    });
  }),

  http.put('/api/Tickets/:id/status', async ({ params, request }) => {
    const id = Number(params.id);
    const body = (await request.json()) as { action: string; assignee?: string };
    return HttpResponse.json({
      id,
      issue: '测试工单',
      action: body.action,
      priority: 'High',
      status: body.action === 'complete' ? 'Completed' : 'InProgress',
      assignee: body.assignee ?? 'test',
      regulationRef: 'GB 50160',
      suggestedDeadline: '2026-08-01',
      isOpen: body.action !== 'close',
      logCount: 0,
    });
  }),

  http.post('/api/Tickets/followup', async ({ request }) => {
    const body = (await request.json()) as { complianceResult: string };
    return HttpResponse.json({
      tickets: [
        {
          id: 1,
          issue: '已完成整改',
          action: body.complianceResult,
          priority: 'High',
          status: 'Closed' as const,
          assignee: 'admin',
          regulationRef: 'GB 50160',
          suggestedDeadline: '2026-08-01',
          isOpen: false,
          logCount: 0,
        },
      ],
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe('ticketsApi — 端点路径', () => {
  it('list() 请求 GET /api/Tickets', async () => {
    const result = await ticketsApi.list();
    expect(result.total).toBe(3);
    expect(result.open).toBe(2);
    expect(result.tickets).toHaveLength(2);
    expect(result.tickets[0].status).toBe('New');
  });

  it('updateStatus() 请求 PUT /api/Tickets/:id/status', async () => {
    const result = await ticketsApi.updateStatus(1, { action: 'complete' });
    expect(result.id).toBe(1);
    expect(result.status).toBe('Completed');
  });

  it('followup() 请求 POST /api/Tickets/followup', async () => {
    const result = await ticketsApi.followup({ complianceResult: '已按要求整改' });
    expect(result.tickets).toHaveLength(1);
    expect(result.tickets[0].status).toBe('Closed');
  });
});
