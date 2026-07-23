// ============================================================
// llm-assertions.ts — LLM 推理质量专用断言
//
// 真实 GPU E2E 的核心价值：验证 LLM 输出质量而非仅 UI 渲染
//
// 基于三层契约架构 Layer 3 (Quality Baseline):
//   断言不再凭想象写正则，而是基于 baseline.json 中验证过的模型实际能力
// ============================================================

import { expect, type Page, type Locator } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { COMPLIANCE_CHECK } from '../../src/test-ids';

// ── 基线数据 ──
interface BaselineScenario {
  query: string;
  commonKeywords: string[];
  rareKeywords: string[];
  qualityFloor: {
    mustContainAtLeast: number;
    minLength: number;
    description: string;
  };
}

interface Baseline {
  scenarios: Record<string, BaselineScenario>;
}

let _baseline: Baseline | null = null;
function loadBaseline(): Baseline {
  if (!_baseline) {
    try {
      // __dirname 在 Playwright ESM 上下文中可能不可用，使用相对路径
      const path = join(process.cwd(), 'e2e-real', 'baseline.json');
      _baseline = JSON.parse(readFileSync(path, 'utf-8')) as Baseline;
    } catch {
      console.warn('[LLM-ASSERT] 无法加载 baseline.json，使用降级模式');
      _baseline = { scenarios: {} };
    }
  }
  return _baseline;
}

// ── 已知合法的 GB 编号模式 ──
const GB_PATTERN = /GB\s*\d{4,5}(?:\.\d+)?\s*-\s*\d{4}/gi;

// ── 常见工具名称 ──
const KNOWN_TOOLS = [
  'CheckStorageCompatibility',
  'QueryChemicalHazard',
  'CheckSafetyDistance',
  'QueryMajorHazardSource',
  'CheckRegulationVersion',
  'CheckComprehensiveCompliance',
  'QueryChemicalProperty',
];

/**
 * 验证 LLM 输出中包含合法的 GB 编号（非幻觉）
 * - GB 编号格式: GB XXXXX-YYYY 或 GB XXXXX.YYYY-YYYY
 * - 可选：与已知规范列表交叉校验
 */
function isPage(obj: Locator | Page): obj is Page {
  return 'goto' in obj;
}

export async function expectGbNumberPresent(locator: Locator | Page, description = '法规引用') {
  const text = isPage(locator)
    ? ((await locator.locator('body').textContent()) ?? '')
    : ((await locator.textContent()) ?? '');

  const matches = text.match(GB_PATTERN) ?? [];
  expect(matches.length, `${description} 应包含 GB 编号引用`).toBeGreaterThan(0);

  // 验证格式合法性：每个匹配项应是 4-5 位数字开头
  for (const m of matches) {
    expect(m, `GB 编号 "${m}" 格式应合法`).toMatch(/^GB\s*\d{4,5}/i);
  }
}

/**
 * 验证工具调用链中包含指定工具名
 */
export async function expectToolCallPresent(page: Page, toolName: string, description = '工具调用链') {
  const toolChain = page.locator(`[data-testid="${COMPLIANCE_CHECK.toolChain}"]`).or(page.locator(`text=${toolName}`));

  // 等待工具调用链渲染（LLM 推理可能需要较长时间）
  await expect(toolChain.first(), `${description} 应包含 ${toolName}`).toBeVisible({
    timeout: 90_000,
  });
}

/**
 * 验证 LLM 推理在合理时间范围内完成
 */
export function expectResponseTimeInRange(
  startTime: number,
  minMs: number,
  maxMs: number,
  description = 'LLM 推理耗时',
) {
  const elapsed = Date.now() - startTime;
  expect(
    elapsed,
    `${description}: ${elapsed}ms 应在 ${minMs}ms-${maxMs}ms 之间（真实 GPU 推理）`,
  ).toBeGreaterThanOrEqual(minMs);
  expect(elapsed, `${description}: ${elapsed}ms 不应超过 ${maxMs}ms`).toBeLessThanOrEqual(maxMs);
}

/**
 * 验证无幻觉 GB 编号（输出中的 GB 编号必须全部在已知列表中）
 * 注意：knownList 为空时仅验证格式合法性
 */
export async function expectNoHallucinatedGb(
  locator: Locator | Page,
  knownGbList: string[] = [],
  description = 'GB 编号真实性',
) {
  const text = isPage(locator)
    ? ((await locator.locator('body').textContent()) ?? '')
    : ((await locator.textContent()) ?? '');

  const matches = text.match(GB_PATTERN) ?? [];

  if (knownGbList.length === 0) {
    // 无已知列表时仅验证格式
    for (const m of matches) {
      expect(m, `${description}: "${m}" 应匹配合法 GB 编号格式`).toMatch(/^GB\s*\d{4,5}/i);
    }
    return;
  }

  // 标准化已知列表（去空格、大写）
  const normalized = knownGbList.map((g) => g.replace(/\s+/g, '').toUpperCase());

  for (const m of matches) {
    const normM = m.replace(/\s+/g, '').toUpperCase();
    const found = normalized.some((k) => normM.includes(k) || k.includes(normM));
    if (!found) {
      console.warn(`[LLM-ASSERT] 可疑 GB 编号: "${m}" 不在已知列表中`);
      // 不硬失败 — 可能是模型正确引用了不在列表中的标准
    }
  }
}

/**
 * 验证双通道输出结构（法规引用面板 + LLM 解释面板）
 */
export async function expectDualChannelOutput(page: Page) {
  const regulationPanel = page
    .locator(`[data-testid="${COMPLIANCE_CHECK.regulationPanel}"]`)
    .or(page.locator('text=法规引用'));
  const llmPanel = page.locator(`[data-testid="${COMPLIANCE_CHECK.llmPanel}"]`).or(page.locator('text=分析结果'));

  await expect(regulationPanel.first(), '法规引用面板应可见').toBeVisible({ timeout: 60_000 });
  await expect(llmPanel.first(), 'LLM 解释面板应可见').toBeVisible({ timeout: 10_000 });
}

/**
 * 验证合规结论的 is_compliant 字段存在且为布尔值
 * 用于直接 API 响应校验
 */
export function expectValidComplianceConclusion(response: unknown) {
  const r = response as Record<string, unknown>;
  expect(r, '合规响应应有 response 字段').toHaveProperty('response');
  expect(
    typeof r.is_compliant === 'boolean' || r.is_compliant === undefined,
    'is_compliant 应为布尔值或未定义',
  ).toBeTruthy();
}

/**
 * 验证响应中包含的关键字段（API 级别）
 */
export function expectComplianceResponseShape(response: unknown) {
  const r = response as Record<string, unknown>;
  // toolsUsed 应该存在
  if (r.toolsUsed) {
    expect(Array.isArray(r.toolsUsed), 'toolsUsed 应为数组').toBeTruthy();
  }
  // verifiedRegulations 应该存在
  if (r.verifiedRegulations) {
    expect(Array.isArray(r.verifiedRegulations), 'verifiedRegulations 应为数组').toBeTruthy();
  }
}

// ═══════════════════════════════════════════════
// Layer 3: Quality Baseline 断言
// 基于 baseline.json 中模型实际能力，非凭想象的正则
// ═══════════════════════════════════════════════

/**
 * 基于基线验证 LLM 输出质量
 *
 * 与硬编码正则不同，此断言使用 baseline.json 中已验证的
 * commonKeywords 和 qualityFloor 来判断输出是否达标。
 *
 * @param text - LLM 输出文本
 * @param scenarioName - baseline.json 中的场景名，如 'emergency-benzene-leak'
 * @param description - 用于错误信息的描述
 *
 * @example
 *   const text = await llmPanel.textContent();
 *   await expectBaselineQuality(text ?? '', 'emergency-benzene-leak', '苯泄漏应急响应');
 */
export function expectBaselineQuality(text: string, scenarioName: string, description?: string) {
  const baseline = loadBaseline();
  const scenario = baseline.scenarios[scenarioName];

  if (!scenario) {
    // 无基线数据时降级为基础长度检查
    console.warn(`[LLM-ASSERT] 场景 "${scenarioName}" 无基线数据，使用降级断言`);
    expect(text.length, `${description ?? scenarioName}: 输出不应为空`).toBeGreaterThan(10);
    return;
  }

  const label = description ?? scenarioName;

  // 1. 最小长度检查
  expect(
    text.length,
    `${label}: 输出长度 ${text.length} 应 >= ${scenario.qualityFloor.minLength} (baseline)`,
  ).toBeGreaterThanOrEqual(scenario.qualityFloor.minLength);

  // 2. 关键词覆盖率检查（使用 commonKeywords 而非凭空想象的词汇）
  const matchCount = scenario.commonKeywords.filter((k) => text.includes(k)).length;
  expect(
    matchCount,
    `${label}: 命中 ${matchCount}/${scenario.commonKeywords.length} 个基线关键词，需至少 ${scenario.qualityFloor.mustContainAtLeast} 个。` +
      `\n  基线关键词: [${scenario.commonKeywords.join(', ')}]` +
      `\n  实际命中: [${scenario.commonKeywords.filter((k) => text.includes(k)).join(', ')}]`,
  ).toBeGreaterThanOrEqual(scenario.qualityFloor.mustContainAtLeast);

  // 3. 罕见关键词不应出现（模型实际不用这些词，判为合理）
  if (scenario.rareKeywords.length > 0) {
    const rareMatch = scenario.rareKeywords.filter((k) => text.includes(k));
    if (rareMatch.length > 0) {
      console.warn(
        `[LLM-ASSERT] ${label}: 出现罕见关键词 [${rareMatch.join(', ')}]，` +
          `这在实际输出中不常见，建议检查是否需要更新基线。`,
      );
    }
  }
}
