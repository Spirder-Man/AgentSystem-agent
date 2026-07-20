<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue';
import apiClient from '@/lib/axios';
import type { HealthStatus, AuditStatsResponse } from '@/types/api';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const health = ref<HealthStatus | null>(null);
const auditStats = ref<AuditStatsResponse | null>(null);
const loading = ref(true);
const error = ref('');
let timer: ReturnType<typeof setInterval> | null = null;

const statusBadge = computed(() => {
  if (!health.value) return { cls: 'text-slate-500 bg-slate-50', label: '未知' };
  return health.value.status === 'healthy'
    ? { cls: 'text-green-700 bg-green-50 border-green-200', label: '健康' }
    : { cls: 'text-amber-700 bg-amber-50 border-amber-200', label: '降级' };
});

const dbBadge = computed(() => {
  if (!health.value) return { ok: false, label: '未知' };
  return health.value.checks.database === 'connected'
    ? { ok: true, label: '已连接' }
    : { ok: false, label: '断开' };
});

const ollamaBadge = computed(() => {
  if (!health.value) return { ok: false, label: '未知' };
  return health.value.checks.ollama === 'reachable'
    ? { ok: true, label: '可达' }
    : { ok: false, label: '不可达' };
});

async function fetchAll() {
  try {
    const [hRes, aRes] = await Promise.all([
      apiClient.get<HealthStatus>('/health'),
      apiClient.get<AuditStatsResponse>('/api/Audit/stats'),
    ]);
    health.value = hRes.data;
    auditStats.value = aRes.data;
    error.value = '';
  } catch {
    error.value = '监控数据加载失败';
  } finally { loading.value = false; }
}

const topOperations = computed(() => {
  if (!auditStats.value?.byOperation) return [];
  return Object.entries(auditStats.value.byOperation)
    .sort(([, a], [, b]) => b - a)
    .slice(0, 5);
});

const topUsers = computed(() => {
  if (!auditStats.value?.byUser) return [];
  return Object.entries(auditStats.value.byUser)
    .sort(([, a], [, b]) => b - a)
    .slice(0, 5);
});

onMounted(() => { fetchAll(); timer = setInterval(fetchAll, 30_000); });
onUnmounted(() => { if (timer) clearInterval(timer); });
</script>

<template>
  <div class="space-y-6 max-w-5xl">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-bold text-slate-900">系统运维看板</h1>
        <p class="text-xs text-slate-500 mt-1">实时监控系统健康状态、模型连接与审计活动 · 每30秒自动刷新</p>
      </div>
      <span v-if="health" class="text-xs px-2.5 py-1 rounded border" :class="statusBadge.cls">{{ statusBadge.label }}</span>
    </div>

    <SkeletonCard v-if="loading" :count="4" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchAll" />

    <template v-else-if="health">
      <!-- 健康概览卡片 -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div class="bg-white border border-slate-200 rounded p-4">
          <p class="text-xs text-slate-500 mb-1">系统版本</p>
          <p class="text-lg font-bold text-slate-800">{{ health.version }}</p>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4">
          <p class="text-xs text-slate-500 mb-1">数据库</p>
          <div class="flex items-center gap-2">
            <span class="w-2.5 h-2.5 rounded-full" :class="dbBadge.ok ? 'bg-green-500' : 'bg-red-500'" />
            <span class="text-lg font-bold" :class="dbBadge.ok ? 'text-green-700' : 'text-red-700'">{{ dbBadge.label }}</span>
          </div>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4">
          <p class="text-xs text-slate-500 mb-1">Ollama 模型</p>
          <div class="flex items-center gap-2">
            <span class="w-2.5 h-2.5 rounded-full" :class="ollamaBadge.ok ? 'bg-green-500' : 'bg-red-500'" />
            <span class="text-lg font-bold" :class="ollamaBadge.ok ? 'text-green-700' : 'text-red-700'">{{ ollamaBadge.label }}</span>
          </div>
        </div>
        <div class="bg-white border border-slate-200 rounded p-4">
          <p class="text-xs text-slate-500 mb-1">知识库文档</p>
          <p class="text-lg font-bold text-blue-700">{{ health.checks.knowledge_base_docs }} 篇</p>
        </div>
      </div>

      <!-- LLM 指标 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4">🤖 LLM 推理指标</h2>
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div class="bg-slate-50 border border-slate-100 rounded p-3 text-center">
            <p class="text-xs text-slate-500 mb-1">累计调用次数</p>
            <p class="text-2xl font-bold text-blue-700">{{ health.checks.llm_calls }}</p>
          </div>
          <div class="bg-slate-50 border border-slate-100 rounded p-3 text-center">
            <p class="text-xs text-slate-500 mb-1">错误率</p>
            <p class="text-2xl font-bold" :class="parseFloat(health.checks.llm_error_rate) > 5 ? 'text-red-600' : 'text-green-600'">
              {{ health.checks.llm_error_rate }}
            </p>
          </div>
          <div class="bg-slate-50 border border-slate-100 rounded p-3 text-center">
            <p class="text-xs text-slate-500 mb-1">最后更新</p>
            <p class="text-sm font-mono text-slate-600">{{ new Date(health.timestamp).toLocaleString('zh-CN') }}</p>
          </div>
        </div>
      </div>

      <!-- 审计统计 -->
      <div v-if="auditStats" class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div class="bg-white border border-slate-200 rounded p-5">
          <h2 class="text-sm font-semibold text-slate-700 mb-4">📊 操作分布 (Top 5)</h2>
          <div class="space-y-2">
            <div v-for="[op, count] in topOperations" :key="op" class="flex items-center gap-3">
              <span class="text-xs text-slate-500 w-28 truncate">{{ op }}</span>
              <div class="flex-1 h-5 bg-slate-100 rounded overflow-hidden">
                <div class="h-full bg-blue-500 rounded" :style="{ width: `${(count / (topOperations[0]?.[1] || 1)) * 100}%` }" />
              </div>
              <span class="text-xs font-mono text-slate-600 w-8 text-right">{{ count }}</span>
            </div>
          </div>
          <p class="text-xs text-slate-400 mt-4">总操作数: {{ auditStats.totalCount }}</p>
        </div>

        <div class="bg-white border border-slate-200 rounded p-5">
          <h2 class="text-sm font-semibold text-slate-700 mb-4">👤 用户活跃度 (Top 5)</h2>
          <div class="space-y-2">
            <div v-for="[user, count] in topUsers" :key="user" class="flex items-center gap-3">
              <span class="text-xs text-slate-500 w-20 truncate">{{ user }}</span>
              <div class="flex-1 h-5 bg-slate-100 rounded overflow-hidden">
                <div class="h-full bg-emerald-500 rounded" :style="{ width: `${(count / (topUsers[0]?.[1] || 1)) * 100}%` }" />
              </div>
              <span class="text-xs font-mono text-slate-600 w-8 text-right">{{ count }}</span>
            </div>
          </div>
          <p v-if="auditStats.lastLogAt" class="text-xs text-slate-400 mt-4">最近操作: {{ new Date(auditStats.lastLogAt).toLocaleString('zh-CN') }}</p>
        </div>
      </div>
    </template>
  </div>
</template>
