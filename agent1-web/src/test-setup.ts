// 前端测试全局 setup
// — 初始化 Mock Service Worker 的 server 模式（用于 vitest/jsdom）
// — 排除 element-plus 的 resize 警告

import { beforeAll, afterAll, afterEach } from 'vitest';

// 抑制 Element Plus 在 jsdom 环境中的 CSS 变量警告
const originalWarn = console.warn;
console.warn = (...args: unknown[]) => {
  const msg = String(args[0]);
  if (msg.includes('CSS') || msg.includes('style')) return;
  originalWarn(...args);
};

// Mock window.matchMedia (Element Plus 响应式需要)
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
});

// Mock IntersectionObserver
class MockIntersectionObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
Object.defineProperty(window, 'IntersectionObserver', {
  writable: true,
  value: MockIntersectionObserver,
});

// Mock ResizeObserver (Element Plus 需要)
class MockResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
Object.defineProperty(window, 'ResizeObserver', {
  writable: true,
  value: MockResizeObserver,
});
