// ============================================================
// P1-2: Inspection API 单元测试
// ============================================================

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { inspectionApi } from '../inspection';

const server = setupServer(
  http.get('/api/Inspection/plans', () => {
    return HttpResponse.json([
      {
        planId: 'plan-1',
        name: '每日巡检计划',
        area: '甲类仓库',
        inspector: 'auditor',
        status: 'InProgress',
        items: 5,
        createdAt: '2026-07-01T08:00:00Z',
      },
    ]);
  }),

  http.post('/api/Inspection/plans', async ({ request }) => {
    const body = (await request.json()) as { name: string; items: unknown[] };
    return HttpResponse.json({
      planId: 'plan-new',
      name: body.name,
      area: '',
      type: '',
      inspector: 'auditor',
      status: 'Draft',
      scheduledDate: '',
      createdAt: new Date().toISOString(),
      notes: '',
      items: [],
    });
  }),

  http.get('/api/Inspection/plans/:planId', ({ params }) => {
    const { planId } = params;
    return HttpResponse.json({
      planId,
      name: '测试计划',
      area: '乙类仓库',
      type: 'DailyWeekly',
      inspector: 'auditor',
      status: 'Draft',
      scheduledDate: '2026-07-16',
      createdAt: '2026-07-01T08:00:00Z',
      notes: '',
      items: [],
    });
  }),

  http.get('/api/Inspection/rounds', ({ request }) => {
    const url = new URL(request.url);
    const planId = url.searchParams.get('planId');
    return HttpResponse.json([
      {
        roundId: 'round-1',
        planId: planId ?? 'plan-1',
        planName: '测试计划',
        complianceRate: 0.8,
        compliantCount: 4,
        nonCompliantCount: 1,
        ticketCount: 0,
        warningCount: 2,
        totalElapsedMs: 5000,
        executedBy: 'auditor',
        startedAt: '2026-07-16T09:00:00Z',
        completedAt: '2026-07-16T09:05:00Z',
      },
    ]);
  }),

  http.get('/api/Inspection/assets', () => {
    return HttpResponse.json([
      {
        assetId: 'asset-1',
        name: '苯储罐',
        casNumber: '71-43-2',
        location: '甲类仓库',
        quantityTons: 50,
        storageCondition: '常温常压',
        responsiblePerson: '张三',
        isMajorHazardSource: true,
        lastCheckResult: true,
        lastCheckedAt: null,
      },
    ]);
  }),

  http.post('/api/Inspection/scan', () => {
    return HttpResponse.json({
      scannedAt: new Date().toISOString(),
      totalAssets: 3,
      checkedAssets: 3,
      totalFindings: 1,
      newFindings: 0,
      findings: [],
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe('inspectionApi — 端点路径', () => {
  it('listPlans() 请求 GET /api/Inspection/plans', async () => {
    const plans = await inspectionApi.listPlans();
    expect(plans).toHaveLength(1);
    expect(plans[0].planId).toBe('plan-1');
    expect(plans[0].items).toBe(5);
  });

  it('createPlan() 请求 POST /api/Inspection/plans', async () => {
    const plan = await inspectionApi.createPlan({
      name: '新计划',
      items: [{ query: '检查安全距离' }],
    });
    expect(plan.name).toBe('新计划');
    expect(plan.status).toBe('Draft');
  });

  it('getPlan() 请求 GET /api/Inspection/plans/:id', async () => {
    const plan = await inspectionApi.getPlan('plan-1');
    expect(plan.planId).toBe('plan-1');
    expect(plan.area).toBe('乙类仓库');
  });

  it('listRounds() 请求 GET /api/Inspection/rounds', async () => {
    const rounds = await inspectionApi.listRounds('plan-1');
    expect(rounds).toHaveLength(1);
    expect(rounds[0].complianceRate).toBe(0.8);
  });

  it('listAssets() 请求 GET /api/Inspection/assets', async () => {
    const assets = await inspectionApi.listAssets();
    expect(assets).toHaveLength(1);
    expect(assets[0].name).toBe('苯储罐');
  });

  it('scan() 请求 POST /api/Inspection/scan', async () => {
    const result = await inspectionApi.scan();
    expect(result.totalAssets).toBe(3);
    expect(result.checkedAssets).toBe(3);
  });
});
