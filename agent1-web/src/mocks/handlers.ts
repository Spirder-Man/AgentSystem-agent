// ============================================================
// MSW Mock API Handlers — 前端独立开发的网络层
//
// 每一个 handler 对应后端一个 API 端点。
// 切换模式:
//   开发:  VITE_ENABLE_MOCK=true  → MSW 拦截请求
//   集成:  VITE_ENABLE_MOCK=false → 请求直达 Agent1.Api
// ============================================================

import { http, HttpResponse, delay } from 'msw';
import type {
  LoginRequest, LoginResponse, RefreshRequest,
  ComplianceRequest, ComplianceResponse,
  HazardQueryRequest, HazardQueryResponse,
  StorageCompatibilityRequest, StorageCompatibilityResponse,
  ComplianceSummary,
  CreatePlanRequest, InspectionPlan, InspectionRound, InspectionReport, ChemicalAsset,
  ScanResult, QuickCheckRequest, QuickCheckResult,
  TicketListResponse, TicketItem, TicketStatusUpdateRequest,
  HealthStatus, ApiError,
} from '../types/api';
import { mockComplianceSummary, getComplianceResponse, getHazardResponse, getStorageCompatibilityResponse } from './data/compliance';
import { mockPlans, getMockRound, getMockReport, mockAssets, mockScanResult, getMockQuickCheck } from './data/inspection';
import { mockTicketList, applyTicketStatusUpdate } from './data/tickets';

// ═══════════════════════════════════════
// 工具函数
// ═══════════════════════════════════════

/** 模拟 LLM 推理延迟（2-5 秒随机，80% 概率 2-3 秒） */
async function simulateLlmDelay(): Promise<void> {
  const baseDelay = Math.random() < 0.8 ? 2000 : 4000;
  const jitter = Math.random() * 1000;
  await delay(baseDelay + jitter);
}

/** 模拟 5% 概率的服务器错误（测试错误处理） */
function maybeSimulateError(): ApiError | null {
  if (Math.random() < 0.05) {
    return { error: '服务暂时不可用，请稍后重试', retryAfter: 5 };
  }
  return null;
}

// ═══════════════════════════════════════
// Auth 认证
// ═══════════════════════════════════════

export const handlers = [

  // ── POST /api/Auth/login ──
  http.post('/api/Auth/login', async ({ request }) => {
    await delay(300); // 网络延迟模拟
    const body = await request.json() as LoginRequest;

    if (!body.username?.trim() || !body.password?.trim()) {
      return HttpResponse.json<ApiError>(
        { error: '用户名和密码不能为空' },
        { status: 400 }
      );
    }

    // 允许任意用户名+密码组合（开发阶段），真实 API 会验证账号
    const role = body.username.includes('admin') ? 'admin'
      : body.username.includes('auditor') ? 'auditor'
      : 'viewer';

    return HttpResponse.json<LoginResponse>({
      token: `mock-jwt-${role}-${Date.now()}`,
      refreshToken: `mock-refresh-${Date.now()}`,
      username: body.username,
      role,
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  // ── POST /api/Auth/refresh ──
  http.post('/api/Auth/refresh', async ({ request }) => {
    await delay(200);
    const body = await request.json() as RefreshRequest;

    if (!body.refreshToken?.trim()) {
      return HttpResponse.json<ApiError>(
        { error: 'RefreshToken 不能为空' },
        { status: 400 }
      );
    }

    return HttpResponse.json<LoginResponse>({
      token: `mock-jwt-refreshed-${Date.now()}`,
      refreshToken: `mock-refresh-new-${Date.now()}`,
      username: 'admin',
      role: 'admin',
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  // ── POST /api/Auth/logout ──
  http.post('/api/Auth/logout', async () => {
    await delay(100);
    return HttpResponse.json({ message: '已登出' });
  }),

  // ═══════════════════════════════════════
  // Compliance 合规审核
  // ═══════════════════════════════════════

  // ── GET /api/Compliance/summary ──
  http.get('/api/Compliance/summary', async () => {
    await delay(200);
    return HttpResponse.json<ComplianceSummary>(mockComplianceSummary);
  }),

  // ── POST /api/Compliance/check ── (核心: 含 LLM 推理延迟)
  http.post('/api/Compliance/check', async ({ request }) => {
    const error = maybeSimulateError();
    if (error) return HttpResponse.json(error, { status: 503 });

    const body = await request.json() as ComplianceRequest;

    // ⚡ 模拟真实 LLM 推理延迟 — 前端进度条的核心测试场景
    await simulateLlmDelay();

    const result = getComplianceResponse(body.query);
    return HttpResponse.json<ComplianceResponse>(result);
  }),

  // ── POST /api/Compliance/hazard/query ──
  http.post('/api/Compliance/hazard/query', async ({ request }) => {
    await delay(500);
    const body = await request.json() as HazardQueryRequest;
    const result = getHazardResponse(body.substanceName);
    return HttpResponse.json<HazardQueryResponse>(result);
  }),

  // ── POST /api/Compliance/storage/compatibility ──
  http.post('/api/Compliance/storage/compatibility', async ({ request }) => {
    await delay(500);
    const body = await request.json() as StorageCompatibilityRequest;
    const result = getStorageCompatibilityResponse(body.substanceA, body.substanceB);
    return HttpResponse.json<StorageCompatibilityResponse>(result);
  }),

  // ═══════════════════════════════════════
  // Inspection 巡检
  // ═══════════════════════════════════════

  // ── GET /api/Inspection/plans ──
  http.get('/api/Inspection/plans', async () => {
    await delay(200);
    return HttpResponse.json<InspectionPlan[]>(mockPlans);
  }),

  // ── POST /api/Inspection/plans ──
  http.post('/api/Inspection/plans', async ({ request }) => {
    await delay(300);
    const body = await request.json() as CreatePlanRequest;
    const newPlan: InspectionPlan = {
      planId: `plan-${Date.now()}`,
      name: body.name,
      area: body.area ?? '未指定',
      type: body.type ?? 'DailyWeekly',
      inspector: '当前用户',
      status: 'Draft',
      scheduledDate: new Date().toISOString(),
      createdAt: new Date().toISOString(),
      notes: body.notes ?? '',
      items: body.items.map((item, idx) => ({
        itemId: idx + 1,
        query: item.query,
        capabilityName: item.capability ?? 'regulatory-audit',
      })),
    };
    mockPlans.unshift(newPlan);
    return HttpResponse.json<InspectionPlan>(newPlan);
  }),

  // ── GET /api/Inspection/plans/:id ──
  http.get('/api/Inspection/plans/:id', async ({ params }) => {
    await delay(200);
    const plan = mockPlans.find((p) => p.planId === params.id);
    if (!plan) {
      return HttpResponse.json<ApiError>({ error: '计划不存在' }, { status: 404 });
    }
    return HttpResponse.json<InspectionPlan>(plan);
  }),

  // ── POST /api/Inspection/plans/:id/execute ──
  http.post('/api/Inspection/plans/:id/execute', async ({ params }) => {
    await simulateLlmDelay(); // 执行巡检需要 LLM 推理
    const round = getMockRound(`round-${Date.now()}`, params.id as string);
    return HttpResponse.json<InspectionRound>(round);
  }),

  // ── GET /api/Inspection/rounds/:id ──
  http.get('/api/Inspection/rounds/:id', async ({ params }) => {
    await delay(200);
    const round = getMockRound(params.id as string, 'plan-001');
    return HttpResponse.json<InspectionRound>(round);
  }),

  // ── GET /api/Inspection/reports/:id ──
  http.get('/api/Inspection/reports/:id', async ({ params }) => {
    await delay(200);
    const report = getMockReport(params.id as string, 'round-001');
    return HttpResponse.json<InspectionReport>(report);
  }),

  // ── GET /api/Inspection/reports/:id/export ──
  http.get('/api/Inspection/reports/:id/export', async ({ params }) => {
    await delay(100);
    const report = getMockReport(params.id as string, 'round-001');
    return HttpResponse.json(report);
  }),

  // ── GET /api/Inspection/assets ──
  http.get('/api/Inspection/assets', async () => {
    await delay(200);
    return HttpResponse.json<ChemicalAsset[]>(mockAssets);
  }),

  // ── POST /api/Inspection/scan ──
  http.post('/api/Inspection/scan', async () => {
    await simulateLlmDelay();
    return HttpResponse.json<ScanResult>(mockScanResult);
  }),

  // ── POST /api/Inspection/quick-check ──
  http.post('/api/Inspection/quick-check', async ({ request }) => {
    await delay(1500);
    const body = await request.json() as QuickCheckRequest;
    const result = getMockQuickCheck(body.query);
    return HttpResponse.json<QuickCheckResult>(result);
  }),

  // ═══════════════════════════════════════
  // Tickets 工单
  // ═══════════════════════════════════════

  // ── GET /api/Tickets ──
  http.get('/api/Tickets', async () => {
    await delay(200);
    return HttpResponse.json<TicketListResponse>(mockTicketList);
  }),

  // ── PUT /api/Tickets/:id/status ──
  http.put('/api/Tickets/:id/status', async ({ params, request }) => {
    await delay(300);
    const body = await request.json() as TicketStatusUpdateRequest;
    const ticketId = Number(params.id);

    const updated = applyTicketStatusUpdate(ticketId, body.action, body.assignee);
    if (!updated) {
      return HttpResponse.json<ApiError>(
        { error: '工单不存在或状态流转不合法' },
        { status: 400 }
      );
    }
    return HttpResponse.json<TicketItem>(updated);
  }),

  // ═══════════════════════════════════════
  // Health & Infrastructure
  // ═══════════════════════════════════════

  // ── GET /health ──
  http.get('/health', async () => {
    await delay(50);
    return HttpResponse.json<HealthStatus>({
      status: 'healthy',
      timestamp: new Date().toISOString(),
      version: '2.5.0-mock',
      checks: {
        database: 'connected',
        ollama: 'reachable',
        knowledge_base_docs: 156,
        llm_calls: 1234,
        llm_error_rate: '2.1%',
      },
    });
  }),

  // ── GET /health/ready ── (K8s readiness probe)
  http.get('/health/ready', async () => {
    return HttpResponse.json({ ready: true });
  }),

  // ── GET /health/live ── (K8s liveness probe)
  http.get('/health/live', async () => {
    return HttpResponse.json({ alive: true });
  }),

  // ── GET /metrics ──
  http.get('/metrics', async () => {
    return new HttpResponse(
      `# HELP agent1_llm_calls_total Total LLM calls
# TYPE agent1_llm_calls_total counter
agent1_llm_calls_total 1234

# HELP agent1_llm_duration_ms_avg Average LLM duration
# TYPE agent1_llm_duration_ms_avg gauge
agent1_llm_duration_ms_avg 4500

# HELP agent1_api_requests_total Total API requests
# TYPE agent1_api_requests_total counter
agent1_api_requests_total 5678`,
      {
        headers: { 'Content-Type': 'text/plain; version=0.0.4' },
      }
    );
  }),

  // ── GET /cache/stats ──
  http.get('/cache/stats', async () => {
    return HttpResponse.json({
      entries: 45,
      hits: 320,
      misses: 98,
      hitRate: 0.766,
      maxEntries: 500,
      ttlMinutes: 5,
    });
  }),

  // ── POST /cache/clear ──
  http.post('/cache/clear', async () => {
    return HttpResponse.json({ message: '缓存已清除', clearedEntries: 45 });
  }),

  // ── POST /knowledgebase/incremental-update ──
  http.post('/knowledgebase/incremental-update', async () => {
    await delay(2000); // 模拟知识库更新
    return HttpResponse.json({
      message: '知识库增量更新完成',
      addedDocuments: 3,
      removedDocuments: 0,
      totalDocuments: 159,
    });
  }),

  // ── GET /memory/stats ──
  http.get('/memory/stats', async () => {
    return HttpResponse.json({
      sessionCount: 12,
      totalFacts: 87,
      totalDialogTurns: 456,
      cacheHitRate: 0.72,
    });
  }),

  // ── GET /memory/long-term/search ──
  http.get('/memory/long-term/search', async ({ request: req }) => {
    const url = new URL(req.url);
    const q = url.searchParams.get('q') ?? '';
    return HttpResponse.json({
      query: q,
      results: q
        ? [
            { fact: '苯的CAS号为71-43-2', confidence: 0.95, source: '历史查询' },
            { fact: '甲类仓库与明火点安全距离为30米', confidence: 0.88, source: '上次合规审核' },
          ]
        : [],
    });
  }),
];
