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
  CreatePlanRequest, InspectionPlanListItem, InspectionRoundListItem, InspectionPlan, InspectionRound, InspectionReport, ChemicalAsset,
  ScanResult, QuickCheckRequest, QuickCheckResult,
  TicketListResponse, TicketStatusUpdateRequest, TicketFollowupResult, TicketFollowupRequest,
  HealthStatus, ApiError,
  AuditLogEntry, AuditLogListResponse, AuditIntegrityResponse, AuditStatsResponse,
  RegulatoryAuditRequest, RegulatoryAuditResult,
  EmergencyRequest, EmergencyResult,
  KnowledgeGraphRequest, KnowledgeGraphResult,
  EvalRunResponse, EvalTaskStatus,
  DiagnosticsRunResponse,
  MultimodalResult,
  SearchModeResponse, RagTestRequest, RagTestResponse, IncrementalLoadResponse,
  AlertTestRequest, AlertTestResult,
  DbInfoResponse, DbValidateResponse,
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

export function parseAuth(request: Request): MockAuth | null {
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

export function checkSimulatedError(request: Request) {
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
// Auth Guards — 对齐后端三层授权策略:
//   readAuthGuard  = [Authorize(Policy = "Viewer")]  → admin/auditor/viewer
//   writeAuthGuard = [Authorize(Policy = "Auditor")] → admin/auditor
//   adminAuthGuard = [Authorize(Policy = "Admin")]   → admin only
// ═══════════════════════════════════════

export function readAuthGuard(request: Request) {
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
  return null;
}

export function writeAuthGuard(request: Request) {
  const base = readAuthGuard(request);
  if (base) return base;
  const auth = parseAuth(request)!;
  if (auth.role === 'viewer') {
    return HttpResponse.json<ApiError>(
      { error: '您没有权限执行此操作', code: 'UNAUTHORIZED' },
      { status: 403 }
    );
  }
  return null;
}

export function adminAuthGuard(request: Request) {
  const base = readAuthGuard(request);
  if (base) return base;
  const auth = parseAuth(request)!;
  if (auth.role !== 'admin') {
    return HttpResponse.json<ApiError>(
      { error: '需要管理员权限', code: 'UNAUTHORIZED' },
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
      return HttpResponse.json(
        { error: 'RefreshToken 不能为空', code: 'INVALID_INPUT' },
        { status: 400 }
      );
    }
    return HttpResponse.json({
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
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<ComplianceSummary>(mockComplianceSummary);
  }),

  http.post('/api/Compliance/check', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    const body = await request.json() as ComplianceRequest;
    return HttpResponse.json<ComplianceResponse>(getComplianceResponse(body.query));
  }),

  http.post('/api/Compliance/hazard/query', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as HazardQueryRequest;
    return HttpResponse.json<HazardQueryResponse>(getHazardResponse(body.substanceName));
  }),

  http.post('/api/Compliance/storage/compatibility', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as StorageCompatibilityRequest;
    return HttpResponse.json<StorageCompatibilityResponse>(
      getStorageCompatibilityResponse(body.substanceA, body.substanceB)
    );
  }),

  // ═══════════════════════════════════════
  // Dashboard 合规总览 (Auth) — 对齐后端 DashboardController 6 端点
  // ═══════════════════════════════════════

  http.get('/api/Dashboard/overview', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json({
      totalAssets: 6,
      checkedAssets: 5,
      compliantAssets: 3,
      nonCompliantAssets: 2,
      complianceRate: 0.6,
      totalFindings: 12,
      openFindings: 5,
      remediationRate: 0.58,
      lastAutoScanAt: new Date(Date.now() - 86400000).toISOString(),
      hasInventory: true,
      findingsBySeverity: { Critical: 2, High: 3, Medium: 4, Low: 2, Info: 1 },
      findingsByStatus: { New: 2, Confirmed: 3, InProgress: 4, Remediated: 2, VerifiedClosed: 1 },
    });
  }),

  http.post('/api/Dashboard/scan', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    return HttpResponse.json({
      newFindings: 1,
      totalFindings: 3,
      scannedAt: new Date().toISOString(),
      overview: {
        totalAssets: 6,
        checkedAssets: 6,
        complianceRate: 0.67,
        openFindings: 5,
        remediationRate: 0.58,
      },
    });
  }),

  http.get('/api/Dashboard/findings', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const url = new URL(request.url);
    const severity = url.searchParams.get('severity');
    const allFindings = [
      { findingId: 'f-001', description: '苯与丙酮同库储存违规 — 禁忌物料不得同库', regulationRef: 'GB 15603-2022 §4.2.2', assetId: 'a1', assetName: '苯', assetLocation: '甲类仓库A区1号位', severity: 'Critical', status: 'New', isOpen: true, assignee: '', remediationPlan: '立即将丙酮移至专用库房', deadline: new Date(Date.now() + 86400000 * 3).toISOString(), discoveredAt: '2026-07-08T10:00:00Z', lastStatusChangeAt: '2026-07-08T10:00:00Z', verifiedBy: null, verifiedAt: null },
      { findingId: 'f-002', description: '甲醇存量(20t)超过重大危险源临界量(500t)的80%', regulationRef: 'GB 18218-2018', assetId: 'a3', assetName: '甲醇', assetLocation: '甲类仓库B区1号位', severity: 'High', status: 'InProgress', isOpen: true, assignee: '李四', remediationPlan: '建立存量监控台账', deadline: new Date(Date.now() + 86400000 * 7).toISOString(), discoveredAt: '2026-07-07T14:00:00Z', lastStatusChangeAt: '2026-07-09T09:00:00Z', verifiedBy: null, verifiedAt: null },
      { findingId: 'f-003', description: '消防通道标识缺失 — 部分区域疏散标识模糊', regulationRef: 'GB 13495.1-2015 §5.2', assetId: 'a2', assetName: '丙酮', assetLocation: '甲类仓库A区2号位', severity: 'Medium', status: 'Confirmed', isOpen: true, assignee: '', remediationPlan: '补装疏散指示标识和应急照明', deadline: new Date(Date.now() + 86400000 * 5).toISOString(), discoveredAt: '2026-07-06T11:00:00Z', lastStatusChangeAt: '2026-07-07T08:00:00Z', verifiedBy: null, verifiedAt: null },
      { findingId: 'f-004', description: '硝酸储存容器未标注腐蚀性标识', regulationRef: 'GB 15258-2009', assetId: 'a4', assetName: '硝酸', assetLocation: '乙类仓库C区3号位', severity: 'Low', status: 'Remediated', isOpen: false, assignee: '王五', remediationPlan: '张贴GHS腐蚀性象形图', deadline: null, discoveredAt: '2026-06-20T09:00:00Z', lastStatusChangeAt: '2026-07-01T10:00:00Z', verifiedBy: 'admin', verifiedAt: '2026-07-02T09:00:00Z' },
      { findingId: 'f-005', description: '氢氧化钠未按防潮要求密封储存', regulationRef: 'GB 15603-2022 §5.3', assetId: 'a5', assetName: '氢氧化钠', assetLocation: '乙类仓库D区1号位', severity: 'Medium', status: 'New', isOpen: true, assignee: '', remediationPlan: '更换密封容器，增设干燥剂', deadline: new Date(Date.now() + 86400000 * 10).toISOString(), discoveredAt: '2026-07-09T08:00:00Z', lastStatusChangeAt: '2026-07-09T08:00:00Z', verifiedBy: null, verifiedAt: null },
    ];
    let filtered = allFindings;
    if (severity) {
      filtered = allFindings.filter(f => f.severity.toLowerCase() === severity.toLowerCase());
    }
    const bySeverity: Record<string, number> = {};
    const byStatus: Record<string, number> = {};
    allFindings.forEach(f => {
      bySeverity[f.severity] = (bySeverity[f.severity] || 0) + 1;
      byStatus[f.status] = (byStatus[f.status] || 0) + 1;
    });
    return HttpResponse.json({
      items: filtered,
      total: filtered.length,
      summary: { totalFindings: allFindings.length, openFindings: allFindings.filter(f => f.isOpen).length, bySeverity, byStatus },
      appliedFilter: { severity: severity || 'all', status: 'all', openOnly: true },
    });
  }),

  http.get('/api/Dashboard/history', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json({
      items: [
        {
          planId: 'plan-001', name: '甲类仓库周检', area: '甲类仓库A区', type: 'Weekly', inspector: '张三',
          status: 'Completed', scheduledDate: '2026-07-08T10:00:00Z', createdAt: '2026-07-08T10:00:00Z',
          notes: '重点检查易燃液体储存合规性', itemCount: 5, roundCount: 2,
          rounds: [
            { roundId: 'round-001', startedAt: '2026-07-08T10:00:00Z', completedAt: '2026-07-08T10:00:45Z', totalItems: 5, compliantCount: 4, nonCompliantCount: 1, uncertainCount: 0, complianceRate: 0.8, duration: '45s', executedBy: '张三' },
            { roundId: 'round-002', startedAt: '2026-07-01T10:00:00Z', completedAt: '2026-07-01T10:01:30Z', totalItems: 5, compliantCount: 4, nonCompliantCount: 1, uncertainCount: 0, complianceRate: 0.8, duration: '90s', executedBy: '张三' },
          ],
        },
        {
          planId: 'plan-002', name: '罐区月度安全检查', area: '储罐区', type: 'Monthly', inspector: '李四',
          status: 'InProgress', scheduledDate: '2026-07-10T09:00:00Z', createdAt: '2026-07-10T09:00:00Z',
          notes: '', itemCount: 4, roundCount: 1,
          rounds: [
            { roundId: 'round-003', startedAt: '2026-07-10T09:00:00Z', completedAt: null, totalItems: 4, compliantCount: 2, nonCompliantCount: 1, uncertainCount: 1, complianceRate: 0.5, duration: null, executedBy: '李四' },
          ],
        },
        {
          planId: 'plan-003', name: '节前安全大检查', area: '全园区', type: 'PreHoliday', inspector: '王五',
          status: 'Draft', scheduledDate: '2026-08-15T08:00:00Z', createdAt: '2026-07-05T08:00:00Z',
          notes: '春节前全面安全检查', itemCount: 8, roundCount: 0, rounds: [],
        },
      ],
      total: 3,
      statusBreakdown: { Completed: 1, InProgress: 1, Draft: 1 },
    });
  }),

  http.get('/api/Dashboard/report/hazard', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json({
      generatedAt: new Date().toISOString(),
      disclaimer: '本报告为 AI 辅助生成，建议人工复核后提交安全管理部。',
      summary: { totalAssets: 6, totalFindings: 12, openFindings: 4, closedFindings: 8, bySeverity: { Critical: 1, High: 1, Medium: 2 } },
      items: [
        { findingId: 'f-001', description: '苯与丙酮同库储存违规 — 禁忌物料不得同库', regulationRef: 'GB 15603-2022 §4.2.2', severity: 'Critical', status: 'New', assignee: '', remediationPlan: '立即将丙酮移至专用库房', deadline: new Date(Date.now() + 86400000 * 3).toISOString(), discoveredAt: '2026-07-08T10:00:00Z', asset: { assetId: 'a1', name: '苯', location: '甲类仓库A区1号位', casNumber: '71-43-2', isMajorHazardSource: true } },
        { findingId: 'f-002', description: '甲醇存量(20t)超过重大危险源临界量(500t)的80%', regulationRef: 'GB 18218-2018', severity: 'High', status: 'InProgress', assignee: '李四', remediationPlan: '建立存量监控台账', deadline: new Date(Date.now() + 86400000 * 7).toISOString(), discoveredAt: '2026-07-07T14:00:00Z', asset: { assetId: 'a3', name: '甲醇', location: '甲类仓库B区1号位', casNumber: '67-56-1', isMajorHazardSource: true } },
        { findingId: 'f-003', description: '消防通道标识缺失', regulationRef: 'GB 13495.1-2015 §5.2', severity: 'Medium', status: 'Confirmed', assignee: '', remediationPlan: '补装疏散指示标识和应急照明', deadline: new Date(Date.now() + 86400000 * 5).toISOString(), discoveredAt: '2026-07-06T11:00:00Z', asset: { assetId: 'a2', name: '丙酮', location: '甲类仓库A区2号位', casNumber: '67-64-1', isMajorHazardSource: false } },
        { findingId: 'f-005', description: '氢氧化钠未按防潮要求密封储存', regulationRef: 'GB 15603-2022 §5.3', severity: 'Medium', status: 'New', assignee: '', remediationPlan: '更换密封容器，增设干燥剂', deadline: new Date(Date.now() + 86400000 * 10).toISOString(), discoveredAt: '2026-07-09T08:00:00Z', asset: { assetId: 'a5', name: '氢氧化钠', location: '乙类仓库D区1号位', casNumber: '1310-73-2', isMajorHazardSource: false } },
      ],
    });
  }),

  // ═══════════════════════════════════════
  // Regulatory Audit (Auth)
  // ═══════════════════════════════════════

  http.post('/api/regulatory/audit', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    const body = await request.json() as RegulatoryAuditRequest;
    return HttpResponse.json<RegulatoryAuditResult>({
      query: body.query,
      success: true,
      warnings: [],
      intent: '法规审计',
      elapsedMs: 4500,
      output: `══════════════ 监管核查评估报告 ══════════════\n核查时间: ${new Date().toISOString()}\n核查项数: 5\n\n${body.query.includes('消防') ? '✅ 核查项: 消防安全合规性\n判定: 部分合规 (3/5)\n法规依据: GB 50016-2014《建筑设计防火规范》§3.3.1\n建议: 消防通道需保持畅通，灭火器需定期检查\n\n⚠️ 核查项: 疏散标识\n判定: 需整改\n法规依据: GB 13495.1-2015《消防安全标志》\n建议: 部分区域疏散标识缺失或模糊\n\n汇总: ✅ 3项 | ❌ 2项 | ⚠️ 0项 | 合规率: 60%' : '经审查未发现明显违规项，相关设施和流程基本符合现行法规要求。'}`,
      auditRecord: { items: 5, compliantCount: 3, nonCompliantCount: 2, regulationRefs: ['GB 50016-2014', 'GB 13495.1-2015'] },
    });
  }),

  // ═══════════════════════════════════════
  // Emergency Response (Auth)
  // ═══════════════════════════════════════

  http.post('/api/Emergency/response', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as EmergencyRequest;
    const loc = body.location ? `事发位置: ${body.location}` : '事发位置: 未指定';
    const isLeak = body.scenario === 'leak';
    return HttpResponse.json<EmergencyResult>({
      scenario: body.scenario,
      success: true,
      elapsedMs: 5200,
      output: `┌─────────────────────────────────────┐\n│ 🚨 应急响应方案: ${body.substance} ${body.scenario === 'leak' ? '泄漏' : body.scenario === 'fire' ? '火灾' : body.scenario === 'explosion' ? '爆炸' : '中毒'}事故 │\n│ CAS: 71-43-2 | 危化品分类: 易燃液体, 类别2 │\n└─────────────────────────────────────┘\n\n【疏散与隔离】\n1. 立即疏散${isLeak ? '下风向' : '周边'}500米范围内无关人员\n2. 设立警戒区，禁止一切明火和非必要人员进入\n3. 通知应急指挥中心启动应急预案\n\n【PPE 个人防护】\n- 呼吸防护: 正压自给式呼吸器 (SCBA)${isLeak ? '\n- 皮肤防护: A级防化服（全封闭）' : ''}\n- 眼部防护: 防化护目镜\n- 手部防护: 丁基橡胶手套\n\n【泄漏处置】\n1. 切断泄漏源，关闭相关阀门\n2. 用沙土、蛭石或不燃材料围堵收容\n3. 使用防爆工具收集泄漏物至专用容器\n4. 污染区域用大量清水冲洗，废水收集处理\n\n【医疗急救】\n➤ 吸入: 迅速脱离现场至空气新鲜处，保持呼吸道通畅，如呼吸困难给氧，就医\n➤ 皮肤: 脱去污染衣物，用肥皂水和清水彻底冲洗至少15分钟，就医\n➤ 眼睛: 提起眼睑，用流动清水或生理盐水冲洗至少15分钟，就医\n➤ 食入: 漱口，禁止催吐，立即就医\n\n【事故通报模板】\n事故类型: ${body.scenario} | 涉及物质: ${body.substance} (CAS 71-43-2)\n${loc}\n\n【知识库补充建议】\n- 查询 GB 30000.7-2013 易燃液体分类标准\n- 查询 GB 15603-2022 危险化学品储存通则\n- 查询《危险化学品安全管理条例》(国务院令第591号)`,
    });
  }),

  // ═══════════════════════════════════════
  // Knowledge Graph (Auth)
  // ═══════════════════════════════════════

  http.post('/api/knowledgegraph/query', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as KnowledgeGraphRequest;
    return HttpResponse.json<KnowledgeGraphResult>({
      query: body.query,
      success: true,
      warnings: [],
      intent: '知识图谱查询',
      elapsedMs: 3800,
      output: `══════════ 知识图谱查询: ${body.query} ══════════\n实体: ${body.query.includes('苯') ? '苯 (CAS 71-43-2)' : '甲类仓库'} | 关联关系: 8条 | 关联事故: 2起\n\n关联实体:\n├─ 法规: GB 30000.7-2013 (易燃液体)\n├─ 法规: GB 15603-2022 (储存通则)\n├─ 事故: 2019年某化工厂苯泄漏事故 (等级: 较大)\n├─ 事故: 2021年储罐区火灾事故 (等级: 一般)\n├─ 园区: 甲类仓库A区 (风险等级: 高)\n├─ 园区: 储罐区 (风险等级: 高)\n├─ 化学品: 丙酮 (CAS 67-64-1, 储存禁忌)\n└─ 化学品: 甲醇 (CAS 67-56-1, 共储许可)\n\nRAG知识库补充:\nGB 30000.7-2013 将苯列为易燃液体类别2，其蒸气与空气可形成爆炸性混合物。`,
      auditRecord: { regulationRefs: ['GB 30000.7-2013', 'GB 15603-2022'] },
    });
  }),

  // ═══════════════════════════════════════
  // Inspection (Auth) ═══════════════════

  http.get('/api/Inspection/plans', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
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
    const g = writeAuthGuard(request); if (g) return g;
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
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const plan = mockPlans.find((p) => p.planId === params.id);
    if (!plan) return HttpResponse.json<ApiError>({ error: '计划不存在' }, { status: 404 });
    return HttpResponse.json<InspectionPlan>(plan);
  }),

  http.put('/api/Inspection/plans/:id', async ({ request, params }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(300);
    const plan = mockPlans.find((p) => p.planId === params.id);
    if (!plan) return HttpResponse.json<ApiError>({ error: '计划不存在' }, { status: 404 });
    const body = await request.json() as Partial<InspectionPlan>;
    if (body.name !== undefined) plan.name = body.name;
    if (body.area !== undefined) plan.area = body.area;
    if (body.notes !== undefined) plan.notes = body.notes;
    if (body.status !== undefined) plan.status = body.status;
    return HttpResponse.json({ planId: plan.planId, message: '计划已更新' });
  }),

  http.delete('/api/Inspection/plans/:id', async ({ request, params }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const idx = mockPlans.findIndex((p) => p.planId === params.id);
    if (idx === -1) return HttpResponse.json<ApiError>({ error: '计划不存在' }, { status: 404 });
    mockPlans.splice(idx, 1);
    return HttpResponse.json({ message: '计划已删除' });
  }),

  http.post('/api/Inspection/plans/:id/execute', async ({ request, params }) => {
    const g = writeAuthGuard(request); if (g) return g;
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

  // GET /api/Inspection/rounds — 巡检轮次列表（对齐后端 InspectionController.ListRounds）
  http.get('/api/Inspection/rounds', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const planNames = Object.fromEntries(mockPlans.map(p => [p.planId, p.name]));
    const listItems: InspectionRoundListItem[] = mockPlans.flatMap(p => [
      getMockRound(`round-${p.planId}-1`, p.planId),
      getMockRound(`round-${p.planId}-2`, p.planId),
    ]).sort((a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime())
    .map(r => ({
      roundId: r.roundId, planId: r.planId,
      planName: planNames[r.planId] ?? '未知计划',
      complianceRate: r.complianceRate, compliantCount: r.compliantCount,
      nonCompliantCount: r.nonCompliantCount, ticketCount: r.ticketCount,
      warningCount: r.warningCount, totalElapsedMs: r.totalElapsedMs,
      executedBy: r.executedBy, startedAt: r.startedAt, completedAt: r.completedAt,
    }));
    return HttpResponse.json<InspectionRoundListItem[]>(listItems);
  }),

  http.get('/api/Inspection/rounds/:id', async ({ request, params }) => {
    const g = readAuthGuard(request); if (g) return g;
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
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<InspectionReport>(getMockReport(params.id as string, 'round-001'));
  }),

  http.get('/api/Inspection/reports/:id/export', async ({ request, params }) => {
    const g = readAuthGuard(request); if (g) return g;
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
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<ChemicalAsset[]>(mockAssets);
  }),

  http.get('/api/Inspection/assets/:assetId', async ({ request, params }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const asset = mockAssets.find((a) => a.assetId === params.assetId);
    if (!asset) return HttpResponse.json<ApiError>({ error: '资产不存在' }, { status: 404 });
    return HttpResponse.json<ChemicalAsset>(asset);
  }),

  http.post('/api/Inspection/scan', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    const s = maybeSimulateError(); if (s) return HttpResponse.json(s, { status: 503 });
    await simulateLlmDelay();
    return HttpResponse.json<ScanResult>(mockScanResult);
  }),

  http.post('/api/Inspection/quick-check', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(1500);
    const body = await request.json() as QuickCheckRequest;
    return HttpResponse.json<QuickCheckResult>(getMockQuickCheck(body.query));
  }),

  // ═══════════════════════════════════════
  // Multimodal 多模态分析 (Auth)
  // ═══════════════════════════════════════

  http.post('/api/multimodal/analyze', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    // FormData — 不解析 body，使用 MSW 自带的 request.formData()
    await request.formData().catch(() => {});
    return HttpResponse.json<MultimodalResult>({
      analysisType: 'hazard-label',
      result: '【危化品标签识别结果】\n\n化学品名称: 苯\nCAS号: 71-43-2\nUN编号: 1114\n危险类别: 易燃液体, 类别2\n信号词: 危险\n\n象形图:\n⚠️ 火焰 (GHS02)\n⚠️ 健康危害 (GHS08)\n⚠️ 感叹号 (GHS07)\n\n防范说明:\n- 远离热源、热表面、火花、明火及其他点火源\n- 使用防爆电气/通风/照明设备\n- 戴防护手套/穿防护服/戴防护眼罩/戴防护面具',
      fileName: 'hazard-label.jpg',
    });
  }),

  // ═══════════════════════════════════════
  // Tickets (Auth)
  // ═══════════════════════════════════════

  http.get('/api/Tickets', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<TicketListResponse>(mockTicketList);
  }),

  http.put('/api/Tickets/:id/status', async ({ request, params }) => {
    const g = writeAuthGuard(request); if (g) return g;
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

  http.post('/api/tickets/followup', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    const body = await request.json() as TicketFollowupRequest;
    return HttpResponse.json<TicketFollowupResult>({
      tickets: [
        { id: 101, issue: '苯与丙酮同库储存违规', priority: 'Critical', status: 'New', assignee: '',
          action: '立即将丙酮移至专用库房', regulationRef: 'GB 15603-2022 §4.2.2',
          suggestedDeadline: new Date(Date.now() + 86400000 * 3).toISOString(), isOpen: true, logCount: 0 },
        { id: 102, issue: '消防通道标识缺失', priority: 'High', status: 'New', assignee: '',
          action: '补装疏散指示标识和应急照明', regulationRef: 'GB 13495.1-2015 §5.2',
          suggestedDeadline: new Date(Date.now() + 86400000 * 7).toISOString(), isOpen: true, logCount: 0 },
      ],
    });
  }),

  // ═══════════════════════════════════════
  // Eval 合规评测 (Auth)
  // ═══════════════════════════════════════

  http.post('/api/eval/run', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<EvalRunResponse>({ taskId: `eval-${Date.now()}`, message: '评测已启动，共 50 条用例' });
  }),

  http.get('/api/eval/status/:taskId', async ({ request, params }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const completed = Math.random() > 0.3;
    return HttpResponse.json<EvalTaskStatus>({
      taskId: params.taskId as string,
      status: completed ? 'completed' : 'running',
      progress: completed ? '50/50' : `${Math.floor(Math.random() * 30) + 10}/50`,
      report: completed ? {
        model: 'qwen3-8b',
        timestamp: new Date().toISOString(),
        total: 50,
        toolCallRate: 0.85,
        parameterAccuracy: 0.76,
        conclusionAccuracy: 0.82,
        casesCount: 50,
        casesWithErrors: 3,
        cases: Array.from({ length: 5 }, (_, i) => ({
          query: `测试用例 ${i + 1}`, toolMatch: i < 4, paramMatch: i < 4, conclusionMatch: i < 4,
          expectedTools: ['ChemicalRegulationSearch'], actualTools: i < 4 ? ['ChemicalRegulationSearch'] : [],
          error: i >= 4 ? 'FC 调用超时' : undefined,
        })),
      } : undefined,
    });
  }),

  http.delete('/api/eval/status/:taskId', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    await delay(100);
    return HttpResponse.json({ message: '任务已删除' });
  }),

  // ═══════════════════════════════════════
  // Audit 审计日志 (Admin only) — 对齐后端 AuditController
  // ═══════════════════════════════════════

  http.get('/api/Audit/logs', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    const url = new URL(request.url);
    const page = Number(url.searchParams.get('page')) || 1;
    const pageSize = Number(url.searchParams.get('pageSize')) || 50;
    const mockLogs: AuditLogEntry[] = [
      { id: 1, user: 'admin', operation: '合规审核', details: '查询: 苯与丙酮同库储存 | 工具: [CheckStorageCompatibility] | 验证法规: 3条 | 幻觉法规: 0条', isSensitive: true, timestamp: '2026-07-10 14:30:00', chainHash: 'a1b2c3d4e5f6a7b8' },
      { id: 2, user: 'auditor', operation: '危化品查询', details: '化学品: 甲醇 | 工具: [CheckHazardCategory]', isSensitive: true, timestamp: '2026-07-10 13:15:00', chainHash: 'b2c3d4e5f6a7b8c9d0' },
      { id: 3, user: 'admin', operation: '巡检执行', details: '计划: plan-001 (甲类仓库周检) | 合规率: 80%', isSensitive: false, timestamp: '2026-07-10 10:00:00', chainHash: 'c3d4e5f6a7b8c9d0e1' },
      { id: 4, user: 'viewer', operation: '查看报告', details: '报告: report-001 | 轮次: round-001', isSensitive: false, timestamp: '2026-07-10 09:45:00', chainHash: 'd4e5f6a7b8c9d0e1f2' },
      { id: 5, user: 'auditor', operation: '储存兼容性', details: '苯 vs 丙酮 | 工具: [CheckStorageCompatibility]', isSensitive: true, timestamp: '2026-07-09 16:20:00', chainHash: 'e5f6a7b8c9d0e1f2a3' },
      { id: 6, user: 'admin', operation: '自动扫描', details: '资产 6/6 | 发现 2 条 | 新增 1 条', isSensitive: false, timestamp: '2026-07-09 08:00:00', chainHash: 'f6a7b8c9d0e1f2a3b4' },
    ];
    const total = mockLogs.length;
    const paged = mockLogs.slice((page - 1) * pageSize, page * pageSize);
    return HttpResponse.json<AuditLogListResponse>({ total, page, pageSize, logs: paged });
  }),

  http.get('/api/Audit/integrity', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(500);
    return HttpResponse.json<AuditIntegrityResponse>({
      intact: true,
      brokenAtId: null,
      detail: 'SHA256 哈希链完整 — 所有 6 条日志记录未检测到篡改',
      verifiedAt: new Date().toISOString(),
    });
  }),

  http.get('/api/Audit/export', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(300);
    return HttpResponse.json({
      report: '# Agent1 审计报告\n\n## 时间范围: 2026-07-01 ~ 2026-07-10\n\n### 操作统计\n- 合规审核: 12 次\n- 危化品查询: 8 次\n- 巡检执行: 3 次\n- 储存兼容性: 5 次\n- 自动扫描: 2 次\n\n### 完整性验证\n✅ SHA256 哈希链完整，未检测到篡改',
      generatedAt: new Date().toISOString(),
    });
  }),

  http.get('/api/Audit/stats', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(200);
    return HttpResponse.json<AuditStatsResponse>({
      totalCount: 156,
      byOperation: { '合规审核': 42, '危化品查询': 35, '巡检执行': 18, '储存兼容性': 22, '自动扫描': 12, '查看报告': 27 },
      byUser: { admin: 68, auditor: 55, viewer: 33 },
      lastLogAt: '2026-07-10 14:30:00',
    });
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

  // ═══════════════════════════════════════
  // KnowledgeBase (Auth)
  // ═══════════════════════════════════════

  http.get('/api/knowledgebase/search-mode', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    await delay(100);
    return HttpResponse.json({ mode: 'Hybrid', available: ['Bm25', 'Vector', 'Hybrid'], description: 'BM25 + 向量加权融合' });
  }),

  http.put('/api/knowledgebase/search-mode', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    await delay(200);
    const body = await request.json() as { mode: string };
    return HttpResponse.json({ mode: body.mode, message: `搜索模式已切换为 ${body.mode}` });
  }),

  http.post('/api/knowledgebase/rag-test', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    await simulateLlmDelay();
    const body = await request.json() as RagTestRequest;
    return HttpResponse.json({
      query: body.query,
      mode: 'Hybrid',
      totalResults: 5,
      elapsedMs: 450,
      summary: `检索完成：针对「${body.query}」召回 5 个相关法规片段，Top-1 相关度 0.92`,
      results: [
        { id: 'chunk-001', content: 'GB 15603-2022《危险化学品储存通则》第4.2.2条规定…', score: 0.92, rank: 1, retrievalMethod: 'BM25' },
        { id: 'chunk-002', content: 'GB 18218-2018《危险化学品重大危险源辨识》临界量规定…', score: 0.85, rank: 2, retrievalMethod: 'Vector' },
        { id: 'chunk-003', content: 'GB 50016-2014《建筑设计防火规范》§3.3.1 甲类仓库防火间距要求…', score: 0.78, rank: 3, retrievalMethod: 'BM25' },
        { id: 'chunk-004', content: '《危险化学品安全管理条例》第二十四条规定…', score: 0.71, rank: 4, retrievalMethod: 'Vector' },
        { id: 'chunk-005', content: 'AQ 3013-2008《危险化学品从业单位安全标准化通用规范》…', score: 0.65, rank: 5, retrievalMethod: 'Hybrid' },
      ],
    });
  }),

  http.post('/api/knowledgebase/incremental-load', async () => {
    await delay(2000);
    return HttpResponse.json<IncrementalLoadResponse>({ message: '知识库增量更新完成', addedDocuments: 3, removedDocuments: 0, totalDocuments: 159 });
  }),

  // ═══════════════════════════════════════
  // Diagnostics 工具调用诊断 (Auth)
  // ═══════════════════════════════════════

  http.post('/api/diagnostics/tool-calling', async ({ request }) => {
    const g = readAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await simulateLlmDelay();
    return HttpResponse.json<DiagnosticsRunResponse>({
      model: 'qwen3-8b',
      total: 5,
      pass: 4,
      passRate: '80.0%',
      elapsedMs: 12500,
      results: [
        { index: 1, query: '苯的储存要求', description: '危化品查询', expectedTools: 'CheckHazardCategory', toolCalls: ['CheckHazardCategory'], triggered: true, elapsedMs: 2300 },
        { index: 2, query: '审查甲类仓库', description: '合规审核', expectedTools: 'ChemicalRegulationSearch', toolCalls: ['ChemicalRegulationSearch'], triggered: true, elapsedMs: 3100 },
        { index: 3, query: '苯和丙酮能共储吗', description: '储存兼容性', expectedTools: 'CheckStorageCompatibility', toolCalls: ['CheckStorageCompatibility'], triggered: true, elapsedMs: 2800 },
        { index: 4, query: 'KB检索 消防规范', description: '知识库查询', expectedTools: 'KnowledgeBaseSearch', toolCalls: [], triggered: false, elapsedMs: 1500, error: 'LLM 未触发工具调用' },
        { index: 5, query: '应急 苯泄漏', description: '应急响应', expectedTools: 'EmergencyResponse', toolCalls: ['EmergencyResponse'], triggered: true, elapsedMs: 2800 },
      ],
    });
  }),

  // ═══════════════════════════════════════
  // Admin 数据库诊断 (Admin only)
  // ═══════════════════════════════════════

  http.get('/api/admin/db/info', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    await delay(200);
    return HttpResponse.json({
      info: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', version: 'PostgreSQL 16.3' },
      tables: [
        'chemical_substances', 'chemical_substance_categories', 'chemical_substance_aliases',
        'chemical_substance_incompatibilities', 'chemical_documents', 'audit_log',
        'inspection_plans', 'inspection_items', 'inspection_rounds', 'inspection_round_items',
      ],
      retrievedAt: new Date().toISOString(),
    } as DbInfoResponse);
  }),

  http.get('/api/admin/db/validate', async ({ request }) => {
    const g = adminAuthGuard(request); if (g) return g;
    const e = checkSimulatedError(request); if (e) return e;
    await delay(800);
    return HttpResponse.json({
      connected: true,
      server: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', user: 'agent_admin' },
      info: { host: 'localhost', port: 5432, database: 'chemical_park_ai_agent', version: 'PostgreSQL 16.3' },
      tableCount: 10,
      tables: [
        'chemical_substances', 'chemical_substance_categories', 'chemical_substance_aliases',
        'chemical_substance_incompatibilities', 'chemical_documents', 'audit_log',
        'inspection_plans', 'inspection_items', 'inspection_rounds', 'inspection_round_items',
      ],
      elapsedMs: 35,
      verifiedAt: new Date().toISOString(),
    } as DbValidateResponse);
  }),

  // ═══════════════════════════════════════
  // Alerts 告警测试 (Auth)
  // ═══════════════════════════════════════

  http.post('/api/alerts/test', async ({ request }) => {
    const g = writeAuthGuard(request); if (g) return g;
    await delay(500);
    return HttpResponse.json({ sent: true, recipient: 'lcy.050801@qq.com' });
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
