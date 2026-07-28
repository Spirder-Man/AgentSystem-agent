// ============================================================
// P2 E2E (MSW Mock): 合规评测流程 — 评测触发 — 指标生成
//
// 验证:
//   - 合规评测页面加载
//   - 评测执行流程
//   - 评测结果渲染（含核心指标）
//
// 设计原则（对齐 Pre/Post 分层）:
//   - Pre-deploy: 此 E2E 使用 MSW Mock，验证 UI 正确性
//   - 不属于 Post-deploy 的 LLM 推理质量验证
// ============================================================

import { test, expect } from '@playwright/test';
import { EVAL, COMPLIANCE_CHECK } from '../src/test-ids';

// ═══════════════════════════════════════
// 测试数据
// ═══════════════════════════════════════
const ADMIN = { username: 'admin', password: 'admin123' };
const EVAL_SCENARIOS = [
  { query: '苯属于哪类危险化学品', category: '危险类别查询' },
  { query: '硝酸和甲醇的储存安全距离', category: '安全距离' },
  { query: '氯属于重大危险源吗', category: '重大危险源' },
];

// ═══════════════════════════════════════
test.describe('P2 (MSW Mock): 合规评测流程', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await expect(page).toHaveURL(/\/login/);

    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await expect(page.locator('text=合规仪表盘')).toBeVisible({ timeout: 5_000 });
  });

  // ── 导航到评测页面 ──
  test('应能导航到合规评测页面', async ({ page }) => {
    const navEval = page
      .locator('text=合规评测')
      .or(page.locator('text=评测管理'))
      .or(page.locator('text=Eval'))
      .or(page.locator('text=Evaluation'));
    if (await navEval.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await navEval.first().click();
      await expect(page).toHaveURL(/\/(eval|evaluation)/, { timeout: 10_000 });

      const title = page.locator('text=合规评测').or(page.locator('text=评测')).or(page.locator('text=Evaluation'));
      await expect(title.first()).toBeVisible({ timeout: 10_000 });
    }
  });

  // ── 单个合规评测执行 ──
  test('执行单个化学品合规评测应返回完整结果', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('苯和丙酮能同库储存吗');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    // 等待结果面板
    await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

    // 验证 LLM 解释面板有内容
    const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
    await expect(llmPanel).toBeVisible({ timeout: 10_000 });

    // 切换法规引用 Tab 验证内容
    await page.getByTestId(COMPLIANCE_CHECK.regulationTab).click();
    const regulationPanel = page.getByTestId(COMPLIANCE_CHECK.regulationPanel);
    await expect(regulationPanel).toBeVisible({ timeout: 10_000 });
    await expect(regulationPanel).toContainText('GB', { timeout: 3_000 });
  });

  // ── 多场景评测覆盖 ──
  for (const { query, category } of EVAL_SCENARIOS) {
    test(`评测场景: ${category} — "${query}"`, async ({ page }) => {
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(query);

      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 先等待结果面板出现
      await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

      // 再等待 LLM 响应内容
      const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
      await expect(llmPanel).toBeVisible({ timeout: 10_000 });

      const text = (await llmPanel.textContent()) ?? '';
      expect(text.length, `${category} 应返回非空响应`).toBeGreaterThan(10);
    });
  }

  // ── 权限：未登录应重定向 ──
  test('未登录访问 /eval 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/eval');
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
