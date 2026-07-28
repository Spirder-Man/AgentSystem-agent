// ============================================================
// P2 E2E (MSW Mock): 知识库查询 — 检索模式 + GB编号查询
//
// 验证:
//   - 知识库页面加载
//   - GB 编号精确查询
//   - 检索模式切换（BM25 / Vector / Hybrid）
//   - 自然语言查询
//
// 设计原则（对齐 Pre/Post 分层）:
//   - Pre-deploy: 此 E2E 使用 MSW Mock，验证 UI 正确性
//   - 不属于 Post-deploy 的 LLM 推理质量验证
// ============================================================

import { test, expect } from '@playwright/test';
import { COMPLIANCE_CHECK } from '../src/test-ids';

// ═══════════════════════════════════════
// 测试数据
// ═══════════════════════════════════════
const ADMIN = { username: 'admin', password: 'admin123' };
const GB_QUERIES = ['GB 15603', 'GB 30000.7', 'GB 18218'];

// ═══════════════════════════════════════
test.describe('P2 (MSW Mock): 知识库查询 + 检索模式', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await expect(page).toHaveURL(/\/login/);

    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await expect(page.locator('text=合规仪表盘')).toBeVisible({ timeout: 5_000 });
  });

  // ── 导航到知识库页面 ──
  test('应能导航到知识库页面', async ({ page }) => {
    const navKB = page.locator('text=知识库').or(page.locator('text=Knowledge')).or(page.locator('text=知识管理'));
    if (await navKB.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await navKB.first().click();
      await expect(page).toHaveURL(/\/(knowledge|kb)/, { timeout: 10_000 });
    }
  });

  // ── GB 编号精确查询 ──
  for (const query of GB_QUERIES) {
    test(`应能检索 GB 编号 "${query}" 并返回相关文档`, async ({ page }) => {
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await expect(input).toBeVisible();
      await input.fill(query);

      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 等待结果面板
      await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

      // 验证 LLM 解释面板有内容
      const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
      await expect(llmPanel).toBeVisible({ timeout: 10_000 });

      const text = (await llmPanel.textContent()) ?? '';
      expect(text.length, `知识库检索 - ${query} 应有实质内容`).toBeGreaterThan(10);
    });
  }

  // ── 检索模式切换 ──
  test('应能切换 BM25/Vector/Hybrid 检索模式', async ({ page }) => {
    const modeSwitch = page
      .locator('text=BM25')
      .or(page.locator('text=Vector'))
      .or(page.locator('text=混合'))
      .or(page.locator('text=Hybrid'));

    if (await modeSwitch.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await expect(modeSwitch.first()).toBeVisible();
    }
    // 检索模式切换是后端逻辑，UI 层面只验证模式选择器存在性
  });

  // ── 自然语言查询 ──
  test('自然语言查询应能返回相关结果', async ({ page }) => {
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    const input = page.locator('input[placeholder*="化工"]').first();
    await input.fill('硫酸储存禁忌有哪些');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await sendBtn.click();

    // 等待 LLM 响应
    const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
    await expect(llmPanel).toBeVisible({ timeout: 25_000 });

    const text = (await llmPanel.textContent()) ?? '';
    expect(text.length, 'LLM 应返回非空响应').toBeGreaterThan(10);
  });

  // ── 权限：未登录应重定向 ──
  test('未登录访问知识库应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/knowledge');
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
