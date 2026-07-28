// ============================================================
// Playwright E2E 配置 — Agent1 前端测试
//
// 设计原则（对齐测试分层策略）：
//   - 环境解耦：E2E 使用 MSW Mock，不依赖后端 GPU/DB
//   - CI 集成：chromium 单浏览器，失败截图+trace
//   - 职责分离：E2E 只验证 UI 正确性，不验证 LLM 推理质量
// ============================================================

import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000, // LLM 推理场景下 E2E 超时设高
  expect: { timeout: 15_000 },
  retries: process.env.CI ? 2 : 1,
  workers: process.env.CI ? 1 : 2,

  reporter: [
    ['html', { outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'playwright-report/results.json' }],
  ],

  use: {
    baseURL: 'http://localhost:5173',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
    // 使用 MSW Mock 模式避免依赖后端
    extraHTTPHeaders: {
      'X-Mock-Enabled': 'true',
    },
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // CI 中启动 Vite dev server (MSW Mock 模式)
  webServer: process.env.CI
    ? {
        command: 'npm run dev:mock',
        port: 5173,
        reuseExistingServer: false,
        timeout: 30_000,
      }
    : undefined,
});
