// ============================================================
// auth.fixture.ts — 真实 GPU E2E 认证 fixture
//
// 提供:
//   - authenticatedPage(role): 自动登录指定角色的 page fixture
// ============================================================

import { test as base, expect, type Page } from '@playwright/test';

// ── 测试账号（对齐远程 .env 中 AUTH_ACCOUNTS_JSON） ──
export const ACCOUNTS = {
  admin: { username: 'admin', password: '7758521' },
  auditor: { username: 'auditor', password: '7758521' },
  viewer: { username: 'viewer', password: 'viewer123' },
} as const;

export type Role = keyof typeof ACCOUNTS;

// ── 扩展 fixture 类型 ──
type AuthFixtures = {
  authenticatedPage: (role?: Role) => Promise<Page>;
};

/**
 * 登录辅助函数 — 填写凭据并等待跳转到仪表盘
 */
export async function loginAs(page: Page, role: Role = 'admin') {
  const account = ACCOUNTS[role];
  await page.goto('/login');
  await expect(page).toHaveURL(/\/login/);

  await page.fill('input[autocomplete="username"]', account.username);
  await page.fill('input[autocomplete="current-password"]', account.password);
  await page.click('button[type="submit"]');

  // 等待跳转（真实后端 JWT 验证需要更多时间）
  await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

  // viewer 角色在仪表盘可能看到受限视图
  if (role === 'admin' || role === 'auditor') {
    await expect(page.locator('h1:has-text("仪表盘")')).toBeVisible({ timeout: 10_000 });
  }
}

/**
 * 扩展 test fixture，提供快捷登录能力
 */
export const test = base.extend<AuthFixtures>({
  // ── authenticatedPage: 返回已登录的 page ──
  authenticatedPage: async ({ page }, use) => {
    const factory = async (role: Role = 'admin') => {
      await loginAs(page, role);
      return page;
    };
    await use(factory);
  },
});

export { expect };
