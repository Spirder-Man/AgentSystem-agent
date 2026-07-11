// ============================================================
// MSW Mock API Handlers — 生产级网络层模拟
//
// 对齐行为:
//   - JWT 校验 (Authorization header) → 401 缺失/过期
//   - 角色检查 (admin/auditor/viewer)  → 403 viewer 拦截
//   - x-simulate-error header         → 按需触发 400/403/422/429/500/503
//   - LLM 推理延迟 (3–45s)            → 模拟真实推理
//   - 5% 概率 503                     → 测试自动重试
// ============================================================

import { http, HttpResponse, delay } from 'msw';
import type {
  LoginRequest, LoginResponse, RefreshRequest,
  ComplianceRequest, ComplianceResponse,
  HazardQueryRequest, HazardQueryResponse,
  StorageCompatibilityRequest, StorageCompatibilityResponse,
  ComplianceSummary,
  CreatePlanRequest, InspectionPlanListItem, InspectionPlan, InspectionRound, InspectionReport, ChemicalAsset,
  ScanResult, QuickCheckRequest, QuickCheckResult,
  TicketListResponse, TicketStatusUpdateRequest,
  HealthStatus, ApiError,
} from '../types/api';
import { mockComplianceSummary, getComplianceResponse, getHazardResponse, getStorageCompatibilityResponse } from './data/compliance';
import { mockPlans, getMockRound, getMockReport, mockAssets, mockScanResult, getMockQuickCheck } from './data/inspection';
import { mockTicketList, applyTicketStatusUpdate } from './data/tickets';

// ═══════════════════════════════════════
// 类型
// ═══════════════════════════════════════

type UserRole = 'admin' | 'auditor' | 'viewer';

interface MockAuth {
  role: UserRole;
  username: string;
  expired: boolean;
}

// ═══════════════════════════════════════
// JWT 模拟校验
// ═══════════════════════════════════════

function parseAuth(request: Request): MockAuth | null {
  const auth = request.headers.get('Authorization');
  if (!auth || !auth.startsWith('Bearer ')) return null;

  const token = auth.slice(7);
  const match = token.match(/^mock-jwt-(admin|auditor|viewer)-(\d+)$/);
  if (!match) return null;

  const role = match[1] as UserRole;
  const ts = Number(match[2]);
  return {
    role,
    username: role,
    expired: Date.now() - ts > 3_600_000,
  };
}

// ═══════════════════════════════════════
// 错误模拟 (通过 x-simulate-error Header 按需触发)
// ═══════════════════════════════════════

function checkSimulatedError(request: Request): HttpResponse | null {
  const code = request.headers.get('x-simulate-error');
  if (!code) return null;

  const status = Number(code);
  const body: ApiError = { error: '', code: '' };

  switch (status) {
    case 400: body.error = '输入内容不符合要求'; body.code = 'INVALID_INPUT'; body.details = { username: ['用户名格式不正确'] }; break;
    case 403: body.error = '您没有权限执行此操作'; body.code = 'UNAUTHORIZED'; break;
    case 422: body.error = '业务数据校验失败'; body.code = 'VALIDATION_FAILED'; break;
    case 429: body.error = '请求过于频繁，请稍后重试'; body.code = 'RATE_LIMITED'; body.retryAfter = 30; break;
    case 500: body.error = '服务异常，已记录日志'; body.code = 'SERVER_ERROR'; break;
    case 503: body.error = '服务繁忙，请稍后重试'; body.retryAfter = 10; break;
    default: return null;
  }
  return HttpResponse.json(body, { status });
}

// ═══════════════════════════════════════
// Auth Guard: 无 Token → 401, viewer → 403
// ═══════════════════════════════════════

function authGuard(request: Request): HttpResponse | null {
  const auth = parseAuth(request);
  if (!auth) {
    return HttpResponse.json<ApiError>(
      { error: '会话已过期，请重新登录', code: 'SESSION_EXPIRED' },
      { status: 401 }
    );
  }
  if (auth.expired) {
    return HttpResponse.json<ApiError>(
      { error: 'Token 已过期', code: 'SESSION_EXPIRED' },
      { status: 401 }
    );
  }
  if (auth.role === 'viewer') {
    return HttpResponse.json<ApiError>(
      { error: '您没有权限执行此操作', code: 'UNAUTHORIZED' },
      { status: 403 }
    );
  }
  return null;
}

// ═══════════════════════════════════════
// 工具函数
// ═══════════════════════════════════════

async function simulateLlmDelay(): Promise<void> {
  const rand = Math.random();
  const baseDelay = rand < 0.7 ? 3000 + Math.random() * 9000
    : rand < 0.9 ? 12000 + Math.random() * 13000
    : 25000 + Math.random() * 20000;
  await delay(baseDelay + Math.random() * 3000);
}

function maybeSimulateError(): ApiError | null {
  if (Math.random() < 0.05) {
    return { error: '服务繁忙，请稍后重试', retryAfter: 10 };
  }
  return null;
}

// ═══════════════════════════════════════
// Handlers
// ═══════════════════════════════════════

export const handlers = [

  // ── POST /api/Auth/login (公开) ──
  http.post('/api/Auth/login', async ({ request }) => {
    await delay(300);
    const e = checkSimulatedError(request); if (e) return e;
    const body = await request.json() as LoginRequest;
    if (!body.username?.trim() || !body.password?.trim()) {
      return HttpResponse.json<ApiError>(
        { error: '用户名和密码不能为空', code: 'INVALID_INPUT' },
        { status: 400 }
      );
    }
    const u = body.username.toLowerCase();
    const role: UserRole = u.includes('admin') ? 'admin' : u.includes('auditor') ? 'auditor' : 'viewer';
    return HttpResponse.json<LoginResponse>({
      token: `mock-jwt-${role}-${Date.now()}`,
      refreshToken: `mock-refresh-${Date.now()}`,
      username: body.username,
      role,
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  // ── POST /api/Auth/refresh (公开) ──
  http.post('/api/Auth/refresh', async ({ request }) => {
    await delay(200);
    const body = await request.json() as RefreshRequest;
    if (!body.refreshToken?.trim()) {
      return HttpResponse.json<ApiError>(
        { error: 'RefreshToken 不能为空', code: 'INVALID_INPUT' },
        { status: 400 }
      );
    }
    return HttpResponse.json<LoginResponse>({
      token: `mock-jwt-admin-${Date.now()}`,
      refreshToken: `mock-refresh-new-${Date.now()}`,
      username: 'admin',
      role: 'admin',
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  // ── POST /api/Auth/logout (公开) ──
  http.post('/api/Auth/logout', async () => {
    await delay(100);
    return HttpResponse.json({ message: '已登出' });
  }),

  // ═══════════════════════════════════════
  // Compliance (Auth)
  // ═══════════════════════════════════════

  http.get('/api/Compliance/summary', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<ComplianceSummary>(mockComplianceSummary);
  }),

  http.post('/api/Compliance/check', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    const body = await request.json() as ComplianceRequest;
    return HttpResponse.json<ComplianceResponse>(getComplianceResponse(body.query));
  }),

  http.post('/api/Compliance/hazard/query', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as HazardQueryRequest;
    return HttpResponse.json<HazardQueryResponse>(getHazardResponse(body.substanceName));
  }),

  http.post('/api/Compliance/storage/compatibility', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as StorageCompatibilityRequest;
    return HttpResponse.json<StorageCompatibilityResponse>(
      getStorageCompatibilityResponse(body.substanceA, body.substanceB)
    );
  }),

  // ═══════════════════════════════════════
  // Inspection (Auth)
  // ═══════════════════════════════════════

  http.get('/api/Inspection/plans', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    // 对齐后端：列表仅返回 items 计数，不含 type/scheduledDate/notes
    const listItems: InspectionPlanListItem[] = mockPlans.map(p => ({
      planId: p.planId, name: p.name, area: p.area,
      inspector: p.inspector, status: p.status,
      items: p.items.length, createdAt: p.createdAt,
    }));
    return HttpResponse.json<InspectionPlanListItem[]>(listItems);
  }),

  http.post('/api/Inspection/plans', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(300);
    const body = await request.json() as CreatePlanRequest;
    const newPlan: InspectionPlan = {
      planId: `plan-${Date.now()}`, name: body.name,
      area: body.area ?? '未指定', type: body.type ?? 'DailyWeekly',
      inspector: '当前用户', status: 'Draft',
      scheduledDate: new Date().toISOString(), createdAt: new Date().toISOString(),
      notes: body.notes ?? '',
      items: body.items.map((item, idx) => ({
        itemId: idx + 1, query: item.query, capabilityName: item.capability ?? 'regulatory-audit',
      })),
    };
    mockPlans.unshift(newPlan);
    return HttpResponse.json({ planId: newPlan.planId, name: newPlan.name, items: newPlan.items.length });
  }),

  http.get('/api/Inspection/plans/:id', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const plan = mockPlans.find((p) => p.planId === params.id);
    if (!plan) return HttpResponse.json<ApiError>({ error: '计划不存在' }, { status: 404 });
    return HttpResponse.json<InspectionPlan>(plan);
  }),

  http.post('/api/Inspection/plans/:id/execute', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    const round = getMockRound(`round-${Date.now()}`, params.id as string);
    return HttpResponse.json({
      roundId: round.roundId, planId: round.planId, complianceRate: round.complianceRate,
      compliantCount: round.compliantCount, nonCompliantCount: round.nonCompliantCount,
      warningCount: round.warningCount, ticketCount: round.ticketCount,
      totalElapsedMs: round.totalElapsedMs, executedBy: round.executedBy,
      startedAt: round.startedAt, completedAt: round.completedAt,
    });
  }),

  http.get('/api/Inspection/rounds/:id', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const round = getMockRound(params.id as string, 'plan-001');
    return HttpResponse.json({
      roundId: round.roundId, planId: round.planId, complianceRate: round.complianceRate,
      compliantCount: round.compliantCount, nonCompliantCount: round.nonCompliantCount,
      ticketCount: round.ticketCount, warningCount: round.warningCount,
      totalElapsedMs: round.totalElapsedMs, executedBy: round.executedBy,
      startedAt: round.startedAt, completedAt: round.completedAt,
      results: round.results.map(r => ({
        itemId: r.itemId, isCompliant: r.isCompliant, regulationRef: r.regulationRef,
        conclusion: r.conclusion, warnings: r.warnings.length, tools: r.tools,
        traceId: r.traceId, elapsedMs: r.elapsedMs,
      })),
    });
  }),

  http.get('/api/Inspection/reports/:id', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<InspectionReport>(getMockReport(params.id as string, 'round-001'));
  }),

  http.get('/api/Inspection/reports/:id/export', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(100);
    const report = getMockReport(params.id as string, 'round-001');
    return HttpResponse.json({
      meta: { reportId: report.reportId, roundId: report.roundId, format: 'json', generatedAt: report.generatedAt, generatedBy: report.generatedBy },
      plan: { planId: report.plan.planId, name: report.plan.name, area: report.plan.area, inspector: '张三' },
      summary: { complianceRate: report.complianceRate, summary: report.summary },
      findings: report.criticalFindings,
      tickets: [{ id: 1, issue: '苯与丙酮同库储存违规', priority: 'Critical', status: 'New', assignee: '', regulationRef: 'GB 15603-2022 §4.2.2' }],
      audit: { auditHash: report.auditHash, algorithm: 'SHA256' },
    });
  }),

  http.get('/api/Inspection/assets', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<ChemicalAsset[]>(mockAssets);
  }),

  http.post('/api/Inspection/scan', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    return HttpResponse.json<ScanResult>(mockScanResult);
  }),

  http.post('/api/Inspection/quick-check', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(1500);
    const body = await request.json() as QuickCheckRequest;
    return HttpResponse.json<QuickCheckResult>(getMockQuickCheck(body.query));
  }),

  // ═══════════════════════════════════════
  // Tickets (Auth)
  // ═══════════════════════════════════════

  http.get('/api/Tickets', async ({ request }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<TicketListResponse>(mockTicketList);
  }),

  http.put('/api/Tickets/:id/status', async ({ request, params }) => {
    const g = authGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(300);
    const body = await request.json() as TicketStatusUpdateRequest;
    const ticketId = Number(params.id);
    const updated = applyTicketStatusUpdate(ticketId, body.action, body.assignee);
    if (!updated) {
      return HttpResponse.json(
        { error: `工单 #${ticketId} 不存在或状态流转不合法`, code: 'INVALID_INPUT' },
        { status: 400 }
      );
    }
    return HttpResponse.json({ ticketId: updated.id, newStatus: updated.status, logCount: updated.logCount });
  }),

  // ═══════════════════════════════════════
  // Health / Metrics / Cache / KB / Memory (公开)
  // ═══════════════════════════════════════

  http.get('/health', async () => {
    await delay(50);
    return HttpResponse.json<HealthStatus>({
      status: 'healthy', timestamp: new Date().toISOString(), version: '2.5.0-mock',
      checks: { database: 'connected', ollama: 'reachable', knowledge_base_docs: 156, llm_calls: 1234, llm_error_rate: '2.1%' },
    });
  }),
  http.get('/health/ready', async () => HttpResponse.json({ ready: true })),
  http.get('/health/live', async () => HttpResponse.json({ alive: true })),

  http.get('/metrics', async () => {
    return new HttpResponse(
      '# HELP agent1_llm_calls_total Total LLM calls\n# TYPE agent1_llm_calls_total counter\nagent1_llm_calls_total 1234\n\n# HELP agent1_llm_duration_ms_avg Average LLM duration\n# TYPE agent1_llm_duration_ms_avg gauge\nagent1_llm_duration_ms_avg 4500\n\n# HELP agent1_api_requests_total Total API requests\n# TYPE agent1_api_requests_total counter\nagent1_api_requests_total 5678',
      { headers: { 'Content-Type': 'text/plain; version=0.0.4' } }
    );
  }),

  http.get('/cache/stats', async () => HttpResponse.json({ entries: 45, hits: 320, misses: 98, hitRate: 0.766, maxEntries: 500, ttlMinutes: 5 })),
  http.post('/cache/clear', async () => HttpResponse.json({ message: '缓存已清除', clearedEntries: 45 })),
  http.post('/knowledgebase/incremental-update', async () => {
    await delay(2000);
    return HttpResponse.json({ message: '知识库增量更新完成', addedDocuments: 3, removedDocuments: 0, totalDocuments: 159 });
  }),
  http.get('/memory/stats', async () => HttpResponse.json({ sessionCount: 12, totalFacts: 87, totalDialogTurns: 456, cacheHitRate: 0.72 })),
  http.get('/memory/long-term/search', async ({ request: req }) => {
    const url = new URL(req.url);
    const q = url.searchParams.get('q') ?? '';
    return HttpResponse.json({
      query: q,
      results: q ? [
        { fact: '苯的CAS号为71-43-2', confidence: 0.95, source: '历史查询' },
        { fact: '甲类仓库与明火点安全距离为30米', confidence: 0.88, source: '上次合规审核' },
      ] : [],
    });
  }),
];
