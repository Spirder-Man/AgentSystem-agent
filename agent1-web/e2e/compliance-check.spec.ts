// ============================================================
// P1 E2E: 登录 → 合规自查 → 查看结果
//
// 这是 Agent1 最核心的业务闭环，验证：
//   - 路由守卫 + JWT 认证流
//   - 合规自查页面的输入→提交→结果渲染完整交互
//   - 双通道输出（法规引用面板 + LLM 解释面板）
//   - 工具调用链展示
//
// 设计原则（对齐 Pre/Post 分层）：
//   - Pre-deploy: 此 E2E 使用 MSW Mock，验证 UI 正确性
//   - 不属于 Post-deploy 的 LLM 推理质量验证（那是远程评测的职责）
// ============================================================

import { test, expect } from '@playwright/test';
import { COMPLIANCE_CHECK } from '../src/test-ids';

// ═══════════════════════════════════════
// 测试数据
// ═══════════════════════════════════════
const ADMIN = { username: 'admin', password: 'admin123' };
const COMPLIANCE_QUERY = '苯和丙酮能同库储存吗';

// ═══════════════════════════════════════
// Step 1: 登录
// ═══════════════════════════════════════
test.describe('P1: 登录 → 合规自查 → 查看结果', () => {
  test.beforeEach(async ({ page }) => {
    // 进入登录页
    await page.goto('/login');
    await expect(page).toHaveURL(/\/login/);

    // 填写凭据
    await page.fill('input[autocomplete="username"]', ADMIN.username);
    await page.fill('input[autocomplete="current-password"]', ADMIN.password);

    // 点击登录
    await page.click('button[type="submit"]');

    // 验证跳转到仪表盘
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 });
    await expect(page.locator('text=合规仪表盘')).toBeVisible({ timeout: 5_000 });
  });

  // ── 主流程：合规自查完整交互 ──
  test('应完成输入查询 → 提交 → 查看双通道结果', async ({ page }) => {
    // 1. 导航到合规自查页
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    // 2. 验证输入框和发送按钮存在
    const input = page.locator('input[placeholder*="化工"]').first();
    await expect(input).toBeVisible();
    const sendBtn = page.locator('button:has-text("提交审核")');
    await expect(sendBtn).toBeVisible();

    // 3. 输入查询内容
    await input.fill(COMPLIANCE_QUERY);
    await sendBtn.click();

    // 4. 等待结果渲染（最长 25s，MSW Mock 通常 <3s）
    //    双通道输出：法规引用面板 + LLM 解释面板
    await expect(page.getByTestId(COMPLIANCE_CHECK.resultPanel)).toBeVisible({ timeout: 25_000 });
    const llmPanel = page.getByTestId(COMPLIANCE_CHECK.llmPanel);
    await expect(llmPanel).toBeVisible({ timeout: 5_000 });

    // 5. 切换到法规引用 Tab 并验证内容
    await page.getByTestId(COMPLIANCE_CHECK.regulationTab).click();
    const regulationPanel = page.getByTestId(COMPLIANCE_CHECK.regulationPanel);
    await expect(regulationPanel).toBeVisible({ timeout: 10_000 });
    await expect(regulationPanel).toContainText('GB', { timeout: 3_000 });

    // 6. 验证工具调用链展示（CheckStorageCompatibility 被调用）
    const toolChain = page.getByTestId(COMPLIANCE_CHECK.toolChain);
    if (await toolChain.isVisible()) {
      await expect(toolChain).toContainText('CheckStorageCompatibility');
    }
  });

  // ── 边界：空输入保护 ──
  test('空输入时不应提交', async ({ page }) => {
    await page.click('text=合规检查');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await expect(sendBtn).toBeVisible();

    // 不输入内容，检查按钮是否 disabled 或点击后不出结果
    const isDisabled = await sendBtn.isDisabled();
    if (!isDisabled) {
      await sendBtn.click();
      // 不应出现结果面板
      await expect(page.getByTestId(COMPLIANCE_CHECK.regulationPanel)).not.toBeVisible({ timeout: 3_000 });
    }
  });

  // ── 权限：未登录访问业务页面应重定向 ──
  test('未登录访问 /compliance 应重定向到 /login', async ({ page }) => {
    // 清除 cookies/storage 模拟未登录
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/compliance');
    // 应被路由守卫拦截
    await expect(page).toHaveURL(/\/login/, { timeout: 5_000 });
  });

  // ── 权限：viewer 访问业务页面应被拦截 ──
  test('viewer 角色访问 /compliance 应显示 403', async ({ page }) => {
    // viewer 角色登录
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', 'viewer');
    await page.fill('input[autocomplete="current-password"]', 'viewer123');
    await page.click('button[type="submit"]');

    // viewer 登陆后访问业务页面
    await page.goto('/compliance');
    // 应被重定向到 403 或留在登录页
    await expect(
      page.locator('text=无权限').or(page.locator('text=403')).or(page.locator('text=暂无可用功能')),
    ).toBeVisible({ timeout: 10_000 });
  });
});
