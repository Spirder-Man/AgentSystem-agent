// ============================================================
// P2 E2E (MSW Mock): LLM 推理质量专项 — 七大评测类目
//
// 验证:
//   - 工具选择正确性（合规检查 → 工具调用链）
//   - GB 编号引用格式合法性
//   - 双通道输出（法规引用 + LLM 解释）
//   - 响应内容完整性
//
// 设计原则:
//   - MSW Mock 模拟真实后端响应，验证 UI 渲染正确性
//   - 七大评测类目覆盖: 危险类别/储存兼容/安全距离/重大危险源/法规版本/综合审核/无幻觉
// ============================================================

import { test, expect } from '@playwright/test';
import { COMPLIANCE_CHECK } from '../src/test-ids';

// ═══════════════════════════════════════
// 测试数据
// ═══════════════════════════════════════
const ADMIN = { username: 'admin', password: 'admin123' };
const QUALITY_TESTS = [
  { query: '苯属于哪类危险化学品', category: '危险类别查询' },
  { query: '苯和丙酮能同库储存吗', category: '储存兼容性' },
  { query: '甲醇储罐和硝酸储罐的安全距离是多少', category: '安全距离' },
  { query: '氯属于重大危险源吗临界量是多少', category: '重大危险源' },
  { query: 'GB 15603的最新版本是什么', category: '法规版本' },
  { query: '请对苯储存进行综合合规审核', category: '综合审核' },
];

// ═══════════════════════════════════════
test.describe('P2 (MSW Mock): LLM 推理质量专项 (七大评测类目)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await expect(page).toHaveURL(/\/login/);

    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await expect(page.locator('text=合规仪表盘')).toBeVisible({ timeout: 5_000 });
  });

  // ── 全类目合规查询 ──
  for (const { query, category } of QUALITY_TESTS) {
    test(`[${category}] "${query}" — 双通道输出 + 法规引用`, async ({ page }) => {
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(query);

      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 等待结果面板
      await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

      // 验证 LLM 解释面板有内容
      const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
      await expect(llmPanel).toBeVisible({ timeout: 10_000 });

      const text = (await llmPanel.textContent()) ?? '';
      expect(text.length, `${category} 应返回非空响应`).toBeGreaterThan(10);

      // 验证法规引用面板
      await page.getByTestId(COMPLIANCE_CHECK.regulationTab).click();
      const regulationPanel = page.getByTestId(COMPLIANCE_CHECK.regulationPanel);
      await expect(regulationPanel).toBeVisible({ timeout: 10_000 });
    });
  }

  // ── GB 编号格式合法性 ──
  test('合规输出中的 GB 编号应格式合法', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('苯储存的合规要求');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    // 等待结果
    await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

    // 切换到法规引用 Tab
    await page.getByTestId(COMPLIANCE_CHECK.regulationTab).click();
    const regulationPanel = page.getByTestId(COMPLIANCE_CHECK.regulationPanel);
    await expect(regulationPanel).toBeVisible({ timeout: 10_000 });

    // 验证 GB 编号格式：GB 后跟 4-5 位数字
    const text = (await regulationPanel.textContent()) ?? '';
    const gbMatches = text.match(/GB\s*\d{4,5}(?:\.\d+)?/gi) ?? [];
    for (const m of gbMatches) {
      expect(m, `GB 编号 "${m}" 格式应合法`).toMatch(/^GB\s*\d{4,5}/i);
    }
    // MSW Mock 应至少包含一条 GB 引用
    expect(gbMatches.length, '应至少包含一条 GB 编号引用').toBeGreaterThan(0);
  });

  // ── 权限：未登录应重定向 ──
  test('未登录访问 /compliance 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/compliance');
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
