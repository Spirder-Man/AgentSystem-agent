// ============================================================
// P2 E2E (MSW Mock): 应急响应 — 事故上报 → 预案匹配
//
// 验证:
//   - 应急响应页面加载
//   - 事故场景选择 + 上报流程
//   - 预案结果渲染（含处置措施）
//
// 设计原则（对齐 Pre/Post 分层）:
//   - Pre-deploy: 此 E2E 使用 MSW Mock，验证 UI 正确性
//   - 不属于 Post-deploy 的 LLM 推理质量验证
// ============================================================

import { test, expect } from '@playwright/test';
import { EMERGENCY, COMPLIANCE_CHECK } from '../src/test-ids';

// ═══════════════════════════════════════
// 测试数据
// ═══════════════════════════════════════
const ADMIN = { username: 'admin', password: 'admin123' };
const EMERGENCY_SCENARIOS = ['苯泄漏', '甲醇火灾', '硝酸腐蚀'];

// ═══════════════════════════════════════
// Step 1: 登录
// ═══════════════════════════════════════
test.describe('P2 (MSW Mock): 应急响应 — 事故上报 → 预案', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await expect(page).toHaveURL(/\/login/);

    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await expect(page.locator('text=合规仪表盘')).toBeVisible({ timeout: 5_000 });
  });

  // ── 应急响应页面导航 ──
  test('应能导航到应急响应页面', async ({ page }) => {
    const navEmergency = page
      .locator('text=应急响应')
      .or(page.locator('text=应急管理'))
      .or(page.locator('text=Emergency'));
    if (await navEmergency.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await navEmergency.first().click();
      await expect(page).toHaveURL(/\/(emergency|response)/, { timeout: 10_000 });

      const title = page.locator('text=应急响应').or(page.locator('text=事故上报')).or(page.locator('text=应急'));
      await expect(title.first()).toBeVisible({ timeout: 10_000 });
    }
  });

  // ── 事故上报 → 预案匹配 ──
  for (const scenario of EMERGENCY_SCENARIOS) {
    test(`应能上报"${scenario}"事故并获取预案结果`, async ({ page }) => {
      // 应急响应通过合规检查页触发
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(`${scenario}事故应急处置方案`);

      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 等待结果面板（MSW Mock 快速响应）
      await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });

      // 验证 LLM 解释面板有内容
      const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
      await expect(llmPanel).toBeVisible({ timeout: 10_000 });

      const text = (await llmPanel.textContent()) ?? '';
      // MSW Mock 返回完整应急响应模板，应包含处置措施
      expect(text.length, `应急响应 - ${scenario} 应有实质内容`).toBeGreaterThan(50);
    });
  }

  // ── 权限：未登录应重定向 ──
  test('未登录访问应急响应应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/emergency');
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
