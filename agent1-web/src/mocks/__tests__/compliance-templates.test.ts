/**
 * P4-3: Compliance 模板匹配 + 数据工厂测试
 *
 * 覆盖:
 *   - getComplianceResponse 关键词匹配逻辑
 *   - getHazardResponse 危化品查询
 *   - getStorageCompatibilityResponse 储存兼容性
 *   - mockComplianceSummary 结构完整性
 */
import { describe, it, expect } from 'vitest';
import type {
  ComplianceSummary,
  ComplianceResponse,
  HazardQueryResponse,
  StorageCompatibilityResponse,
} from '../../types/api';
import {
  mockComplianceSummary,
  getComplianceResponse,
  getHazardResponse,
  getStorageCompatibilityResponse,
} from '../data/compliance';

// ═══════════════════════════════════════
// mockComplianceSummary 结构验证
// ═══════════════════════════════════════

describe('mockComplianceSummary', () => {
  it('字段齐全且类型正确', () => {
    const s: ComplianceSummary = mockComplianceSummary;
    expect(typeof s.totalAssets).toBe('number');
    expect(typeof s.complianceRate).toBe('number');
    expect(s.complianceRate).toBeGreaterThanOrEqual(0);
    expect(s.complianceRate).toBeLessThanOrEqual(1);
    expect(s.riskDistribution).toBeDefined();
    expect(s.riskDistribution.low).toBeDefined();
    expect(s.riskDistribution.high).toBeDefined();
    expect(s.riskDistribution.critical).toBeDefined();
    expect(s.riskDistribution.unknown).toBeDefined();
    expect(s.findingsBySeverity).toBeDefined();
    expect(s.findingsByStatus).toBeDefined();
  });

  it('totalAssets ≥ checkedAssets', () => {
    expect(mockComplianceSummary.totalAssets).toBeGreaterThanOrEqual(
      mockComplianceSummary.checkedAssets
    );
  });

  it('compliantAssets + nonCompliantAssets ≤ checkedAssets', () => {
    expect(
      mockComplianceSummary.compliantAssets +
        mockComplianceSummary.nonCompliantAssets
    ).toBeLessThanOrEqual(mockComplianceSummary.checkedAssets);
  });
});

// ═══════════════════════════════════════
// getComplianceResponse 关键词匹配
// ═══════════════════════════════════════

describe('getComplianceResponse — 关键词匹配', () => {
  it('"苯"+"丙酮" 匹配储存兼容性模板', () => {
    const r: ComplianceResponse = getComplianceResponse(
      '苯和丙酮能放在同一个仓库吗'
    );
    expect(r.toolsUsed).toContain('CheckStorageCompatibility');
    expect(r.verifiedRegulations).toContain('GB 15603-2022');
    expect(r.hallucinatedRegulations).toEqual([]);
    expect(r.response).toContain('合规判断');
  });

  it('"苯"+"同库" 也匹配储存兼容性', () => {
    const r = getComplianceResponse('苯和丙酮同库储存');
    expect(r.toolsUsed).toContain('CheckStorageCompatibility');
    expect(r.warnings.length).toBeGreaterThan(0);
  });

  it('"苯"+"类别" 匹配危化品分类模板', () => {
    const r = getComplianceResponse('苯属于什么危险类别');
    expect(r.toolsUsed).toContain('CheckHazardCategory');
    expect(r.verifiedRegulations).toContain('GB 30000.7-2013');
    expect(r.hallucinatedRegulations).toEqual([]);
    expect(r.response).toContain('CAS号');
  });

  it('"安全距离" 匹配安全距离模板', () => {
    const r = getComplianceResponse('甲类仓库与明火点的安全距离是多少');
    expect(r.toolsUsed).toContain('GetSafetyDistance');
    expect(r.verifiedRegulations).toContain('GB 50160-2008');
    expect(r.response).toContain('30 米');
  });

  it('"间距" 也匹配安全距离', () => {
    const r = getComplianceResponse('危化品仓库间距要求');
    expect(r.toolsUsed).toContain('GetSafetyDistance');
  });

  it('"消防" 匹配安全距离模板', () => {
    const r = getComplianceResponse('消防通道宽度检查');
    expect(r.toolsUsed).toContain('GetSafetyDistance');
  });

  it('未匹配任何关键词 → 默认通用响应', () => {
    const r = getComplianceResponse('今天天气怎么样');
    expect(r.response).toContain('合规判断');
    expect(r.toolsUsed).toContain('CheckHazardCategory');
    expect(r.hallucinatedRegulations).toEqual([]);
  });
});

describe('getComplianceResponse — 返回值结构完整性', () => {
  it('所有模板返回的 ComplianceResponse 字段齐全', () => {
    const queries = [
      '苯和丙酮同库',
      '苯危险类别',
      '安全距离',
      'random query',
    ];
    for (const q of queries) {
      const r = getComplianceResponse(q);
      expect(r.query).toBe(q);
      expect(r.response).toBeDefined();
      expect(Array.isArray(r.toolsUsed)).toBe(true);
      expect(Array.isArray(r.verifiedRegulations)).toBe(true);
      expect(Array.isArray(r.hallucinatedRegulations)).toBe(true);
      expect(Array.isArray(r.warnings)).toBe(true);
    }
  });

  it('储存兼容性模板无幻觉法规', () => {
    const r = getComplianceResponse('苯和丙酮同库储存合规吗');
    expect(r.hallucinatedRegulations).toEqual([]);
    expect(r.warnings.length).toBeGreaterThan(0);
  });
});

// ═══════════════════════════════════════
// getHazardResponse
// ═══════════════════════════════════════

describe('getHazardResponse', () => {
  it('返回 HazardQueryResponse 结构', () => {
    const r: HazardQueryResponse = getHazardResponse('甲醇');
    expect(r.substanceName).toBe('甲醇');
    expect(r.response).toBeDefined();
    expect(r.toolsUsed).toContain('CheckHazardCategory');
  });

  it('未注册化学品返回默认响应', () => {
    const r = getHazardResponse('未知化学品');
    expect(r.substanceName).toBe('未知化学品');
    expect(r.toolsUsed).toBeDefined();
  });
});

// ═══════════════════════════════════════
// getStorageCompatibilityResponse
// ═══════════════════════════════════════

describe('getStorageCompatibilityResponse', () => {
  it('返回 StorageCompatibilityResponse 结构', () => {
    const r: StorageCompatibilityResponse = getStorageCompatibilityResponse(
      '苯',
      '丙酮'
    );
    expect(r.substanceA).toBe('苯');
    expect(r.substanceB).toBe('丙酮');
    expect(r.toolsUsed).toContain('CheckStorageCompatibility');
    expect(r.response).toContain('兼容性判断');
    expect(r.response).toContain('GB 15603-2022');
  });
});
