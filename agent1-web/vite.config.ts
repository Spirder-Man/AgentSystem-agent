import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const isMock = mode === 'mock';

  // Mock 模式下不配置代理，MSW 在浏览器端全拦截
  // 非 Mock 模式：VITE_PROXY_TARGET 默认 127.0.0.1:15000（SSH 隧道）
  const proxyTarget = env.VITE_PROXY_TARGET || 'http://127.0.0.1:15000';

  return {
    plugins: [vue()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      ...(isMock
        ? {}
        : {
            proxy: {
              // 将 /api 请求转发到真实后端
              '/api': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
              '/health': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
              '/metrics': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
              '/cache': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
              '/knowledgebase': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
              '/memory': {
                target: proxyTarget,
                changeOrigin: true,
                secure: false,
              },
            },
          }),
    },
    build: {
      rollupOptions: {
        output: {
          manualChunks: {
            'element-plus': ['element-plus'],
            echarts: ['echarts', 'vue-echarts'],
          },
        },
      },
    },
  };
});
