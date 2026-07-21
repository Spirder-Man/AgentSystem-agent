import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  // VITE_PROXY_TARGET 默认 localhost:15000，可通过 .env 覆盖为 SSH 隧道端口
  const proxyTarget = env.VITE_PROXY_TARGET || 'http://localhost:15000';

  return {
    plugins: [vue()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      proxy: {
        // 当 Mock 关闭时，将 /api 请求转发到后端
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
    },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          'element-plus': ['element-plus'],
          'echarts': ['echarts', 'vue-echarts'],
        },
      },
    },
  },
  };
});
