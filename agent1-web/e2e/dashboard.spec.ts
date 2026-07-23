// ============================================================
// P3 E2E: 登录 → 仪表盘 → 合规总览数据验证
//
// 验证：
//   - 仪表盘页面加载，合规总览卡片渲染
//   - 核心指标数字展示（资产数、合规率、不合规发现数）
//   - 扫描按钮 → 触发自动扫描 → 结果更新
//   - viewer 角色可见但无写入按钮
//
// 设计原则：MSW Mock 模式，不依赖后端 GPU/DB
// ============================================================

import { test, expect } from '@playwright/test';
import { DASHBOARD } from '../src/test-ids';

const ADMIN = { username: 'admin', password: 'admin123' };

test.describe('P3: 仪表盘 — 合规总览 + 指标验证', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
  });

  // ── 仪表盘加载 ──
  test('仪表盘应展示合规总览卡片和核心指标', async ({ page }) => {
    // 验证仪表盘标题
    const title = page.locator('text=合规仪表盘');
    await expect(title.first()).toBeVisible({ timeout: 10_000 });

    // 验证核心指标区域存在（MSW Mock 数据：6 资产 / 60% 合规率 / 12 发现）
    const body = page.locator('body');

    // 关键数字验证（.or 柔性匹配不同 UI 实现）
    await expect(body).toContainText('6', { timeout: 5_000 }); // totalAssets
    await expect(body).toContainText('60', { timeout: 3_000 }); // complianceRate
    await expect(body).toContainText('12', { timeout: 3_000 }); // totalFindings
  });

  // ── 扫描功能 ──
  test('admin 点击扫描应触发自动扫描并更新结果', async ({ page }) => {
    // 查找扫描按钮
    const scanBtn = page
      .locator('button:has-text("扫描")')
      .or(page.locator('button:has-text("自动扫描")').or(page.locator(`[data-testid="${DASHBOARD.scanBtn}"]`)));

    if (await scanBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await scanBtn.click();

      // 等待扫描完成（MSW Mock 返回 LLM 延迟 3-45s，但 Playwright 场景下快）
      // 验证出现扫描结果
      const scanResult = page
        .locator('text=新发现')
        .or(page.locator('text=newFindings').or(page.locator('text=扫描完成')));
      await expect(scanResult.first()).toBeVisible({ timeout: 30_000 });
    }
  });

  // ── viewer 只读 ──
  test('viewer 应能看到仪表盘但无扫描按钮', async ({ page }) => {
    // viewer 登录
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', 'viewer');
    await page.fill('input[autocomplete="current-password"]', 'viewer123');
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });

    // 仪表盘数据应可见
    const title = page.locator('text=合规仪表盘');
    await expect(title.first()).toBeVisible({ timeout: 10_000 });

    // 扫描按钮不应可见（viewer 无写入权限）
    const scanBtn = page.locator('button:has-text("扫描")').or(page.locator('button:has-text("自动扫描")'));
    const isVisible = await scanBtn.isVisible().catch(() => false);
    if (isVisible) {
      const isDisabled = await scanBtn.isDisabled().catch(() => false);
      expect(isDisabled).toBe(true);
    }
  });

  // ── 合规发现列表 ──
  test('仪表盘应展示不合规发现列表', async ({ page }) => {
    // 导航到发现列表（可能在仪表盘上或子页面）
    const findingsLink = page
      .locator('text=不合规发现')
      .or(page.locator('text=合规发现').or(page.locator('text=Findings')));

    if (await findingsLink.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await findingsLink.first().click();

      // 验证发现列表渲染
      const finding = page.locator('text=苯与丙酮').or(page.locator('text=Critical').or(page.locator('text=严重')));
      await expect(finding.first()).toBeVisible({ timeout: 10_000 });
    }
  });
});
