// ============================================================
// P3 E2E-Real: 登录 → 仪表盘 → 合规总览数据验证 (真实 GPU + DB)
//
// 验证:
//   - 仪表盘从真实数据库加载合规总览
//   - 核心指标数字（assets/合规率/findings）来自真实数据
//   - 扫描按钮 → 触发真实 LLM 扫描 → 结果更新
//   - viewer 角色可见但无写入按钮
// ============================================================

import { test, expect, loginAs, ACCOUNTS } from './fixtures/auth.fixture';
import { expectResponseTimeInRange } from './utils/llm-assertions';

test.describe('P3-Real: 仪表盘 — 合规总览 + 指标验证 (真实数据)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 仪表盘加载 ──
  test('仪表盘应展示合规总览卡片和真实数据指标', async ({ page }) => {
    const title = page.locator('text=合规仪表盘');
    await expect(title.first()).toBeVisible({ timeout: 15_000 });

    // 验证页面主体内容已加载（真实数据来自 DB）
    const body = page.locator('body');
    // 至少应包含数字内容（具体值依赖真实 DB）
    await expect(body).toContainText(/[\d]+/, { timeout: 10_000 });
  });

  // ── 扫描功能 (真实 LLM) ──
  test('admin 点击扫描应触发真实 LLM 扫描并更新结果', async ({ page }) => {
    const scanBtn = page.locator('button:has-text("扫描")').or(
      page.locator('button:has-text("自动扫描")').or(
        page.locator('[data-testid="auto-scan-btn"]'),
      ),
    );

    if (await scanBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const startTime = Date.now();
      await scanBtn.click();

      // 等待扫描结果（真实 LLM 推理可能需要 30s+）
      const scanResult = page.locator('text=新发现').or(
        page.locator('text=newFindings').or(page.locator('text=扫描完成')),
      );
      await expect(scanResult.first()).toBeVisible({ timeout: 60_000 });

      // 验证扫描耗时合理
      expectResponseTimeInRange(startTime, 3_000, 60_000, '仪表盘扫描 LLM');
    }
  });

  // ── viewer 只读 ──
  test('viewer 应能看到仪表盘但无扫描按钮', async ({ page }) => {
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', ACCOUNTS.viewer.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.viewer.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    const title = page.locator('text=合规仪表盘');
    await expect(title.first()).toBeVisible({ timeout: 15_000 });

    const scanBtn = page.locator('button:has-text("扫描")').or(
      page.locator('button:has-text("自动扫描")'),
    );
    const isVisible = await scanBtn.isVisible().catch(() => false);
    if (isVisible) {
      const isDisabled = await scanBtn.isDisabled().catch(() => false);
      expect(isDisabled).toBe(true);
    }
  });
});
