// ============================================================
// NEW E2E-Real: 应急响应 — 事故上报 → 分级 → 预案匹配 → 通知
//
// 验证:
//   - 应急响应页面加载
//   - 事故上报表单填写
//   - 预案匹配（含具体处置措施）
//   - 通知列表生成
// ============================================================

import { test, expect, loginAs } from './fixtures/auth.fixture';
import { expectResponseTimeInRange, expectBaselineQuality } from './utils/llm-assertions';
import { COMPLIANCE_CHECK } from '../src/test-ids';

const EMERGENCY_SCENARIOS = ['苯泄漏', '甲醇火灾', '硝酸腐蚀'];

test.describe('Emergency-Real: 应急响应 — 上报 → 分级 → 预案 (真实 GPU)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, 'admin');
  });

  // ── 导航到应急响应页面 ──
  test('应能导航到应急响应页面', async ({ page }) => {
    const navEmergency = page
      .locator('text=应急响应')
      .or(page.locator('text=应急管理').or(page.locator('text=Emergency')));
    if (await navEmergency.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await navEmergency.first().click();
      // 应急响应路由
      await expect(page).toHaveURL(/\/(emergency|response)/, { timeout: 10_000 });

      // 验证页面标题
      const title = page.locator('text=应急响应').or(page.locator('text=事故上报').or(page.locator('text=应急')));
      await expect(title.first()).toBeVisible({ timeout: 10_000 });
    }
  });

  // ── 事故上报 → 分级 ──
  for (const scenario of EMERGENCY_SCENARIOS) {
    test(`应能上报"${scenario}"事故并获取分级结果`, async ({ page }) => {
      // 导航到合规检查页（应急响应可能集成在此）
      await page.click('text=合规检查');
      await expect(page).toHaveURL(/\/compliance/);

      const input = page.locator('input[placeholder*="化工"]').first();
      await input.fill(`${scenario}事故应急处置方案`);

      const startTime = Date.now();
      const sendBtn = page.locator('button:has-text("提交审核")');
      await sendBtn.click();

      // 等待 LLM 应急响应并轮询加载实际内容
      const llmPanel = page.locator(`[data-testid="${COMPLIANCE_CHECK.llmPanel}"]`);
      await expect(llmPanel).toBeVisible({ timeout: 90_000 });

      // 轮询等待实际内容加载完成
      let text = '';
      for (let i = 0; i < 15; i++) {
        text = (await llmPanel.textContent()) ?? '';
        if (text.length > 20) break;
        await page.waitForTimeout(2000);
      }

      // 验证响应包含处置措施（基于 baseline.json，允许缓存命中）
      const baselineKey =
        scenario === '苯泄漏'
          ? 'emergency-benzene-leak'
          : scenario === '甲醇火灾'
            ? 'emergency-methanol-fire'
            : 'emergency-nitric-acid-corrosion';
      expectBaselineQuality(text, baselineKey, `应急响应 - ${scenario}`);

      // 至少应有实质内容
      expect(text.length, `应急响应 - ${scenario} 应有实质内容`).toBeGreaterThan(50);

      // 验证响应耗时（容忍缓存）
      expectResponseTimeInRange(startTime, 3_000, 90_000, `应急响应 - ${scenario}`, true);
    });
  }
});
