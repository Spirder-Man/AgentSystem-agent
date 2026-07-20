import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'path';

export default defineConfig({
  plugins: [vue()],
  test: {
    // jsdom 环境模拟浏览器 DOM
    environment: 'jsdom',
    // 全局 setup（如 msw server 等）
    setupFiles: ['./src/test-setup.ts'],
    // 排除 node_modules 和 e2e 目录
    exclude: ['node_modules', 'e2e'],
    // 覆盖率配置
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary', 'html'],
      reportsDirectory: './coverage',
      exclude: [
        'node_modules/',
        'src/env.d.ts',
        'vitest.config.ts',
        'src/test-setup.ts',
      ],
    },
  },
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
});
