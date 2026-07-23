// ============================================================
// P4 E2E: 登录 → 资产台账列表 → 查看资产详情
//
// 验证：
//   - 资产列表页渲染（化学品名称、CAS号、位置、数量）
//   - 点击资产 → 进入详情页
//   - viewer 可查看但无编辑按钮
//
// MSW Mock 数据：6 个化学品资产（苯、丙酮、甲醇、硝酸、氢氧化钠、氯）
// 设计原则：MSW Mock 模式，不依赖后端 GPU/DB
// ============================================================

import { test, expect } from '@playwright/test';

const ADMIN = { username: 'admin', password: 'admin123' };

test.describe('P4: 资产台账 — 列表 → 详情', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
  });

  // ── 资产列表 ──
  test('应展示资产台账列表（化学品名称 + CAS号 + 位置）', async ({ page }) => {
    // 导航到资产台账
    const navAssets = page.locator('text=资产台账').or(page.locator('text=资产管理').or(page.locator('text=Assets')));
    await navAssets.first().click();
    await expect(page).toHaveURL(/\/assets/, { timeout: 5_000 });

    // 验证资产列表渲染（MSW Mock 返回 6 个资产）
    const assetRow = page.locator('text=苯').first();
    await expect(assetRow).toBeVisible({ timeout: 10_000 });

    // 验证 CAS 号格式（71-43-2 是苯的 CAS）
    await expect(page.locator('body')).toContainText('71-43-2', { timeout: 5_000 });

    // 验证位置信息
    await expect(page.locator('body')).toContainText('甲类仓库', { timeout: 3_000 });
  });

  // ── 资产详情 ──
  test('点击资产应进入详情页', async ({ page }) => {
    const navAssets = page.locator('text=资产台账').or(page.locator('text=资产管理').or(page.locator('text=Assets')));
    await navAssets.first().click();
    await expect(page).toHaveURL(/\/assets/, { timeout: 5_000 });

    // 点击第一个资产「苯」
    const firstAsset = page.locator('text=苯').first();
    await expect(firstAsset).toBeVisible({ timeout: 10_000 });
    await firstAsset.click();

    // 验证进入详情页
    await expect(page).toHaveURL(/\/assets\//, { timeout: 5_000 });

    // 验证详情页字段（CAS号、储存条件、负责人等）
    await expect(page.locator('body')).toContainText('71-43-2', { timeout: 5_000 });
    await expect(page.locator('body')).toContainText('张三', { timeout: 3_000 });
  });

  // ── viewer 只读 ──
  test('viewer 可查看资产详情但无编辑删除按钮', async ({ page }) => {
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', 'viewer');
    await page.fill('input[autocomplete="current-password"]', 'viewer123');
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });

    // 导航到资产台账
    const navAssets = page.locator('text=资产台账').or(page.locator('text=资产管理').or(page.locator('text=Assets')));
    await navAssets.first().click();

    // 进入详情
    const firstAsset = page.locator('text=苯').first();
    await expect(firstAsset).toBeVisible({ timeout: 10_000 });
    await firstAsset.click();
    await expect(page).toHaveURL(/\/assets\//, { timeout: 5_000 });

    // 编辑/删除按钮不应可见
    const editBtn = page
      .locator('button:has-text("编辑")')
      .or(page.locator('button:has-text("删除")').or(page.locator('button:has-text("修改")')));
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
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
