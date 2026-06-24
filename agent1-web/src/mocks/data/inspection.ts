// ============================================================
// 巡检 & 资产 Mock 数据
// ============================================================

import type {
  InspectionPlan,
  InspectionRound,
  InspectionReport,
  ChemicalAsset,
  ScanResult,
  QuickCheckResult,
} from '../../types/api';

// ── 巡检计划列表 ──

export const mockPlans: InspectionPlan[] = [
  {
    planId: 'plan-001',
    name: '甲类仓库周检',
    area: '甲类仓库A区',
    type: 'Weekly',
    inspector: '张三',
    status: 'Completed',
    scheduledDate: '2026-06-23T10:00:00Z',
    createdAt: '2026-06-23T10:00:00Z',
    notes: '重点检查易燃液体储存合规性',
    items: [
      { itemId: 1, query: '苯与丙酮储存间距', capabilityName: 'storage-compliance' },
      { itemId: 2, query: '消防通道是否畅通', capabilityName: 'safety-distance' },
      { itemId: 3, query: '甲醇储罐安全状态', capabilityName: 'regulatory-audit' },
      { itemId: 4, query: 'GHS标签完整性', capabilityName: 'ghs-label-check' },
      { itemId: 5, query: '应急设备可用性', capabilityName: 'regulatory-audit' },
    ],
  },
  {
    planId: 'plan-002',
    name: '罐区月度安全检查',
    area: '储罐区',
    type: 'Monthly',
    inspector: '李四',
    status: 'InProgress',
    scheduledDate: '2026-06-22T09:00:00Z',
    createdAt: '2026-06-22T09:00:00Z',
    notes: '',
    items: [
      { itemId: 1, query: '储罐防腐蚀检查', capabilityName: 'regulatory-audit' },
      { itemId: 2, query: '压力容器安全阀校验', capabilityName: 'regulatory-audit' },
      { itemId: 3, query: '硝酸储存条件', capabilityName: 'storage-compliance' },
      { itemId: 4, query: '围堰完整性', capabilityName: 'safety-distance' },
    ],
  },
  {
    planId: 'plan-003',
    name: '节前安全大检查',
    area: '全园区',
    type: 'PreHoliday',
    inspector: '王五',
    status: 'Draft',
    scheduledDate: '2026-06-21T08:00:00Z',
    createdAt: '2026-06-21T08:00:00Z',
    notes: '春节前全面安全检查，覆盖所有区域',
    items: [
      { itemId: 1, query: '全园区消防设施', capabilityName: 'safety-distance' },
      { itemId: 2, query: '危化品仓库储存合规', capabilityName: 'storage-compliance' },
      { itemId: 3, query: '应急疏散通道', capabilityName: 'safety-distance' },
      { itemId: 4, query: '电气设备安全', capabilityName: 'regulatory-audit' },
      { itemId: 5, query: '管道色标合规', capabilityName: 'regulatory-audit' },
      { itemId: 6, query: '储罐区防雷接地', capabilityName: 'regulatory-audit' },
      { itemId: 7, query: '危废暂存间合规', capabilityName: 'regulatory-audit' },
      { itemId: 8, query: '应急物资储备', capabilityName: 'regulatory-audit' },
    ],
  },
];

// ── 巡检轮次结果 ──

export function getMockRound(roundId: string, planId: string): InspectionRound {
  return {
    roundId,
    planId,
    complianceRate: 0.8,
    compliantCount: 4,
    nonCompliantCount: 1,
    warningCount: 2,
    ticketCount: 2,
    totalElapsedMs: 45000,
    executedBy: '张三',
    startedAt: '2026-06-23T10:00:00Z',
    completedAt: '2026-06-23T10:00:45Z',
    results: [
      {
        itemId: 1,
        isCompliant: false,
        regulationRef: 'GB 15603-2022 §4.2.2',
        conclusion: '苯与丙酮同库储存违规，禁忌物料不得同库',
        warnings: ['丙酮存量超临界量80%'],
        tools: ['CheckStorageCompatibility'],
        traceId: 'trace-001',
        elapsedMs: 12000,
      },
      {
        itemId: 2,
        isCompliant: true,
        regulationRef: 'GB 50016 §7.1.8',
        conclusion: '消防通道宽度 ≥ 4m，符合要求',
        warnings: [],
        tools: ['GetSafetyDistance'],
        traceId: 'trace-002',
        elapsedMs: 3000,
      },
    ],
  };
}

// ── 巡检报告 ──

export function getMockReport(reportId: string, roundId: string): InspectionReport {
  return {
    reportId,
    roundId,
    complianceRate: 0.8,
    summary: `# 甲类仓库周检报告

## 巡检概况
- 巡检时间: 2026-06-23 10:00
- 巡检人: 张三
- 检查项: 5 项
- 合规率: 80%

## 不合规发现
1. **苯与丙酮同库储存** — GB 15603-2022 §4.2.2
   - 严重程度: Critical
   - 建议: 立即分库储存

## 审计哈希
SHA256: a1b2c3d4e5f6...`,
    criticalFindings: [
      '苯与丙酮同库储存 — 禁忌物料不得同库 (GB 15603-2022 §4.2.2)',
    ],
    auditHash: 'a1b2c3d4e5f6',
    generatedAt: new Date().toISOString(),
    generatedBy: '系统自动生成',
    markdown: '...',
    plan: { planId: 'plan-001', name: '甲类仓库周检', area: '甲类仓库A区' },
  };
}

// ── 资产台账 ──

export const mockAssets: ChemicalAsset[] = [
  {
    assetId: 'a1',
    name: '苯',
    casNumber: '71-43-2',
    location: '甲类仓库A区1号位',
    quantityTons: 15,
    storageCondition: '常温常压, 避光, 通风',
    responsiblePerson: '张三',
    isMajorHazardSource: true,
    lastCheckResult: false,
    lastCheckedAt: '2026-06-23T10:30:00Z',
  },
  {
    assetId: 'a2',
    name: '丙酮',
    casNumber: '67-64-1',
    location: '甲类仓库A区2号位',
    quantityTons: 8,
    storageCondition: '常温常压, 通风',
    responsiblePerson: '张三',
    isMajorHazardSource: false,
    lastCheckResult: true,
    lastCheckedAt: '2026-06-23T10:30:00Z',
  },
  {
    assetId: 'a3',
    name: '甲醇',
    casNumber: '67-56-1',
    location: '甲类仓库B区1号位',
    quantityTons: 20,
    storageCondition: '常温常压, 避光',
    responsiblePerson: '李四',
    isMajorHazardSource: true,
    lastCheckResult: false,
    lastCheckedAt: '2026-06-22T14:00:00Z',
  },
  {
    assetId: 'a4',
    name: '硝酸',
    casNumber: '7697-37-2',
    location: '乙类仓库C区3号位',
    quantityTons: 5,
    storageCondition: '常温, 远离可燃物, 防腐蚀容器',
    responsiblePerson: '王五',
    isMajorHazardSource: false,
    lastCheckResult: true,
    lastCheckedAt: '2026-06-20T09:00:00Z',
  },
  {
    assetId: 'a5',
    name: '氢氧化钠',
    casNumber: '1310-73-2',
    location: '乙类仓库D区1号位',
    quantityTons: 3,
    storageCondition: '防潮, 密封, 远离酸类',
    responsiblePerson: '王五',
    isMajorHazardSource: false,
    lastCheckResult: null,
    lastCheckedAt: null,
  },
  {
    assetId: 'a6',
    name: '氯',
    casNumber: '7782-50-5',
    location: '甲类仓库C区2号位',
    quantityTons: 2,
    storageCondition: '加压液化, 钢瓶储存, 通风',
    responsiblePerson: '赵六',
    isMajorHazardSource: true,
    lastCheckResult: true,
    lastCheckedAt: '2026-06-21T11:00:00Z',
  },
];

// ── 自动扫描结果 ──

export const mockScanResult: ScanResult = {
  scannedAt: new Date().toISOString(),
  totalAssets: 6,
  checkedAssets: 6,
  totalFindings: 2,
  newFindings: 1,
  findings: [
    {
      findingId: 'f-001',
      assetId: 'a1',
      ruleId: 'R_STORAGE_COMPAT',
      regulationRef: 'GB 15603-2022 §4.2.2',
      description: '苯与丙酮同库储存违规 — 禁忌物料不得同库',
      severity: 'Critical',
      status: 'New',
    },
    {
      findingId: 'f-002',
      assetId: 'a3',
      ruleId: 'R_MAJOR_HAZARD_THRESHOLD',
      regulationRef: 'GB 18218-2018',
      description: '甲醇存量(20t)超过重大危险源临界量(500t)的80%',
      severity: 'High',
      status: 'New',
    },
  ],
};

// ── 快速检查结果 ──

export function getMockQuickCheck(query: string): QuickCheckResult {
  return {
    isCompliant: !query.includes('违规') && !query.includes('同库'),
    conclusion: query.includes('违规')
      ? '不合规：检测到储存违规风险'
      : '合规：未发现明显违规项',
    regulationRef: 'GB 15603-2022',
    warnings: query.includes('违规') ? ['建议进一步核查'] : [],
    tools: ['CheckStorageCompatibility'],
    traceId: `trace-${Date.now()}`,
    elapsedMs: 2500,
  };
}
