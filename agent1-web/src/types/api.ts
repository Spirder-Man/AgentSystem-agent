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
export type TicketStatus =
  | 'New'
  | 'Accepted'
  | 'InProgress'
  | 'Completed'
  | 'Verified'
  | 'Closed'
  | 'Rejected';

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

// ── Generic ──

export interface ApiError {
  error: string;
  code?: string;
  retryAfter?: number;
  details?: Record<string, string[]>; // 字段级校验详情
}
