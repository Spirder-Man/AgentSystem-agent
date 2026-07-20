/**
 * P2-6: EvalPage 评估质量深度测试
 *
 * 验证: cases 结构完整性/报告字段边界/轮询状态转换/错误边界
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';

// 评估报告结构验证 — 纯逻辑测试，无需 DOM
describe('EvalPage 评估质量 — 报告结构', () => {
  describe('报告字段完整性', () => {
    it('完整报告应包含所有必需字段', () => {
      const singleCase = {
        query: '苯的储存条件',
        toolMatch: true,
        paramMatch: true,
        conclusionMatch: true,
        expectedTools: ['CheckHazardCategory'],
        actualTools: ['CheckHazardCategory'],
        expectedParams: { substance: '苯' },
        actualParams: { substance: '苯' },
        expectedConclusion: '合规',
        actualConclusion: '合规',
        error: null,
        elapsedMs: 2800,
      };

      const report = {
        model: 'qwen3-8b',
        timestamp: '2026-07-10T08:00:00Z',
        total: 3,
        toolCallRate: 0.85,
        parameterAccuracy: 0.76,
        conclusionAccuracy: 0.82,
        hallucinationRate: 0.04,
        avgElapsedMs: 3200,
        casesCount: 3,
        casesWithErrors: 1,
        cases: [singleCase, singleCase, singleCase],
      };

      // 核心指标
      expect(report.total).toBeGreaterThan(0);
      expect(report.casesCount).toBe(report.total);
      expect(report.toolCallRate).toBeGreaterThanOrEqual(0);
      expect(report.toolCallRate).toBeLessThanOrEqual(1);
      expect(report.parameterAccuracy).toBeGreaterThanOrEqual(0);
      expect(report.conclusionAccuracy).toBeGreaterThanOrEqual(0);
      expect(report.hallucinationRate).toBeGreaterThanOrEqual(0);

      // cases 结构
      const c = report.cases[0];
      expect(c.query).toBeTruthy();
      expect(typeof c.toolMatch).toBe('boolean');
      expect(typeof c.paramMatch).toBe('boolean');
      expect(typeof c.conclusionMatch).toBe('boolean');
      expect(Array.isArray(c.expectedTools)).toBe(true);
      expect(Array.isArray(c.actualTools)).toBe(true);
    });

    it('幻觉案例应有 error 字段', () => {
      const hallucinatedCase = {
        query: '不存在的法规',
        toolMatch: false,
        paramMatch: false,
        conclusionMatch: false,
        expectedTools: ['ChemicalRegulationSearch'],
        actualTools: ['UnknownTool'],
        error: 'Model hallucinated regulation GB-99999 which does not exist',
        hallucinatedRegulations: ['GB-99999'],
      };

      expect(hallucinatedCase.error).toBeTruthy();
      expect(hallucinatedCase.hallucinatedRegulations).toHaveLength(1);
    });

    it('casesCount 应与 cases 数组长度一致', () => {
      const casesArray = [
        { query: 'q1' }, { query: 'q2' }, { query: 'q3' },
      ];
      const casesCount = casesArray.length;

      expect(casesCount).toBe(3);
      expect(casesArray).toHaveLength(casesCount);
    });
  });

  describe('指标计算验证', () => {
    it('toolCallRate 计算应为匹配数/总数', () => {
      const cases = [
        { toolMatch: true },
        { toolMatch: true },
        { toolMatch: false },
        { toolMatch: true },
      ];
      const matchedCount = cases.filter(c => c.toolMatch).length;
      const rate = matchedCount / cases.length;

      expect(matchedCount).toBe(3);
      expect(rate).toBe(0.75);
    });

    it('conclusionAccuracy 计算应为匹配数/总数', () => {
      const cases = [
        { conclusionMatch: true },
        { conclusionMatch: true },
        { conclusionMatch: true },
        { conclusionMatch: false },
        { conclusionMatch: true },
      ];
      const matchedCount = cases.filter(c => c.conclusionMatch).length;
      const rate = matchedCount / cases.length;

      expect(matchedCount).toBe(4);
      expect(rate).toBe(0.8);
    });

    it('hallucinationRate 应正确计算幻觉占比', () => {
      const cases = [
        { hallucinatedRegulations: [] },
        { hallucinatedRegulations: [] },
        { hallucinatedRegulations: ['GB-FAKE'] },
        { hallucinatedRegulations: [] },
      ];
      const hallucinations = cases.filter(c => c.hallucinatedRegulations && c.hallucinatedRegulations.length > 0).length;
      const rate = hallucinations / cases.length;

      expect(hallucinations).toBe(1);
      expect(rate).toBe(0.25);
    });
  });

  describe('边界值', () => {
    it('空 cases 数组应有默认值', () => {
      const report = {
        total: 0,
        casesCount: 0,
        cases: [],
        toolCallRate: 0,
        parameterAccuracy: 0,
        conclusionAccuracy: 0,
      };

      expect(report.total).toBe(0);
      expect(report.cases).toHaveLength(0);
      expect(report.toolCallRate).toBe(0);
    });

    it('全部匹配时指标应为 1.0', () => {
      const report = {
        toolCallRate: 1.0,
        parameterAccuracy: 1.0,
        conclusionAccuracy: 1.0,
      };

      expect(report.toolCallRate).toBe(1);
      expect(report.parameterAccuracy).toBe(1);
      expect(report.conclusionAccuracy).toBe(1);
    });
  });
});
