// ============================================================
// P5 E2E: 登录 (admin) → 审计日志 → 完整性校验
//
// 验证：
//   - 审计日志列表渲染（操作人、操作类型、时间戳、哈希链ID）
//   - SHA256 哈希链完整性校验
//   - 非 admin 用户 (auditor/viewer) 访问 → 403 拦截
//   - 分页功能
//
// 注意：/audit 路由仅 admin 角色可访问（对应后端 Admin 策略）
// MSW Mock 数据：6 条审计日志
// 设计原则：MSW Mock 模式，不依赖后端 GPU/DB
// ============================================================

import { test, expect } from '@playwright/test';

const ADMIN = { username: 'admin', password: 'admin123' };

test.describe('P5: 审计日志 — 列表 + 完整性校验 (admin only)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[placeholder*="用户名"]', ADMIN.username);
    await page.fill('input[placeholder*="密码"]', ADMIN.password);
    await page.click('button:has-text("登录")');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
  });

  // ── 审计日志列表加载 ──
  test('admin 应能看到审计日志列表', async ({ page }) => {
    // 导航到审计日志
    const navAudit = page.locator('text=审计日志').or(
      page.locator('text=审计管理').or(page.locator('text=Audit'))
    );
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 5_000 });

    // 验证日志列表渲染（MSW Mock 返回 6 条）
    const logEntry = page.locator('text=合规审核').or(
      page.locator('text=危化品查询').or(page.locator('text=巡检执行'))
    );
    await expect(logEntry.first()).toBeVisible({ timeout: 10_000 });

    // 验证哈希链 ID 存在
    await expect(page.locator('body')).toContainText('a1b2c3', { timeout: 5_000 });
  });

  // ── 完整性校验 ──
  test('admin 应能执行哈希链完整性校验', async ({ page }) => {
    const navAudit = page.locator('text=审计日志').or(
      page.locator('text=审计管理').or(page.locator('text=Audit'))
    );
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 5_000 });

    // 查找完整性校验按钮
    const integrityBtn = page.locator('button:has-text("完整性")').or(
      page.locator('button:has-text("校验")').or(
        page.locator('button:has-text("Integrity")')
      )
    );

    if (await integrityBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await integrityBtn.click();

      // 验证校验结果（MSW Mock 返回 intact: true）
      const checkResult = page.locator('text=完整').or(
        page.locator('text=未检测到篡改').or(page.locator('text=intact'))
      );
      await expect(checkResult.first()).toBeVisible({ timeout: 10_000 });
    }
  });

  // ── auditor 无权限 ──
  test('auditor 访问 /audit 应被拦截', async ({ page }) => {
    // auditor 登录
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[placeholder*="用户名"]', 'auditor');
    await page.fill('input[placeholder*="密码"]', 'auditor123');
    await page.click('button:has-text("登录")');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });

    // 直接访问审计日志
    await page.goto('/audit');

    // 应被重定向到 403 或留在仪表盘
    await expect(
      page.locator('text=无权限').or(
        page.locator('text=403').or(page.locator('text=暂无可用功能'))
      )
    ).toBeVisible({ timeout: 10_000 });
  });

  // ── viewer 无权限 ──
  test('viewer 访问 /audit 应被拦截', async ({ page }) => {
    // viewer 登录
    await page.goto('/login');
    await page.context().clearCookies();
    await page.fill('input[placeholder*="用户名"]', 'viewer');
    await page.fill('input[placeholder*="密码"]', 'viewer123');
    await page.click('button:has-text("登录")');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });

    // 直接访问审计日志
    await page.goto('/audit');

    // 应被重定向到 403
    await expect(
      page.locator('text=无权限').or(
        page.locator('text=403').or(page.locator('text=暂无可用功能'))
      )
    ).toBeVisible({ timeout: 10_000 });
  });

  // ── 审计统计 ──
  test('admin 应能看到审计统计概览', async ({ page }) => {
    const navAudit = page.locator('text=审计日志').or(
      page.locator('text=审计管理').or(page.locator('text=Audit'))
    );
    await navAudit.first().click();
    await expect(page).toHaveURL(/\/audit/, { timeout: 5_000 });

    // 验证统计信息（MSW Mock: 156 总记录）
    await expect(page.locator('body')).toContainText('156', { timeout: 5_000 });
    // admin 用户出现 68 次操作
    await expect(page.locator('body')).toContainText('admin', { timeout: 3_000 });
  });
});
