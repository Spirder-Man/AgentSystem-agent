// ============================================================
// Agent1 前端 API 契约定义 — 与后端 C# record/class 对齐
// ============================================================

// ── Auth 认证 ──

/** 用户角色 — 对齐后端 Program.cs 授权策略:
 *  Admin   = admin only
 *  Auditor = admin + auditor (所有业务 Controller 实际使用的策略)
 *  Viewer  = admin + auditor + viewer (已定义但当前无 Controller 使用) */
export type UserRole = 'admin' | 'auditor' | 'viewer';

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  username: string;
  role: UserRole;
  expiresAt: string; // ISO 8601
}

export interface RefreshRequest {
  refreshToken: string;
}

// ── Compliance 合规审核 ──

export interface ComplianceRequest {
  query: string;
}

export interface ComplianceResponse {
  query: string;
  response: string | null;
  toolsUsed: string[];
  verifiedRegulations: string[];
  hallucinatedRegulations: string[];
  warnings: string[];
}

export interface HazardQueryRequest {
  substanceName: string;
}

export interface HazardQueryResponse {
  substanceName: string;
  response: string | null;
  toolsUsed: string[];
}

export interface StorageCompatibilityRequest {
  substanceA: string;
  substanceB: string;
}

export interface StorageCompatibilityResponse {
  substanceA: string;
  substanceB: string;
  response: string | null;
  toolsUsed: string[];
}

export interface ComplianceSummary {
  totalAssets: number;
  checkedAssets: number;
  compliantAssets: number;
  nonCompliantAssets: number;
  complianceRate: number;
  totalFindings: number;
  openFindings: number;
  remediationRate: number;
  lastAutoScanAt: string | null;
  findingsBySeverity: Record<string, number>;
  findingsByStatus: Record<string, number>;
  riskDistribution: {
    low: number;
    unknown: number;
    high: number;
    critical: number;
  };
}

// ── Inspection 巡检 ──

export interface CreatePlanRequest {
  name: string;
  type?: string;
  area?: string;
  items: InspectionItemRequest[];
  notes?: string;
}

export interface InspectionItemRequest {
  query: string;
  capability?: string;
}

/** 巡检计划列表项 — 对齐 GET /api/Inspection/plans 返回格式（items 为数字计数） */
export interface InspectionPlanListItem {
  planId: string;
  name: string;
  area: string;
  inspector: string;
  status: 'Draft' | 'InProgress' | 'Completed' | 'Archived';
  items: number; // 后端返回 count，不是数组
  createdAt: string;
}

/** 巡检计划详情 — 对齐 GET /api/Inspection/plans/:id 返回格式（items 为完整对象数组） */
export interface InspectionPlan {
  planId: string;
  name: string;
  area: string;
  type: string;
  inspector: string;
  status: 'Draft' | 'InProgress' | 'Completed' | 'Archived';
  scheduledDate: string;
  createdAt: string;
  notes: string;
  items: InspectionItem[];
}

export interface InspectionItem {
  itemId: number;
  query: string;
  capabilityName: string;
  expectedRegulation?: string;
}

export interface InspectionRound {
  roundId: string;
  planId: string;
  complianceRate: number;
  compliantCount: number;
  nonCompliantCount: number;
  warningCount: number;
  ticketCount: number;
  totalElapsedMs: number;
  executedBy: string;
  startedAt: string;
  completedAt: string | null;
  results: InspectionItemResult[];
}

/** 巡检轮次详情 — 对齐 GET /api/Inspection/rounds/:id，warnings 为计数而非数组 */
export interface InspectionRoundDetail {
  roundId: string;
  planId: string;
  complianceRate: number;
  compliantCount: number;
  nonCompliantCount: number;
  ticketCount: number;
  warningCount: number;
  totalElapsedMs: number;
  executedBy: string;
  startedAt: string;
  completedAt: string | null;
  results: RoundDetailResult[];
}

export interface RoundDetailResult {
  itemId: number;
  isCompliant: boolean | null;
  regulationRef: string;
  conclusion: string;
  warnings: number; // API 返回计数，非数组
  tools: string[];
  traceId: string;
  elapsedMs: number;
}

export interface InspectionItemResult {
  itemId: number;
  isCompliant: boolean | null;
  regulationRef: string;
  conclusion: string;
  warnings: string[];
  tools: string[];
  traceId: string;
  elapsedMs: number;
}

/** 巡检轮次列表项 — 对齐 GET /api/Inspection/rounds */
export interface InspectionRoundListItem {
  roundId: string;
  planId: string;
  planName: string;
  complianceRate: number;
  compliantCount: number;
  nonCompliantCount: number;
  ticketCount: number;
  warningCount: number;
  totalElapsedMs: number;
  executedBy: string;
  startedAt: string;
  completedAt: string | null;
}

export interface InspectionReport {
  reportId: string;
  roundId: string;
  complianceRate: number;
  summary: string;
  criticalFindings: string[];
  auditHash: string;
  generatedAt: string;
  generatedBy: string;
  markdown: string;
  plan: { planId: string; name: string; area: string };
}

export interface ChemicalAsset {
  assetId: string;
  name: string;
  casNumber: string;
  location: string;
  quantityTons: number;
  storageCondition: string;
  responsiblePerson: string;
  isMajorHazardSource: boolean;
  lastCheckResult: boolean | null;
  lastCheckedAt: string | null;
}

export interface ScanResult {
  scannedAt: string;
  totalAssets: number;
  checkedAssets: number;
  totalFindings: number;
  newFindings: number;
  findings: Finding[];
}

export interface Finding {
  findingId: string;
  assetId: string;
  ruleId: string;
  regulationRef: string;
  description: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low' | 'Info';
  status: string;
}

export interface QuickCheckRequest {
  query: string;
}

export interface QuickCheckResult {
  isCompliant: boolean;
  conclusion: string;
  regulationRef: string;
  warnings: string[];
  tools: string[];
  traceId: string;
  elapsedMs: number;
}

// ── Tickets 工单 ──

/** 工单状态 — 对齐后端 TicketFollowupModule.TicketStatus 枚举 */
export type TicketStatus = 'New' | 'Accepted' | 'InProgress' | 'Completed' | 'Verified' | 'Closed' | 'Rejected';

export interface TicketItem {
  id: number;
  issue: string;
  action: string;
  priority: string;
  status: TicketStatus;
  assignee: string;
  regulationRef: string;
  suggestedDeadline: string;
  isOpen: boolean;
  logCount: number;
}

export interface TicketListResponse {
  total: number;
  open: number;
  tickets: TicketItem[];
}

export interface TicketStatusUpdateRequest {
  action: 'accept' | 'start' | 'complete' | 'verify' | 'close' | 'reject';
  assignee?: string;
  reason?: string;
}

// ── Health ──

export interface HealthStatus {
  status: 'healthy' | 'degraded';
  timestamp: string;
  version: string;
  checks: {
    database: string;
    ollama: string;
    knowledge_base_docs: number;
    llm_calls: number;
    llm_error_rate: string;
  };
}

// ── Audit 审计日志 ──

export interface AuditLogEntry {
  id: number;
  user: string;
  operation: string;
  details: string;
  isSensitive: boolean;
  timestamp: string;
  chainHash: string | null;
}

export interface AuditLogListResponse {
  total: number;
  page: number;
  pageSize: number;
  logs: AuditLogEntry[];
}

export interface AuditIntegrityResponse {
  intact: boolean;
  brokenAtId: number | null;
  detail: string;
  verifiedAt: string;
}

export interface AuditStatsResponse {
  totalCount: number;
  byOperation: Record<string, number>;
  byUser: Record<string, number>;
  lastLogAt: string | null;
}

// ── KnowledgeBase 知识库 ──

export interface SearchModeResponse {
  mode: 'Bm25' | 'Vector' | 'Hybrid';
  available: string[];
  description: string;
}

export interface SearchModeUpdateRequest {
  mode: 'Bm25' | 'Vector' | 'Hybrid';
}

export interface RagTestRequest {
  query: string;
  topK?: number;
}

export interface RagTestResponse {
  query: string;
  mode: string;
  totalResults: number;
  elapsedMs: number;
  results: RagChunk[];
  summary: string;
}

export interface RagChunk {
  id: string;
  content: string;
  score: number;
  rank: number;
  retrievalMethod: string;
}

export interface IncrementalLoadResponse {
  message: string;
  addedDocuments: number;
  removedDocuments: number;
  totalDocuments: number;
}

// ── Diagnostics 工具诊断 ──

export interface DiagnosticsRunResponse {
  model: string;
  total: number;
  pass: number;
  passRate: string;
  elapsedMs: number;
  results: DiagnosticsTestResult[];
}

export interface DiagnosticsTestResult {
  index: number;
  query: string;
  description: string;
  expectedTools: string;
  toolCalls: string[];
  triggered: boolean;
  elapsedMs: number;
  error?: string;
}

// ── Eval 合规评测 ──

export interface EvalRunResponse {
  taskId: string;
  message: string;
}

export interface EvalTaskStatus {
  taskId: string;
  status: 'running' | 'completed' | 'failed';
  progress: string;
  report?: EvalReport;
}

export interface EvalReport {
  model: string;
  timestamp: string;
  total: number;
  toolCallRate: number;
  parameterAccuracy: number;
  conclusionAccuracy: number;
  cases: EvalCaseResult[];
  casesCount?: number;
  casesWithErrors?: number;
}

export interface EvalCaseResult {
  query: string;
  toolMatch: boolean;
  paramMatch: boolean;
  conclusionMatch: boolean;
  expectedTools: string[];
  actualTools: string[];
  error?: string;
}

// ── Multimodal 多模态 ──

export type AnalysisType = 'hazard-label' | 'storage-scene' | 'custom';

export interface MultimodalResult {
  analysisType: AnalysisType;
  result: string;
  fileName?: string;
}

// ── Regulatory 法规审计 ──

export interface RegulatoryAuditRequest {
  query: string;
}

export interface RegulatoryAuditResult {
  query: string;
  success: boolean;
  warnings: string[];
  intent: string;
  elapsedMs: number;
  output: string;
  auditRecord: unknown;
}

// ── Emergency 应急响应 ──

export interface EmergencyRequest {
  scenario: 'leak' | 'fire' | 'explosion' | 'poisoning';
  substance: string;
  location?: string;
}

export interface EmergencyResult {
  scenario: string;
  success: boolean;
  elapsedMs: number;
  output: string;
}

// ── KnowledgeGraph 知识图谱 ──

export interface KnowledgeGraphRequest {
  query: string;
}

export interface KnowledgeGraphResult {
  query: string;
  success: boolean;
  elapsedMs: number;
  entityCount: number;
  relationCount: number;
  output: string;
}

// ── Alerts 告警 ──

export interface AlertTestRequest {
  title: string;
  message: string;
}

export interface AlertTestResult {
  sent: boolean;
  recipient: string;
}

// ── Ticket Followup 工单跟进 ──

export interface TicketFollowupRequest {
  complianceResult: string;
}

export interface TicketFollowupResult {
  tickets: TicketItem[];
}

// ── Dashboard 合规总览 (DashboardController 6 端点) ──

/** GET /api/Dashboard/overview */
export interface DashboardOverview {
  totalAssets: number;
  checkedAssets: number;
  compliantAssets: number;
  nonCompliantAssets: number;
  complianceRate: number;
  totalFindings: number;
  openFindings: number;
  remediationRate: number;
  lastAutoScanAt: string | null;
  hasInventory: boolean;
  findingsBySeverity: Record<string, number>;
  findingsByStatus: Record<string, number>;
}

/** GET /api/Dashboard/assets */
export interface DashboardAssetItem {
  assetId: string;
  name: string;
  casNumber: string;
  location: string;
  quantityTons: number;
  storageCondition: string;
  responsiblePerson: string;
  isMajorHazardSource: boolean;
  lastCheckedAt: string | null;
  lastCheckResult: boolean | null;
  status: string;
  openFindings: number;
  totalFindings: number;
  applicableRegulations: string[];
}

/** GET /api/Dashboard/findings */
export interface DashboardFinding {
  findingId: string;
  description: string;
  regulationRef: string;
  assetId: string;
  assetName: string;
  assetLocation: string;
  severity: string;
  status: string;
  isOpen: boolean;
  assignee: string;
  remediationPlan: string;
  deadline: string | null;
  discoveredAt: string;
  lastStatusChangeAt: string | null;
  verifiedBy: string | null;
  verifiedAt: string | null;
}

export interface DashboardFindingsResponse {
  items: DashboardFinding[];
  total: number;
  summary: {
    totalFindings: number;
    openFindings: number;
    bySeverity: Record<string, number>;
    byStatus: Record<string, number>;
  };
  appliedFilter: {
    severity: string;
    status: string;
    openOnly: boolean;
  };
}

/** POST /api/Dashboard/scan */
export interface DashboardScanResult {
  newFindings: number;
  totalFindings: number;
  scannedAt: string;
  overview: {
    totalAssets: number;
    checkedAssets: number;
    complianceRate: number;
    openFindings: number;
    remediationRate: number;
  };
}

/** GET /api/Dashboard/history */
export interface DashboardHistoryRound {
  roundId: string;
  startedAt: string;
  completedAt: string | null;
  totalItems: number;
  compliantCount: number;
  nonCompliantCount: number;
  uncertainCount: number;
  complianceRate: number;
  duration: string | null;
  executedBy: string;
}

export interface DashboardHistoryPlan {
  planId: string;
  name: string;
  area: string;
  type: string;
  inspector: string;
  status: string;
  scheduledDate: string;
  createdAt: string;
  notes: string;
  itemCount: number;
  roundCount: number;
  rounds: DashboardHistoryRound[];
}

export interface DashboardHistoryResponse {
  items: DashboardHistoryPlan[];
  total: number;
  statusBreakdown: Record<string, number>;
}

/** GET /api/Dashboard/report/hazard */
export interface DashboardHazardItem {
  findingId: string;
  description: string;
  regulationRef: string;
  severity: string;
  status: string;
  assignee: string;
  remediationPlan: string;
  deadline: string | null;
  discoveredAt: string;
  asset: {
    assetId: string;
    name: string;
    location: string;
    casNumber: string;
    isMajorHazardSource: boolean;
  } | null;
}

export interface DashboardHazardReport {
  generatedAt: string;
  disclaimer: string;
  summary: {
    totalAssets: number;
    totalFindings: number;
    openFindings: number;
    closedFindings: number;
    bySeverity: Record<string, number>;
  };
  items: DashboardHazardItem[];
}

// ── Generic ──

export interface ApiError {
  error: string;
  code?: string;
  retryAfter?: number;
  details?: Record<string, string[]>; // 字段级校验详情
}

// ── Admin 数据库诊断 ──

export interface DbInfoResponse {
  info: {
    host: string;
    port: number;
    database: string;
    version: string;
  };
  tables: string[];
  retrievedAt: string;
}

export interface DbValidateResponse {
  connected: boolean;
  server: {
    host: string;
    port: number;
    database: string;
    user: string;
  };
  info: {
    host: string;
    port: number;
    database: string;
    version: string;
  };
  tableCount: number;
  tables: string[];
  elapsedMs: number;
  verifiedAt: string;
}
