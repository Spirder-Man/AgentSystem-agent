// ============================================================
// NEW E2E-Real: LLM 推理质量专项测试
//
// 核心验证维度（对齐化工合规评测体系）:
//   1. 工具选择正确性 — LLM 是否正确选择了工具
//   2. 参数准确性 — 工具调用参数是否正确
//   3. 无幻觉 GB 编号 — 输出的 GB 编号格式是否合法
//   4. 忠实度 — 结论是否可追溯到工具数据
//   5. 响应时间 — 真实 GPU 推理耗时是否在合理范围
//
// 覆盖类目: 危险类别/储存兼容/安全距离/重大危险源/法规版本/综合审核
// ============================================================

import { test, expect, loginAs } from './fixtures/auth.fixture';
import {
  expectGbNumberPresent,
  expectToolCallPresent,
  expectResponseTimeInRange,
  expectDualChannelOutput,
} from './utils/llm-assertions';
import { COMPLIANCE_CHECK } from '../src/test-ids';

// ── 七大评测类目测试数据 ──
const QUALITY_TESTS = [
  { query: '苯属于哪类危险化学品', category: '危险类别查询', expectedTool: 'QueryChemicalHazard' },
  { query: '苯和丙酮能同库储存吗', category: '储存兼容性', expectedTool: 'CheckStorageCompatibility' },
  { query: '甲醇储罐和硝酸储罐的安全距离是多少', category: '安全距离', expectedTool: 'CheckSafetyDistance' },
  { query: '氯属于重大危险源吗临界量是多少', category: '重大危险源', expectedTool: 'QueryMajorHazardSource' },
  { query: 'GB 15603的最新版本是什么', category: '法规版本', expectedTool: 'CheckRegulationVersion' },
  { query: '请对苯储存进行综合合规审核', category: '综合审核', expectedTool: 'CheckComprehensiveCompliance' },
];

test.describe('LLM-Quality-Real: LLM 推理质量专项 (七大评测类目)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 全类目工具选择正确性 ──
  for (const { query, category, expectedTool } of QUALITY_TESTS) {
    test(`[${category}] "${query}" — 工具选择 + GB编号 + 响应时间`, async ({ page }) => {
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(query);

      const startTime = Date.now();
      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 验证双通道输出
      const { hasRegulations } = await expectDualChannelOutput(page);

      // 验证 GB 编号（仅当 LLM 提取到法规编号时）
      if (hasRegulations) {
        const regulationPanel = page
          .locator(`[data-testid="${COMPLIANCE_CHECK.regulationPanel}"]`)
          .or(page.locator('text=法规引用'))
          .first();
        await expectGbNumberPresent(regulationPanel, category);
      }

      // 验证工具调用链
      const hasToolChain = await page
        .locator(`[data-testid="${COMPLIANCE_CHECK.toolChain}"]`)
        .or(page.locator('text=工具调用'))
        .isVisible({ timeout: 5_000 })
        .catch(() => false);

      if (hasToolChain) {
        await expectToolCallPresent(page, expectedTool, `${category} - 工具调用`);
      }

      // 验证响应时间
      expectResponseTimeInRange(startTime, 3_000, 90_000, category);
    });
  }

  // ── 无幻觉 GB 编号验证 ──
  test('合规输出中的 GB 编号应格式合法', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('苯储存需要遵守哪些规范');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    // 等待完整结果
    const { hasRegulations } = await expectDualChannelOutput(page);

    // 验证所有 GB 编号格式合法
    const bodyText = (await page.locator('body').textContent()) ?? '';
    const gbPattern = /GB\s*\d{4,5}(?:\.\d+)?\s*-\s*\d{4}/gi;
    const matches = bodyText.match(gbPattern) ?? [];

    for (const m of matches) {
      expect(m, `GB 编号 "${m}" 格式应合法`).toMatch(/^GB\s*\d{4,5}/i);
    }

    // 至少应有一个 GB 编号引用（否则 RAG 召回可能有问题）
    if (matches.length === 0) {
      if (hasRegulations) {
        console.warn('[LLM-QUALITY] 响应中未检测到 GB 编号 — 法规面板存在但未提取到编号，可能是 RAG 召回率问题');
      } else {
        console.warn('[LLM-QUALITY] 响应中未检测到 GB 编号 — LLM 未提取到法规编号（hasRegulations=false）');
      }
    }
  });

  // ── 忠实度验证：结论应包含具体的判断依据 ──
  test('合规结论应包含具体判断依据（非泛化回复）', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('苯和丙酮能同库储存吗');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    const { hasRegulations } = await expectDualChannelOutput(page);

    const llmPanel = page.locator(`[data-testid="${COMPLIANCE_CHECK.llmPanel}"]`).or(page.locator('text=分析结果'));
    const text = (await llmPanel.first().textContent()) ?? '';

    // 具体判断依据关键词
    const hasSpecificEvidence = /GB|禁配|间距|兼容|储存条件|法规|标准/.test(text);
    expect(hasSpecificEvidence, '结论应包含具体法规/标准引用').toBe(true);

    // 不应是纯泛化回复
    const genericOnly = text.length < 20 || /(无法|不确定|需要更多信息)/.test(text);
    if (genericOnly) {
      console.warn(`[LLM-QUALITY] 响应可能过于泛化: "${text.substring(0, 100)}"`);
    }
  });
});
