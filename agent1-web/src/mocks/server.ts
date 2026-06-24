// ============================================================
// MSW Browser Worker — 在浏览器 Service Worker 线程中运行
//
// 使用方式（main.tsx）:
//   async function enableMocking() {
//     if (import.meta.env.VITE_ENABLE_MOCK !== 'true') return;
//     const { worker } = await import('./mocks/server');
//     return worker.start({ onUnhandledRequest: 'bypass' });
//   }
//   enableMocking().then(() => ReactDOM.createRoot(...).render(<App />));
// ============================================================

import { setupWorker } from 'msw/browser';
import { handlers } from './handlers';

export const worker = setupWorker(...handlers);
