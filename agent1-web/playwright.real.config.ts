// ============================================================
// Playwright Real GPU E2E 配置 — Agent1 真实后端全链路测试
//
// 全链路: Browser → Vite → SSH Tunnel → .NET API → llama.cpp GPU
//
// 设计原则（对齐双 E2E 分层）:
//   - 主力测试层: 验证 LLM 推理质量 + 工具调用 + 数据一致性
//   - 全局 SSH 隧道: 隧道外部管理（npm run tunnel:start），Playwright 只管 Vite
//   - CI 不跑: 此配置仅本地/手动执行，CI 使用 playwright.config.ts (MSW Mock)
// ============================================================

import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e-real',
  timeout: 120_000,               // LLM 真实推理最大等待 2 分钟
  expect: { timeout: 30_000 },    // 单个断言等待最长 30s
  retries: 1,                     // LLM 推理偶有波动，重试 1 次
  workers: 1,                     // 串行执行，避免 GPU 并发过载
  forbidOnly: false,

  reporter: [
    ['html', { outputFolder: 'playwright-report-real' }],
    ['json', { outputFile: 'playwright-report-real/results.json' }],
    ['list'],
  ],

  use: {
    // Vite dev server 地址（代理到 SSH 隧道 :15001 → 远程 API :5000）
    baseURL: 'http://localhost:5173',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
    // 不设置 extraHTTPHeaders，请求直通 Vite 代理 → SSH 隧道 → 真实后端
  },

  projects: [
    {
      name: 'chromium-real',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // 启动 Vite 开发服务器（真实后端模式，MSW 关闭）
  webServer: {
    command: 'npx vite --host 0.0.0.0 --port 5173',
    port: 5173,
    reuseExistingServer: true,    // 允许复用已启动的 Vite（隧道已就绪）
    timeout: 30_000,
    env: {
      VITE_ENABLE_MOCK: 'false',       // 关闭 MSW Mock
      VITE_PROXY_TARGET: 'http://localhost:15001', // 指向 SSH 隧道
    },
  },
});
