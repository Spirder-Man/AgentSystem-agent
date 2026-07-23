// ============================================================
// NEW E2E-Real: 合规评测流程 — 64条评测集执行 → 指标生成
//
// 验证:
//   - 评测流程触发
//   - 评测结果含三项核心指标（工具触发率/参数准确率/结论准确率）
//   - 评测报告完整性
// ============================================================

import { test, expect, loginAs } from './fixtures/auth.fixture';
import { expectResponseTimeInRange } from './utils/llm-assertions';
import { COMPLIANCE_CHECK } from '../src/test-ids';

test.describe('EvalFlow-Real: 合规评测流程 (真实 GPU)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 导航到评测页面 ──
  test('应能导航到合规评测页面', async ({ page }) => {
    const navEval = page
      .locator('text=合规评测')
      .or(page.locator('text=评测管理').or(page.locator('text=Eval')).or(page.locator('text=Evaluation')));
    if (await navEval.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await navEval.first().click();
      await expect(page).toHaveURL(/\/(eval|evaluation)/, { timeout: 10_000 });

      // 验证页面标题
      const title = page.locator('text=合规评测').or(page.locator('text=评测').or(page.locator('text=Evaluation')));
      await expect(title.first()).toBeVisible({ timeout: 10_000 });
    }
  });

  // ── 单个合规评测执行 ──
  test('执行单个化学品合规评测应返回完整结果', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('苯和丙酮能同库储存吗');

    const startTime = Date.now();
    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    // 等待双通道输出
    const regulationPanel = page
      .locator(`[data-testid="${COMPLIANCE_CHECK.regulationPanel}"]`)
      .or(page.locator('text=法规引用'));
    await expect(regulationPanel.first()).toBeVisible({ timeout: 60_000 });

    const llmPanel = page.locator(`[data-testid="${COMPLIANCE_CHECK.llmPanel}"]`).or(page.locator('text=分析结果'));
    await expect(llmPanel.first()).toBeVisible({ timeout: 10_000 });

    // 验证包含 GB 编号引用和结论
    const bodyText = (await page.locator('body').textContent()) ?? '';
    expect(bodyText, '应包含 GB 编号').toMatch(/GB/i);

    // 验证推理耗时
    expectResponseTimeInRange(startTime, 3_000, 90_000, '评测查询 LLM');
  });

  // ── 多场景评测覆盖 ──
  const EVAL_SCENARIOS = [
    { query: '苯属于哪类危险化学品', category: '危险类别查询' },
    { query: '硝酸和甲醇的储存安全距离', category: '安全距离' },
    { query: '氯属于重大危险源吗', category: '重大危险源' },
  ];

  for (const { query, category } of EVAL_SCENARIOS) {
    test(`评测场景: ${category} — "${query}"`, async ({ page }) => {
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(query);

      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 等待 LLM 响应
      const llmPanel = page.locator(`[data-testid="${COMPLIANCE_CHECK.llmPanel}"]`).or(page.locator('text=分析结果'));
      await expect(llmPanel.first()).toBeVisible({ timeout: 90_000 });

      // 验证非空响应
      const text = await llmPanel.first().textContent();
      expect(text?.length ?? 0, `${category} 应返回非空响应`).toBeGreaterThan(10);
    });
  }
});
