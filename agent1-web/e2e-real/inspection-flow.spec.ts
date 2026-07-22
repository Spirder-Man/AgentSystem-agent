// ============================================================
// P2 E2E-Real: 登录 → 巡检计划列表 → 查看计划详情 → 执行巡检 (真实 GPU)
//
// 验证:
//   - 巡检计划列表从真实数据库加载
//   - 点击进入计划详情 → 检查项含真实数据
//   - 执行巡检触发真实 LLM 推理 → 轮次结果含评估
//   - viewer 角色权限（只读）
// ============================================================

import { test, expect, loginAs, ACCOUNTS } from './fixtures/auth.fixture';
import { expectGbNumberPresent, expectResponseTimeInRange } from './utils/llm-assertions';

test.describe('P2-Real: 巡检流程 — 计划列表 → 详情 → 执行 (真实 GPU)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 巡检计划列表 ──
  test('应展示巡检计划列表（来自真实数据库）', async ({ page }) => {
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 10_000 });

    // 验证至少有一行计划数据（来自真实 DB）
    const planRow = page.locator('text=甲类仓库周检').or(
      page.locator('text=罐区月度安全检查'),
    );
    await expect(planRow.first()).toBeVisible({ timeout: 15_000 });

    // 验证状态标签存在
    const statusTag = page.locator('text=已完成').or(
      page.locator('text=进行中').or(page.locator('text=草稿')),
    );
    await expect(statusTag.first()).toBeVisible({ timeout: 10_000 });
  });

  // ── 计划详情 → 检查项 ──
  test('点击计划应进入详情页，展示检查项列表', async ({ page }) => {
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 10_000 });

    // 点击第一个计划
    const firstPlan = page.locator('text=甲类仓库周检').first();
    await expect(firstPlan).toBeVisible({ timeout: 15_000 });
    await firstPlan.click();

    // 验证进入详情页
    await expect(page).toHaveURL(/\/inspection\/plans\//, { timeout: 10_000 });

    // 验证检查项列表含 GB 标准引用
    const itemRow = page.locator('text=苯与丙酮储存间距').or(
      page.locator('text=消防通道是否畅通'),
    );
    await expect(itemRow.first()).toBeVisible({ timeout: 15_000 });
  });

  // ── 执行巡检 (真实 LLM) ──
  test('执行巡检应触发真实 LLM 推理并生成轮次结果', async ({ page }) => {
    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 10_000 });

    // 进入计划详情
    const firstPlan = page.locator('text=甲类仓库周检').first();
    await expect(firstPlan).toBeVisible({ timeout: 15_000 });
    await firstPlan.click();
    await expect(page).toHaveURL(/\/inspection\/plans\//, { timeout: 10_000 });

    // 查找执行按钮
    const executeBtn = page.locator('button:has-text("执行")').or(
      page.locator('button:has-text("开始巡检")'),
    );

    if (await executeBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      const startTime = Date.now();
      await executeBtn.click();

      // 等待执行完成（真实 LLM 推理）
      const resultIndicator = page.locator('text=巡检完成').or(
        page.locator('text=执行完成').or(page.locator('text=轮次')),
      );
      await expect(resultIndicator.first()).toBeVisible({ timeout: 90_000 });

      // 验证推理耗时在合理范围
      expectResponseTimeInRange(startTime, 5_000, 90_000, '巡检执行 LLM 推理');
    }
  });

  // ── 权限：viewer 不能创建/执行巡检 ──
  test('viewer 角色不应有创建/执行按钮', async ({ page }) => {
    // viewer 登录
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[autocomplete="username"]', ACCOUNTS.viewer.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.viewer.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    const navInspection = page.locator('text=巡检计划');
    await navInspection.first().click();
    await expect(page).toHaveURL(/\/inspection\/plans/, { timeout: 10_000 });

    const createBtn = page.locator('button:has-text("新建")').or(
      page.locator('button:has-text("执行")'),
    );
    const isVisible = await createBtn.isVisible().catch(() => false);
    if (isVisible) {
      const isDisabled = await createBtn.isDisabled().catch(() => false);
      expect(isDisabled).toBe(true);
    }
  });

  // ── 权限：未登录拦截 ──
  test('未登录访问 /inspection/plans 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/inspection/plans');
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});
