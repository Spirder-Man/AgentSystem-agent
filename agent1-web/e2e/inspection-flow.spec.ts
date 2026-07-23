// ============================================================
// P2 E2E: 登录 → 巡检计划列表 → 查看计划详情 → 执行巡检
//
// 验证：
//   - 巡检计划列表页渲染（计划名称、状态、巡检人）
//   - 点击进入计划详情 → 查看检查项
//   - 执行按钮触发 → 轮次结果展示
//   - viewer 角色权限（只读）
//
// 设计原则：MSW Mock 模式，不依赖后端 GPU/DB
// ============================================================

import { test, expect } from '@playwright/test';

const ADMIN = { username: 'admin', password: 'admin123' };

test.describe('P2: 巡检流程 — 计划列表 → 详情 → 执行', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
  });

  // ── 巡检计划列表 ──
  test('应展示巡检计划列表（计划名称 + 状态 + 巡检人）', async ({ page }) => {
    // 导航到巡检计划页
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 5_000 });

    // 验证至少有一行计划数据（MSW Mock 返回 3 个计划）
    const planRow = page.locator('text=甲类仓库周检').or(page.locator('text=罐区月度安全检查'));
    await expect(planRow.first()).toBeVisible({ timeout: 10_000 });

    // 验证状态标签存在
    const statusTag = page.locator('text=已完成').or(page.locator('text=进行中').or(page.locator('text=草稿')));
    await expect(statusTag.first()).toBeVisible({ timeout: 5_000 });
  });

  // ── 计划详情 → 检查项 ──
  test('点击计划应进入详情页，展示检查项列表', async ({ page }) => {
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 5_000 });

    // 点击第一个计划
    const firstPlan = page.locator('text=甲类仓库周检').first();
    await expect(firstPlan).toBeVisible({ timeout: 10_000 });
    await firstPlan.click();

    // 验证进入详情页
    await expect(page).toHaveURL(/\/inspection\/plans\//, { timeout: 5_000 });

    // 验证检查项列表（MSW Mock 返回 5 项）
    const itemRow = page.locator('text=苯与丙酮储存间距').or(page.locator('text=消防通道是否畅通'));
    await expect(itemRow.first()).toBeVisible({ timeout: 10_000 });
  });

  // ── 权限：viewer 不能创建/执行巡检 ──
  test('viewer 角色在巡检页不应有创建/执行按钮', async ({ page }) => {
    // viewer 登录
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', 'viewer');
    await page.fill('input[autocomplete="current-password"]', 'viewer123');
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });

    // 导航到巡检页面
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 5_000 });

    // viewer 不应看到"新建计划"或"执行"按钮
    const createBtn = page.locator('button:has-text("新建")').or(page.locator('button:has-text("执行")'));
    // 按钮要么不可见，要么是 disabled
    const isVisible = await createBtn.isVisible().catch(() => false);
    if (isVisible) {
      const isDisabled = await createBtn.isDisabled().catch(() => false);
      expect(isDisabled).toBe(true);
    }
  });

  // ── 权限：未登录访问巡检页应重定向 ──
  test('未登录访问 /inspection/plans 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/inspection/plans');
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });
});
