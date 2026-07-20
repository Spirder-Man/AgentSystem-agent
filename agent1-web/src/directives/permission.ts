// ============================================================
// v-permission — 全局角色权限指令
//
// 用法:
//   <button v-permission="['admin']">管理员按钮</button>
//   <button v-permission="['admin','auditor']">业务按钮</button>
//
// 无权限的元素从 DOM 中移除 (非 display:none)
// ============================================================

import type { Directive } from 'vue';
import type { UserRole } from '@/types/api';
import { useAuthStore } from '@/stores/auth';

export const vPermission: Directive<HTMLElement, UserRole[]> = {
  mounted(el, binding) {
    const auth = useAuthStore();
    if (!auth.hasPermission(binding.value)) {
      el.parentNode?.removeChild(el);
    }
  },
};
