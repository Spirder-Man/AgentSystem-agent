// ============================================================
// FE-1: 合规审核 API 单元测试
//
// 验证:
//   - complianceApi.check() 请求命中 /api/compliance/check + POST
//   - complianceApi 其他端点路径正确
//   - 401 错误经 axios 拦截器传播为 rejected Promise
// ============================================================

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type { ComplianceRequest, ComplianceResponse } from '@/types/api';
import { complianceApi } from '../compliance';

// ═══════════════════════════════════════
// MSW Server
// ═══════════════════════════════════════

const server = setupServer(
  // GET /api/compliance/summary
  http.get('/api/compliance/summary', () => {
    return HttpResponse.json({
      totalAssets: 12,
      checkedAssets: 10,
      compliantAssets: 8,
      nonCompliantAssets: 2,
      complianceRate: 0.8,
      totalFindings: 5,
      openFindings: 3,
      remediationRate: 0.4,
      lastAutoScanAt: null,
      findingsBySeverity: { Critical: 1, High: 2, Medium: 2 },
      findingsByStatus: { Open: 3, Closed: 2 },
      riskDistribution: { low: 5, unknown: 2, high: 0, critical: 0 },
    });
  }),

  // POST /api/compliance/check
  http.post('/api/compliance/check', async ({ request }) => {
    const body = (await request.json()) as ComplianceRequest;
    return HttpResponse.json({
      query: body.query,
      response: '合规判断结果…',
      toolsUsed: ['CheckHazardCategory'],
      verifiedRegulations: ['GB 30000.7-2013'],
      hallucinatedRegulations: [],
      warnings: [],
    } satisfies ComplianceResponse);
  }),

  // POST /api/compliance/hazard-query
  http.post('/api/compliance/hazard-query', async ({ request }) => {
    const body = (await request.json()) as { substanceName: string };
    return HttpResponse.json({
      substanceName: body.substanceName,
      response: '危险类别查询结果…',
      toolsUsed: ['CheckHazardCategory'],
    });
  }),

  // POST /api/compliance/storage-compatibility
  http.post('/api/compliance/storage-compatibility', async ({ request }) => {
    const body = (await request.json()) as { substanceA: string; substanceB: string };
    return HttpResponse.json({
      substanceA: body.substanceA,
      substanceB: body.substanceB,
      response: '兼容性判断…',
      toolsUsed: ['CheckStorageCompatibility'],
    });
  }),

  // 401 场景 —— GET /api/compliance/summary?simulate=401
  http.get('/api/compliance/unauthorized', () => {
    return new HttpResponse(
      JSON.stringify({ error: '未授权访问' }),
      { status: 401, headers: { 'Content-Type': 'application/json' } }
    );
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

// ═══════════════════════════════════════
// 端点路径验证
// ═══════════════════════════════════════

describe('complianceApi — 端点路径', () => {
  it('check() 请求 POST /api/compliance/check', async () => {
    const result = await complianceApi.check({ query: '苯属于什么危险类别' });
    expect(result.query).toBe('苯属于什么危险类别');
    expect(result.toolsUsed).toContain('CheckHazardCategory');
    expect(result.verifiedRegulations).toContain('GB 30000.7-2013');
    expect(result.hallucinatedRegulations).toEqual([]);
  });

  it('hazardQuery() 请求 POST /api/compliance/hazard-query', async () => {
    const result = await complianceApi.hazardQuery({ substanceName: '甲醇' });
    expect(result.substanceName).toBe('甲醇');
    expect(result.toolsUsed).toContain('CheckHazardCategory');
  });

  it('storageCompatibility() 请求 POST /api/compliance/storage-compatibility', async () => {
    const result = await complianceApi.storageCompatibility({
      substanceA: '苯',
      substanceB: '丙酮',
    });
    expect(result.substanceA).toBe('苯');
    expect(result.substanceB).toBe('丙酮');
    expect(result.toolsUsed).toContain('CheckStorageCompatibility');
  });

  it('summary() 请求 GET /api/compliance/summary', async () => {
    const result = await complianceApi.summary();
    expect(result.totalAssets).toBe(12);
    expect(result.complianceRate).toBe(0.8);
    expect(result.riskDistribution).toBeDefined();
  });
});

// ═══════════════════════════════════════
// 错误处理
// ═══════════════════════════════════════

describe('complianceApi — 错误处理', () => {
  it('401 响应抛出错误', async () => {
    // 直接用 apiClient 访问未授权端点（走拦截器 401 分支）
    const { apiClient } = await import('../client');
    await expect(
      apiClient.get('/api/compliance/unauthorized')
    ).rejects.toThrow();
  });
});
