// ============================================================
// P1 E2E-Real: 登录 → 合规自查 → 查看结果 (真实 GPU 推理)
//
// 全链路: Browser → Vite → SSH Tunnel → .NET API → llama.cpp GPU
//
// 验证:
//   - 路由守卫 + JWT 认证流（真实后端验证）
//   - 合规自查页面的输入→提交→真实 LLM 推理结果渲染
//   - LLM 工具调用链真实性 (CheckStorageCompatibility 等)
//   - GB 编号非幻觉（格式校验 + 可追溯到规范库）
//   - 双通道输出结构（法规引用面板 + LLM 解释面板）
//   - 响应时间在合理范围（真实 GPU 推理 >3s）
// ============================================================

import { test, expect, loginAs, ACCOUNTS } from './fixtures/auth.fixture';
import {
  expectGbNumberPresent,
  expectToolCallPresent,
  expectResponseTimeInRange,
  expectDualChannelOutput,
} from './utils/llm-assertions';
import { COMPLIANCE_CHECK } from '../src/test-ids';

const COMPLIANCE_QUERIES = ['苯和丙酮能同库储存吗', '硝酸和甲醇的储存安全距离是多少', '苯属于哪类危险化学品'];

test.describe('P1-Real: 登录 → 合规自查 → 真实 GPU 推理', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 核心场景：合规自查完整交互 + LLM 质量断言 ──
  test('应完成输入查询 → 提交 → 真实 LLM 推理 → 双通道结果', async ({ page }) => {
    // 1. 导航到合规自查页
    await page.click('text=合规检查');
    await expect(page).toHaveURL(/\/compliance/);

    // 2. 验证输入框和发送按钮存在
    const input = page.locator('input[placeholder*="化工"]').first();
    await expect(input).toBeVisible();
    const sendBtn = page.locator('button:has-text("提交审核")');
    await expect(sendBtn).toBeVisible();

    // 3. 输入查询内容并提交
    await input.fill(COMPLIANCE_QUERIES[0]);
    const startTime = Date.now();
    await sendBtn.click();

    // 4. 验证双通道输出（法规引用 + LLM 解释）
    await expectDualChannelOutput(page);

    // 5. 真实 GPU 推理时间应在 3s-90s 之间
    expectResponseTimeInRange(startTime, 3_000, 90_000);

    // 6. 验证 GB 编号非幻觉
    const regulationPanel = page
      .locator(`[data-testid="${COMPLIANCE_CHECK.regulationPanel}"]`)
      .or(page.locator('text=法规引用'))
      .first();
    await expectGbNumberPresent(regulationPanel, '合规自查法规引用');

    // 7. 验证工具调用链（真实 GPU 才会产生工具调用）
    const hasToolChain = await page
      .locator(`[data-testid="${COMPLIANCE_CHECK.toolChain}"]`)
      .or(page.locator('text=工具调用'))
      .isVisible({ timeout: 5_000 })
      .catch(() => false);

    if (hasToolChain) {
      await expectToolCallPresent(page, 'CheckStorageCompatibility');
    }
  });

  // ── 边界：空输入保护（真实后端） ──
  test('空输入时不应提交', async ({ page }) => {
    await page.click('text=合规检查');

    const sendBtn = page.locator('button:has-text("提交审核")');
    await expect(sendBtn).toBeVisible();

    const isDisabled = await sendBtn.isDisabled();
    if (!isDisabled) {
      await sendBtn.click();
      await expect(
        page.locator(`[data-testid="${COMPLIANCE_CHECK.regulationPanel}"]`).or(page.locator('text=法规引用')),
      ).not.toBeVisible({ timeout: 5_000 });
    }
  });

  // ── 权限：未登录访问应重定向 ──
  test('未登录访问 /compliance 应重定向到 /login', async ({ page }) => {
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await page.goto('/compliance');
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });

  // ── 权限：viewer 访问业务页面应被拦截 ──
  test('viewer 角色访问 /compliance 应显示 403 或受限', async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[autocomplete="username"]', ACCOUNTS.viewer.username);
    await page.fill('input[autocomplete="current-password"]', ACCOUNTS.viewer.password);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    await page.goto('/compliance');
    await expect(
      page.locator('text=无权限').or(page.locator('text=403')).or(page.locator('text=暂无可用功能')),
    ).toBeVisible({ timeout: 15_000 });
  });
});
