import { createApp } from 'vue';
import { createPinia } from 'pinia';
import ElementPlus from 'element-plus';
import zhCn from 'element-plus/es/locale/lang/zh-cn';
import 'element-plus/dist/index.css';
import './assets/main.css';

import App from './App.vue';
import { router } from './router';
import { configureAuth, configureErrorHandler } from './lib/axios';
import { useAuthStore } from './stores/auth';
import { vPermission } from './directives/permission';

async function bootstrap() {
  // VITE_ENABLE_MOCK=true 由 .env.mock 文件控制，仅 npm run dev:mock 加载
  if (import.meta.env.VITE_ENABLE_MOCK === 'true') {
    try {
      const { worker } = await import('./mocks/server');
      await worker.start({
        onUnhandledRequest: 'bypass',
        serviceWorker: { url: '/mockServiceWorker.js' },
      });
      console.log('[MSW] Mock Service Worker 已启动');
    } catch (e) {
      console.error('[MSW] 启动失败:', e);
    }
  }

  const app = createApp(App);
  const pinia = createPinia();
  app.use(pinia);
  app.use(router);
  app.use(ElementPlus, { locale: zhCn });
  app.directive('permission', vPermission);

  const auth = useAuthStore();

  configureAuth({
    getToken: () => auth.token,
    getRefreshToken: () => auth.refreshToken,
    onTokenRefreshed: (token, refreshToken) => {
      auth.token = token;
      auth.refreshToken = refreshToken;
    },
    onLogout: () => auth.clearAuth(),
  });

  configureErrorHandler({
    onError: (code, msg) => console.warn(`[${code}] ${msg}`),
    onForbidden: () => router.push({ name: 'Forbidden' }),
  });

  auth.restoreAuth();
  app.mount('#app');
}

bootstrap();
