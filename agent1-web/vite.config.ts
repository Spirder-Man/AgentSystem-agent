import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
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
        target: 'http://localhost:15001',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://localhost:15001',
        changeOrigin: true,
      },
      '/metrics': {
        target: 'http://localhost:15001',
        changeOrigin: true,
      },
      '/cache': {
        target: 'http://localhost:15001',
        changeOrigin: true,
      },
      '/knowledgebase': {
        target: 'http://localhost:15001',
        changeOrigin: true,
      },
      '/memory': {
        target: 'http://localhost:15001',
        changeOrigin: true,
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
});
