<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { HealthStatus, AlertTestResult } from '@/types/api';
import { useAuthStore } from '@/stores/auth';
import { ElMessage } from 'element-plus';
import SkeletonCard from '@/components/common/SkeletonCard.vue';

const auth = useAuthStore();
const health = ref<HealthStatus | null>(null);
const loading = ref(true);

async function fetchHealth() {
  loading.value = true;
  try { const { data } = await apiClient.get<HealthStatus>('/health'); health.value = data; }
  catch { /* ignore */ }
  finally { loading.value = false; }
}

async function clearCache() {
  try {
    await apiClient.post('/cache/clear');
    ElMessage.success('缓存已清除');
  } catch { ElMessage.error('清除失败'); }
}

async function updateKB() {
  try {
    const { data } = await apiClient.post<{ message: string }>('/api/KnowledgeBase/incremental-load');
    ElMessage.success(data.message);
  } catch { ElMessage.error('更新失败'); }
}

const alertLoading = ref(false);
const alertResult = ref<AlertTestResult | null>(null);

async function sendTestAlert() {
  alertResult.value = null;
  alertLoading.value = true;
  try {
    const { data } = await apiClient.post<AlertTestResult>('/api/alerts/test', {
      title: 'Agent1 告警测试',
      message: '这是一条来自系统设置的测试告警邮件',
    });
    alertResult.value = data;
  } catch { alertResult.value = { sent: false, recipient: '' }; }
  finally { alertLoading.value = false; }
}

onMounted(fetchHealth);
</script>

<template>
  <div class="space-y-6 max-w-2xl">
    <h1 class="text-xl font-bold text-slate-900">系统设置</h1>

    <!-- 健康状态 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">运行状态</h3>
      <SkeletonCard v-if="loading" :count="1" />
      <div v-else-if="health" class="grid grid-cols-2 sm:grid-cols-3 gap-3 text-xs">
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">版本</span>
          <p class="text-slate-700 font-mono">{{ health.version }}</p>
        </div>
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">数据库</span>
          <p :class="health.checks.database === 'connected' ? 'text-green-600' : 'text-red-600'">{{ health.checks.database }}</p>
        </div>
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">LLM</span>
          <p :class="health.checks.ollama === 'reachable' ? 'text-green-600' : 'text-red-600'">{{ health.checks.ollama }}</p>
        </div>
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">知识库文档</span>
          <p class="text-slate-700">{{ health.checks.knowledge_base_docs }} 篇</p>
        </div>
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">LLM 调用</span>
          <p class="text-slate-700">{{ health.checks.llm_calls }} 次</p>
        </div>
        <div class="bg-slate-50 rounded p-2">
          <span class="text-slate-400">LLM 错误率</span>
          <p class="text-slate-700">{{ health.checks.llm_error_rate }}</p>
        </div>
      </div>
    </div>

    <!-- 运维操作 -->
    <div class="bg-white border border-slate-200 rounded p-4 space-y-3">
      <h3 class="text-sm font-semibold text-slate-700">运维操作</h3>
      <div class="flex flex-wrap gap-3">
        <button @click="clearCache" class="text-xs px-3 py-1.5 rounded border border-amber-200 text-amber-700 bg-amber-50 hover:bg-amber-100">
          🗑 清除查询缓存
        </button>
        <button @click="updateKB" class="text-xs px-3 py-1.5 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100">
          📚 增量更新知识库
        </button>
      </div>
    </div>

    <!-- 告警测试 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">告警测试</h3>
      <p class="text-xs text-slate-500 mb-3">发送测试告警邮件，验证告警通道是否正常</p>
      <button @click="sendTestAlert" :disabled="alertLoading" class="text-xs px-4 py-2 rounded border border-amber-200 text-amber-700 bg-amber-50 hover:bg-amber-100 disabled:opacity-50 transition-colors">
        {{ alertLoading ? '发送中…' : '✉️ 发送测试告警邮件' }}
      </button>
      <div v-if="alertResult" class="mt-3 text-xs p-2 rounded" :class="alertResult.sent ? 'bg-green-50 text-green-700 border border-green-200' : 'bg-red-50 text-red-700 border border-red-200'">
        {{ alertResult.sent ? '✅ 邮件已发送至 ' + alertResult.recipient : '❌ 发送失败' }}
      </div>
    </div>

    <!-- 用户信息 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <h3 class="text-sm font-semibold text-slate-700 mb-3">当前会话</h3>
      <div class="text-xs text-slate-500 space-y-1">
        <p>用户: <span class="text-slate-700">{{ auth.username }}</span></p>
        <p>角色: <span class="text-slate-700">{{ auth.role }}</span></p>
        <p>Token: <span class="font-mono text-slate-400 text-[10px]">{{ auth.token?.slice(0, 30) }}…</span></p>
      </div>
    </div>
  </div>
</template>
