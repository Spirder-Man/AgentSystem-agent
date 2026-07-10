<script setup lang="ts">
import { useAuthStore } from '@/stores/auth';
import { useRouter } from 'vue-router';

const auth = useAuthStore();
const router = useRouter();

function goToLogin() {
  auth.clearAuth();
  router.replace({ name: 'Login' });
}
</script>

<template>
  <div class="flex items-center justify-center min-h-screen bg-gray-50">
    <div class="text-center max-w-md px-6">
      <div class="text-6xl font-bold text-gray-300 mb-4">403</div>
      <h1 class="text-xl font-semibold text-gray-800 mb-2">访问被拒绝</h1>
      <p class="text-gray-500 mb-2">
        您的账户角色为 <span class="font-mono text-orange-600">{{ auth.role || '未知' }}</span>，
        当前无权访问此页面。
      </p>
      <p class="text-sm text-gray-400 mb-6">
        如需开通权限，请联系系统管理员将您的角色升级为 admin 或 auditor。
      </p>
      <el-button type="primary" @click="goToLogin">返回登录</el-button>
    </div>
  </div>
</template>
