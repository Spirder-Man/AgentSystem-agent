// ============================================================
// useGlobalLoading — 全局 Loading 遮罩管理
//
// 使用方式:
//   import { useGlobalLoading } from '@/composables/useGlobalLoading';
//   const { start, stop, loading } = useGlobalLoading();
//
//   或在顶层组件中通过 provide/inject 共享:
//   App.vue:
//     const globalLoading = useGlobalLoading();
//     provide('globalLoading', globalLoading);
//   任意子组件:
//     const { start, stop } = inject('globalLoading');
// ============================================================

import { ref } from 'vue';

const isGlobalLoading = ref(false);
const loadingMessage = ref('');

export function useGlobalLoading() {
  function start(message = '处理中，请稍候…') {
    loadingMessage.value = message;
    isGlobalLoading.value = true;
  }

  function stop() {
    isGlobalLoading.value = false;
    loadingMessage.value = '';
  }

  return {
    loading: isGlobalLoading,
    message: loadingMessage,
    start,
    stop,
  };
}
