<script setup lang="ts">
import { ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useAuthStore } from '@/stores/auth';

const router = useRouter();
const route = useRoute();
const auth = useAuthStore();
const username = ref('');
const password = ref('');
const loading = ref(false);
const errorMsg = ref('');

async function handleLogin() {
  errorMsg.value = '';
  if (!username.value.trim() || !password.value.trim()) { errorMsg.value = '请输入用户名和密码'; return; }
  loading.value = true;
  const ok = await auth.login({ username: username.value.trim(), password: password.value });
  loading.value = false;
  if (ok) router.push((route.query.redirect as string) || '/dashboard');
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-100 to-blue-50">
    <div class="w-full max-w-sm">
      <div class="text-center mb-8">
        <h1 class="text-2xl font-bold text-slate-900 tracking-tight">Agent1</h1>
        <p class="text-sm text-slate-500 mt-1">化工合规 AI 平台</p>
      </div>
      <div class="bg-white border border-slate-200 rounded-lg p-6">
        <h2 class="text-base font-semibold text-slate-800 mb-5">登录</h2>
        <form @submit.prevent="handleLogin" class="space-y-4">
          <div v-if="errorMsg" class="text-xs text-red-600 bg-red-50 border border-red-100 rounded px-3 py-2">{{ errorMsg }}</div>
          <div>
            <label class="text-xs text-slate-500 mb-1 block">用户名</label>
            <input v-model="username" type="text" class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="admin / auditor / viewer" autocomplete="username" />
          </div>
          <div>
            <label class="text-xs text-slate-500 mb-1 block">密码</label>
            <input v-model="password" type="password" class="w-full px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400" placeholder="任意密码" autocomplete="current-password" />
          </div>
          <button type="submit" :disabled="loading" class="w-full py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors">
            <span v-if="loading">登录中…</span><span v-else>登 录</span>
          </button>
        </form>
        <p class="text-xs text-slate-400 mt-4 text-center">用户名含 admin/auditor/viewer 切换角色</p>
      </div>
    </div>
  </div>
</template>
