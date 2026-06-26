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

/** 模拟 LLM 推理延迟（基于 Task 11 实测：Qwen3-8B on RTX 3090 通常 3-25s） */
async function simulateLlmDelay(): Promise<void> {
  // 70% 概率 3-12s（常规查询），20% 概率 12-25s（复杂推理），10% 概率 25-45s（巡检全量扫描）
  const rand = Math.random();
  const baseDelay = rand < 0.7 ? 3000 + Math.random() * 9000   // 3-12s
    : rand < 0.9 ? 12000 + Math.random() * 13000                 // 12-25s
    : 25000 + Math.random() * 20000;                              // 25-45s
  const jitter = Math.random() * 3000;
  await delay(baseDelay + jitter);
}

/** 模拟 5% 概率的服务器错误（测试错误处理） */
function maybeSimulateError(): ApiError | null {
  if (Math.random() < 0.05) {
    return { error: '服务繁忙，请稍后重试', retryAfter: 10 };
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
    const usernameLower = body.username.toLowerCase();
    const role = usernameLower.includes('admin') ? 'admin'
      : usernameLower.includes('auditor') ? 'auditor'
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
  // ⚠️ 对齐后端: QueryHazard() 使用 LLM 推理 (ExecuteEvalFastQueryAsync)
  http.post('/api/Compliance/hazard/query', async ({ request }) => {
    await simulateLlmDelay();
    const body = await request.json() as HazardQueryRequest;
    const result = getHazardResponse(body.substanceName);
    return HttpResponse.json<HazardQueryResponse>(result);
  }),

  // ── POST /api/Compliance/storage/compatibility ──
  // ⚠️ 对齐后端: CheckStorageCompatibility() 使用 LLM 推理 (ExecuteEvalFastAsync)
  http.post('/api/Compliance/storage/compatibility', async ({ request }) => {
    await simulateLlmDelay();
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
    // 对齐后端 InspectionController.CreatePlan() 返回: {planId, name, items: count}
    return HttpResponse.json({
      planId: newPlan.planId,
      name: newPlan.name,
      items: newPlan.items.length,
    });
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
  // ⚠️ 对齐后端: ExecutePlan() 仅返回摘要字段，不包含 results 数组
  //    前端需额外调用 GET /api/Inspection/rounds/:id 获取详细结果
  http.post('/api/Inspection/plans/:id/execute', async ({ params }) => {
    const error = maybeSimulateError();
    if (error) return HttpResponse.json(error, { status: 503 });

    await simulateLlmDelay(); // 执行巡检需要 LLM 推理
    const round = getMockRound(`round-${Date.now()}`, params.id as string);
    return HttpResponse.json({
      roundId: round.roundId,
      planId: round.planId,
      complianceRate: round.complianceRate,
      compliantCount: round.compliantCount,
      nonCompliantCount: round.nonCompliantCount,
      warningCount: round.warningCount,
      ticketCount: round.ticketCount,
      totalElapsedMs: round.totalElapsedMs,
      executedBy: round.executedBy,
      startedAt: round.startedAt,
      completedAt: round.completedAt,
    });
  }),

  // ── GET /api/Inspection/rounds/:id ──
  // ⚠️ 对齐后端: Warnings 返回数量(非数组), Tools 使用 PascalCase, ElapsedMs 来自 Metrics
  http.get('/api/Inspection/rounds/:id', async ({ params }) => {
    await delay(200);
    const round = getMockRound(params.id as string, 'plan-001');
    return HttpResponse.json({
      roundId: round.roundId,
      planId: round.planId,
      complianceRate: round.complianceRate,
      compliantCount: round.compliantCount,
      nonCompliantCount: round.nonCompliantCount,
      ticketCount: round.ticketCount,
      warningCount: round.warningCount,
      totalElapsedMs: round.totalElapsedMs,
      executedBy: round.executedBy,
      startedAt: round.startedAt,
      completedAt: round.completedAt,
      results: round.results.map(r => ({
        itemId: r.itemId,
        isCompliant: r.isCompliant,
        regulationRef: r.regulationRef,
        conclusion: r.conclusion,
        warnings: r.warnings.length,
        tools: r.tools,
        traceId: r.traceId,
        elapsedMs: r.elapsedMs,
      })),
    });
  }),

  // ── GET /api/Inspection/reports/:id ──
  http.get('/api/Inspection/reports/:id', async ({ params }) => {
    await delay(200);
    const report = getMockReport(params.id as string, 'round-001');
    return HttpResponse.json<InspectionReport>(report);
  }),

  // ── GET /api/Inspection/reports/:id/export ──
  // ⚠️ 对齐后端 ExportReport() 返回: {meta, plan, summary, findings, tickets, audit}
  http.get('/api/Inspection/reports/:id/export', async ({ params }) => {
    await delay(100);
    const report = getMockReport(params.id as string, 'round-001');
    return HttpResponse.json({
      meta: {
        reportId: report.reportId,
        roundId: report.roundId,
        format: 'json',
        generatedAt: report.generatedAt,
        generatedBy: report.generatedBy,
      },
      plan: {
        planId: report.plan.planId,
        name: report.plan.name,
        area: report.plan.area,
        inspector: '张三',
      },
      summary: {
        complianceRate: report.complianceRate,
        summary: report.summary,
      },
      findings: report.criticalFindings,
      tickets: [
        { id: 1, issue: '苯与丙酮同库储存违规', priority: 'Critical', status: 'New', assignee: '', regulationRef: 'GB 15603-2022 §4.2.2' },
      ],
      audit: {
        auditHash: report.auditHash,
        algorithm: 'SHA256',
      },
    });
  }),

  // ── GET /api/Inspection/assets ──
  http.get('/api/Inspection/assets', async () => {
    await delay(200);
    return HttpResponse.json<ChemicalAsset[]>(mockAssets);
  }),

  // ── POST /api/Inspection/scan ──
  // ⚠️ 对齐后端: RunAutoScan() 使用 LLM 规则引擎扫描 (ScanAssetsAsync)
  http.post('/api/Inspection/scan', async () => {
    const error = maybeSimulateError();
    if (error) return HttpResponse.json(error, { status: 503 });

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
  // ⚠️ 对齐后端 TicketsController.UpdateStatus() 返回: {ticketId, newStatus, logCount}
  http.put('/api/Tickets/:id/status', async ({ params, request }) => {
    await delay(300);
    const body = await request.json() as TicketStatusUpdateRequest;
    const ticketId = Number(params.id);

    const updated = applyTicketStatusUpdate(ticketId, body.action, body.assignee);
    if (!updated) {
      return HttpResponse.json(
        { error: `工单 #${ticketId} 不存在或状态流转不合法` },
        { status: 400 }
      );
    }
    return HttpResponse.json({
      ticketId: updated.id,
      newStatus: updated.status,
      logCount: updated.logCount,
    });
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
