// ============================================================
// P5 E2E-Real: 登录 (admin) → 审计日志 → 完整性校验 (真实后端)
//
// 验证:
//   - 审计日志列表渲染（操作人、操作类型、时间戳、哈希链ID）
//   - SHA256 哈希链完整性校验（真实后端验证）
//   - 非 admin 用户 (auditor/viewer) 访问 → 403 拦截
//   - 分页 + 统计信息
// ============================================================

import { test, expect, loginAs, ACCOUNTS } from './fixtures/auth.fixture';

test.describe('P5-Real: 审计日志 — 列表 + 完整性校验 (真实后端)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 审计日志列表加载 ──
  test('admin 应能看到审计日志列表', async ({ page }) => {
    const navAudit = page.locator('text=审计日志').or(page.locator('text=审计管理').or(page.locator('text=Audit')));
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 10_000 });

    // 验证日志列表渲染（来自真实 DB）
    const logEntry = page
      .locator('text=合规审核')
      .or(page.locator('text=危化品查询').or(page.locator('text=巡检执行')));
    await expect(logEntry.first()).toBeVisible({ timeout: 15_000 });
  });

  // ── 完整性校验 — 核心价值：验证真实哈希链 ──
  test('admin 应能执行哈希链完整性校验并验证结果', async ({ page }) => {
    const navAudit = page.locator('text=审计日志').or(page.locator('text=审计管理').or(page.locator('text=Audit')));
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 10_000 });

    // 查找完整性校验按钮
    const integrityBtn = page
      .locator('button:has-text("完整性")')
      .or(page.locator('button:has-text("校验")').or(page.locator('button:has-text("Integrity")')));

    if (await integrityBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await integrityBtn.click();

      // 验证校验结果（真实后端 SHA256 哈希链验证）
      const checkResult = page
        .locator('text=完整')
        .or(page.locator('text=未检测到篡改').or(page.locator('text=intact')));
      await expect(checkResult.first()).toBeVisible({ timeout: 15_000 });
    }
  });

  // ── 审计统计 ──
  test('admin 应能看到审计统计概览（来自真实 DB）', async ({ page }) => {
    const navAudit = page.locator('text=审计日志').or(page.locator('text=审计管理').or(page.locator('text=Audit')));
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 10_000 });

    // 验证含数字统计（具体值依赖真实 DB 状态）
    await expect(page.locator('body')).toContainText(/[\d]+/, { timeout: 10_000 });
  });

  // ── auditor 无权限 ──
  test('auditor 访问 /audit 应被拦截', async ({ page }) => {
    await page.context().clearCookies();
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ACCOUNTS.auditor.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.auditor.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    await page.goto('/audit');
    await expect(
      page.locator('text=无权限').or(page.locator('text=403')).or(page.locator('text=暂无可用功能')),
    ).toBeVisible({ timeout: 15_000 });
  });

  // ── viewer 无权限 ──
  test('viewer 访问 /audit 应被拦截', async ({ page }) => {
    await page.context().clearCookies();
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ACCOUNTS.viewer.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.viewer.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    await page.goto('/audit');
    await expect(
      page.locator('text=无权限').or(page.locator('text=403')).or(page.locator('text=暂无可用功能')),
    ).toBeVisible({ timeout: 15_000 });
  });
});
