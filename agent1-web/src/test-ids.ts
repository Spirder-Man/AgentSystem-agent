// ============================================================
// test-ids.ts — 前端测试契约：所有 data-testid 的单一真值源
//
// 用途:
//   1. Vue 组件引用此模块设置 data-testid 属性
//   2. e2e-real 测试引用此模块获取定位器
//   3. CI check-test-ids.ts 扫描校验，禁止测试使用未注册的 test-id
//
// 命名规范: {模块}-{功能}-{元素}
//   如: compliance-input, dashboard-scan-btn
// ============================================================

// ── 合规检查页 ComplianceCheckPage ──
export const COMPLIANCE_CHECK = {
  /** 查询输入框 */
  input: 'compliance-input',
  /** 提交审核按钮 */
  submitBtn: 'compliance-submit-btn',
  /** 结果区容器（含分析结果+法规引用+安全警告） */
  resultPanel: 'compliance-result-panel',
  /** 工具调用链 */
  toolChain: 'compliance-tool-chain',
  /** 分析结果 Tab 按钮 */
  analysisTab: 'compliance-analysis-tab',
  /** 分析结果内容区（LLM 解释面板） */
  llmPanel: 'compliance-llm-panel',
  /** 法规引用 Tab 按钮 */
  regulationTab: 'compliance-regulation-tab',
  /** 法规引用内容区 */
  regulationPanel: 'compliance-regulation-panel',
} as const;

// ── 仪表盘页 DashboardPage ──
export const DASHBOARD = {
  /** 自动合规扫描按钮 */
  scanBtn: 'dashboard-scan-btn',
  /** 扫描结果区 */
  scanResult: 'dashboard-scan-result',
} as const;

// ── 审计日志页 AuditPage ──
export const AUDIT = {
  /** 哈希链完整性校验按钮 */
  integrityBtn: 'audit-integrity-btn',
  /** 完整性验证结果提示 */
  integrityResult: 'audit-integrity-result',
  /** 审计日志表格 */
  logTable: 'audit-log-table',
} as const;

// ── 应急响应页 EmergencyPage ──
export const EMERGENCY = {
  /** 事故类型按钮组 */
  scenarioBtns: 'emergency-scenario-btns',
  /** 生成应急方案按钮 */
  submitBtn: 'emergency-submit-btn',
  /** 应急方案结果区 */
  result: 'emergency-result',
} as const;

// ── 合规评测页 EvalPage ──
export const EVAL = {
  /** 启动评测按钮 */
  startBtn: 'eval-start-btn',
  /** 评测报告区 */
  report: 'eval-report',
} as const;

// ── 侧边栏导航 AppSidebar ──
/**
 * 导航项 data-testid 格式: nav-{路径}
 * 如: nav-dashboard, nav-compliance, nav-assets
 *
 * @param path - 路由路径，如 '/dashboard', '/compliance/history'
 * @returns test-id 字符串
 */
export function navTestId(path: string): string {
  return 'nav-' + path.replace(/^\//, '').replace(/\//g, '-');
}

// ── 全量注册表（供 check-test-ids.ts 校验用） ──
/** 所有已注册的 data-testid 值集合 */
export const ALL_TEST_IDS = new Set<string>([
  // ComplianceCheckPage
  COMPLIANCE_CHECK.input,
  COMPLIANCE_CHECK.submitBtn,
  COMPLIANCE_CHECK.resultPanel,
  COMPLIANCE_CHECK.toolChain,
  COMPLIANCE_CHECK.analysisTab,
  COMPLIANCE_CHECK.llmPanel,
  COMPLIANCE_CHECK.regulationTab,
  COMPLIANCE_CHECK.regulationPanel,
  // DashboardPage
  DASHBOARD.scanBtn,
  DASHBOARD.scanResult,
  // AuditPage
  AUDIT.integrityBtn,
  AUDIT.integrityResult,
  AUDIT.logTable,
  // EmergencyPage
  EMERGENCY.scenarioBtns,
  EMERGENCY.submitBtn,
  EMERGENCY.result,
  // EvalPage
  EVAL.startBtn,
  EVAL.report,
]);
