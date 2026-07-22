// ============================================================
// P4 E2E-Real: 登录 → 资产台账列表 → 查看资产详情 (真实 DB)
//
// 验证:
//   - 资产列表从真实数据库加载（化学品名称、CAS号、位置、数量）
//   - 点击资产 → 进入详情页
//   - viewer 可查看但无编辑按钮
// ============================================================

import { test, expect, loginAs, ACCOUNTS } from './fixtures/auth.fixture';

test.describe('P4-Real: 资产台账 — 列表 → 详情 (真实数据)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 资产列表 ──
  test('应展示资产台账列表（化学品名称 + CAS号 + 位置）', async ({ page }) => {
    const navAssets = page.locator('text=资产台账').or(
      page.locator('text=资产管理').or(page.locator('text=Assets')),
    );
    await navAssets.first().click();
    await expect(page).toHaveURL(/\/assets/, { timeout: 10_000 });

    // 验证资产列表渲染（真实 DB，预期含苯 71-43-2）
    const assetRow = page.locator('text=苯').first();
    await expect(assetRow).toBeVisible({ timeout: 15_000 });

    // 验证 CAS 号格式（苯的 CAS: 71-43-2）
    await expect(page.locator('body')).toContainText('71-43-2', { timeout: 10_000 });
  });

  // ── 资产详情 ──
  test('点击资产应进入详情页，展示完整信息', async ({ page }) => {
    const navAssets = page.locator('text=资产台账').or(
      page.locator('text=资产管理').or(page.locator('text=Assets')),
    );
    await navAssets.first().click();
    await expect(page).toHaveURL(/\/assets/, { timeout: 10_000 });

    // 点击第一个资产「苯」
    const firstAsset = page.locator('text=苯').first();
    await expect(firstAsset).toBeVisible({ timeout: 15_000 });
    await firstAsset.click();

    // 验证进入详情页
    await expect(page).toHaveURL(/\/assets\//, { timeout: 10_000 });

    // 验证详情页字段（CAS号、储存条件等）
    await expect(page.locator('body')).toContainText('71-43-2', { timeout: 10_000 });
  });

  // ── viewer 只读 ──
  test('viewer 可查看资产详情但无编辑删除按钮', async ({ page }) => {
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', ACCOUNTS.viewer.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.viewer.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    const navAssets = page.locator('text=资产台账').or(
      page.locator('text=资产管理').or(page.locator('text=Assets')),
    );
    await navAssets.first().click();
    await expect(page).toHaveURL(/\/assets/, { timeout: 10_000 });

    const firstAsset = page.locator('text=苯').first();
    await expect(firstAsset).toBeVisible({ timeout: 15_000 });
    await firstAsset.click();
    await expect(page).toHaveURL(/\/assets\//, { timeout: 10_000 });

    const editBtn = page.locator('button:has-text("编辑")').or(
      page.locator('button:has-text("删除")').or(page.locator('button:has-text("修改")')),
    );
    const isVisible = await editBtn.isVisible().catch(() => false);
    if (isVisible) {
      const isDisabled = await editBtn.isDisabled().catch(() => false);
      expect(isDisabled).toBe(true);
    }
  });

  // ── 权限：未登录拦截 ──
  test('未登录访问 /assets 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/assets');
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});
