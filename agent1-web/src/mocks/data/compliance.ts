// ============================================================
// 合规审核 Mock 数据
// ============================================================

import type {
  ComplianceSummary,
  ComplianceResponse,
  HazardQueryResponse,
  StorageCompatibilityResponse,
} from '../../types/api';

// ── 合规总览（仪表盘核心数据）──

export const mockComplianceSummary: ComplianceSummary = {
  totalAssets: 8,
  checkedAssets: 6,
  compliantAssets: 4,
  nonCompliantAssets: 2,
  complianceRate: 0.75,
  totalFindings: 5,
  openFindings: 3,
  remediationRate: 0.6,
  lastAutoScanAt: new Date().toISOString(),
  findingsBySeverity: { Critical: 2, High: 1, Medium: 1, Low: 0, Info: 1 },
  findingsByStatus: {
    New: 2, Confirmed: 1, InProgress: 1, Remediated: 0,
    VerifiedClosed: 1, Closed: 2, FalsePositive: 0,
  },
  riskDistribution: { low: 4, unknown: 2, high: 1, critical: 1 },
};

// ── 合规审核响应模板（按查询类型分）──

const complianceTemplates: Record<string, ComplianceResponse> = {
  default: {
    query: '',
    response: null,
    toolsUsed: [],
    verifiedRegulations: [],
    hallucinatedRegulations: [],
    warnings: [],
  },

  // 储存兼容性
  '苯+丙酮': {
    query: '苯和丙酮能放在同一个仓库吗',
    response: `【合规判断】否

【法规依据】GB 15603-2022 §4.2.2 — 危险化学品储存通则
【违规点】苯（易燃液体）与丙酮（易燃液体）储存禁忌配对：
  • 两者同为易燃液体（闪点 < 60°C），且均为低闪点物质
  • GB 15603 §4.2.2 明确规定：禁忌物料不得同库储存
  • 苯闪点 -11°C，丙酮闪点 -20°C，泄漏后蒸气混合有火灾爆炸风险

【整改建议】
  1. 立即分库储存：苯移至甲类仓库A区1号位，丙酮移至甲类仓库A区2号位
  2. 两库间距应 ≥ 15 米（甲类仓库防火间距要求）
  3. 建议3个工作日内完成转移

⚠️ 警告: 丙酮存量（8吨）已超过重大危险源临界量（500吨）的80%，建议关注存量管理`,
    toolsUsed: ['CheckStorageCompatibility', 'CheckHazardCategory', 'LookupChemicalProperties'],
    verifiedRegulations: ['GB 15603-2022', 'GB 30000.7-2013', 'GB 18218-2018'],
    hallucinatedRegulations: [],
    warnings: ['丙酮存量超危险源临界量80%'],
  },

  // 危险类别
  '苯危险类别': {
    query: '苯属于什么危险类别',
    response: `【合规判断】是（信息查询）

【法规依据】GB 30000.7-2013 — 易燃液体分类标准

【物质属性】苯 (Benzene)
  • CAS号: 71-43-2
  • 分子式: C₆H₆
  • 闪点: -11°C（低闪点易燃液体）
  • 沸点: 80.1°C
  • UN编号: 1114

【危险类别】
  1. 易燃液体 (GB 30000.7-2013) — 闪点 ≤ 60°C
  2. 致癌性 (GB 30000.23-2013) — IARC 1类致癌物
  3. 特异性靶器官毒性-重复接触 (GB 30000.25-2013)

【重大危险源】临界量: 50 吨 (GB 18218-2018)`,
    toolsUsed: ['CheckHazardCategory', 'LookupChemicalProperties'],
    verifiedRegulations: ['GB 30000.7-2013', 'GB 30000.23-2013', 'GB 18218-2018'],
    hallucinatedRegulations: [],
    warnings: [],
  },

  // 安全距离
  '甲类仓库安全距离': {
    query: '甲类仓库与明火点的安全距离',
    response: `【合规判断】是（信息查询）

【法规依据】GB 50160-2008 (2018版) — 石油化工企业设计防火标准

【安全距离要求】
  • 甲类仓库与明火点: ≥ 30 米
  • 甲类仓库与甲类仓库: ≥ 20 米
  • 甲类仓库与民用建筑: ≥ 50 米
  • 甲类仓库与厂外道路: ≥ 20 米

【相关标准】
  • GB 50016-2014 (2018版) — 建筑设计防火规范
  • GB 50160-2008 (2018版) — 石油化工企业设计防火标准`,
    toolsUsed: ['GetSafetyDistance'],
    verifiedRegulations: ['GB 50160-2008', 'GB 50016-2014'],
    hallucinatedRegulations: [],
    warnings: [],
  },
};

// ── 危化品查询响应 ──

const hazardTemplates: Record<string, HazardQueryResponse> = {
  default: {
    substanceName: '',
    response: '【危险类别】易燃液体\n【适用标准】GB 30000.7-2013',
    toolsUsed: ['CheckHazardCategory'],
  },
};

// ── 储存兼容性查询响应 ──

const storageTemplates: Record<string, StorageCompatibilityResponse> = {
  default: {
    substanceA: '',
    substanceB: '',
    response: '【兼容性判断】否\n【法规依据】GB 15603-2022 §4.2.2\n【建议】立即分库储存',
    toolsUsed: ['CheckStorageCompatibility'],
  },
};

// ═══════════════════════════════════════
// 模板匹配引擎
// ═══════════════════════════════════════

export function getComplianceResponse(query: string): ComplianceResponse {
  // 关键词匹配 → 返回预定义模板
  const q = query.toLowerCase();
  if (q.includes('苯') && (q.includes('丙酮') || q.includes('同库') || q.includes('放在一起'))) {
    return { ...complianceTemplates['苯+丙酮'], query };
  }
  if (q.includes('苯') && (q.includes('类别') || q.includes('分类') || q.includes('属于'))) {
    return { ...complianceTemplates['苯危险类别'], query };
  }
  if (q.includes('安全距离') || q.includes('间距') || q.includes('消防')) {
    return { ...complianceTemplates['甲类仓库安全距离'], query };
  }

  // 默认：生成通用合规响应
  return {
    query,
    response: `【合规判断】是\n\n【法规依据】根据您的问题，相关标准如下：\n• GB 15603-2022 危险化学品储存通则\n• GB 30000 系列 — 化学品分类标准\n\n【建议】请提供更具体的查询信息以获得精确判断`,
    toolsUsed: ['CheckHazardCategory'],
    verifiedRegulations: ['GB 15603-2022'],
    hallucinatedRegulations: [],
    warnings: [],
  };
}

export function getHazardResponse(substanceName: string): HazardQueryResponse {
  const tpl = hazardTemplates[substanceName] ?? hazardTemplates.default;
  return { ...tpl, substanceName };
}

export function getStorageCompatibilityResponse(a: string, b: string): StorageCompatibilityResponse {
  return {
    substanceA: a,
    substanceB: b,
    response: storageTemplates.default.response,
    toolsUsed: storageTemplates.default.toolsUsed,
  };
}
