<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { TicketListResponse, TicketItem } from '@/types/api';
import {
  TICKET_ACTIONS_BY_STATUS,
  TICKET_STATUS_LABEL_MAP,
  TICKET_STATUS_COLOR_MAP,
  TICKET_PRIORITY_COLOR_MAP,
  ACTION_TO_STATUS,
  isTerminalStatus,
} from '@/utils/constants';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';
import { ElMessage, ElMessageBox } from 'element-plus';

const route = useRoute();
const router = useRouter();
const ticket = ref<TicketItem | null>(null);
const loading = ref(true);
const error = ref('');
const updating = ref(false);

async function fetchTicket() {
  loading.value = true; error.value = '';
  try {
    const id = Number(route.params.id);
    const resp = await apiClient.get<TicketListResponse>('/api/Tickets');
    const found = resp.data.tickets.find(t => t.id === id);
    if (!found) { error.value = '工单不存在'; return; }
    ticket.value = found;
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

async function handleAction(actionItem: (typeof TICKET_ACTIONS_BY_STATUS)[keyof typeof TICKET_ACTIONS_BY_STATUS][0]) {
  if (!ticket.value) return;
  if (['reject', 'close'].includes(actionItem.action)) {
    try {
      await ElMessageBox.confirm(`确认${actionItem.label}工单 #${ticket.value.id}？`, '操作确认', {
        confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning',
      });
    } catch { return; }
  }

  updating.value = true;
  try {
    await apiClient.put(`/api/Tickets/${ticket.value.id}/status`, {
      action: actionItem.action,
      assignee: ticket.value.assignee || void 0,
    });
    const newStatus = ACTION_TO_STATUS[actionItem.action];
    ticket.value.status = newStatus;
    ticket.value.logCount += 1;
    if (isTerminalStatus(newStatus)) ticket.value.isOpen = false;
    ElMessage.success(`${actionItem.label}成功`);
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ElMessage.error(ae.response?.data?.error || '操作失败');
  } finally { updating.value = false; }
}

onMounted(fetchTicket);

const statusBadgeCls = computed(() => {
  if (!ticket.value) return '';
  const m: Record<string, string> = {
    info: 'border-slate-200 text-slate-600 bg-slate-50',
    warning: 'border-amber-200 text-amber-700 bg-amber-50',
    success: 'border-green-200 text-green-700 bg-green-50',
    danger: 'border-red-200 text-red-700 bg-red-50',
    '': 'border-blue-200 text-blue-700 bg-blue-50',
  };
  const c = TICKET_STATUS_COLOR_MAP[ticket.value.status as keyof typeof TICKET_STATUS_COLOR_MAP] ?? '';
  return m[c] || m[''];
});

const priorityBadgeCls = computed(() => {
  if (!ticket.value) return '';
  const m: Record<string, string> = {
    danger: 'border-red-200 text-red-700 bg-red-50',
    warning: 'border-amber-200 text-amber-700 bg-amber-50',
    info: 'border-slate-200 text-slate-600 bg-slate-50',
    '': 'border-blue-200 text-blue-700 bg-blue-50',
  };
  const c = TICKET_PRIORITY_COLOR_MAP[ticket.value.priority] ?? '';
  return m[c] || m[''];
});

const actionBtnCls = (t: string) => {
  const m: Record<string, string> = {
    primary: 'border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100',
    success: 'border-green-200 text-green-700 bg-green-50 hover:bg-green-100',
    warning: 'border-amber-200 text-amber-700 bg-amber-50 hover:bg-amber-100',
    danger: 'border-red-200 text-red-700 bg-red-50 hover:bg-red-100',
    info: 'border-slate-200 text-slate-600 bg-slate-50 hover:bg-slate-100',
  };
  return m[t] || m['info'];
};
</script>

<template>
  <div class="space-y-4 max-w-3xl">
    <button @click="router.push({ name: 'Tickets' })" class="text-xs text-blue-600 hover:text-blue-800 flex items-center gap-1">
      <span class="text-base leading-none">←</span> 返回工单列表
    </button>

    <SkeletonTable v-if="loading" :rows="4" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchTicket" />

    <template v-else-if="ticket">
      <div class="flex items-center justify-between">
        <h1 class="text-xl font-bold text-slate-900">工单 #{{ ticket.id }}</h1>
        <span :class="statusBadgeCls" class="inline-block text-xs px-2 py-1 rounded border">
          {{ TICKET_STATUS_LABEL_MAP[ticket.status] }}
        </span>
      </div>

      <!-- 基本信息卡片 -->
      <div class="bg-white border border-slate-200 rounded p-5 space-y-4">
        <div>
          <label class="text-xs text-slate-400 block mb-1">问题描述</label>
          <p class="text-sm text-slate-800 leading-relaxed">{{ ticket.issue }}</p>
        </div>
        <div>
          <label class="text-xs text-slate-400 block mb-1">整改措施</label>
          <p class="text-sm text-slate-700 leading-relaxed">{{ ticket.action }}</p>
        </div>
        <div class="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <div>
            <label class="text-xs text-slate-400 block mb-1">优先级</label>
            <span :class="priorityBadgeCls" class="inline-block text-xs px-1.5 py-0.5 rounded border font-medium">{{ ticket.priority }}</span>
          </div>
          <div>
            <label class="text-xs text-slate-400 block mb-1">负责人</label>
            <p class="text-sm text-slate-700">{{ ticket.assignee || '未分配' }}</p>
          </div>
          <div>
            <label class="text-xs text-slate-400 block mb-1">法规引用</label>
            <p class="text-sm font-mono text-slate-500">{{ ticket.regulationRef }}</p>
          </div>
          <div>
            <label class="text-xs text-slate-400 block mb-1">建议期限</label>
            <p class="text-sm text-slate-700">{{ new Date(ticket.suggestedDeadline).toLocaleDateString('zh-CN') }}</p>
          </div>
        </div>
        <div class="text-xs text-slate-400">
          操作日志: {{ ticket.logCount }} 条 · 状态: {{ ticket.isOpen ? '进行中' : '已关闭' }}
        </div>
      </div>

      <!-- 操作区 -->
      <div v-if="TICKET_ACTIONS_BY_STATUS[ticket.status].length > 0" class="bg-white border border-slate-200 rounded p-4">
        <h3 class="text-xs font-semibold text-slate-500 mb-3">可用操作</h3>
        <div class="flex gap-2 flex-wrap">
          <button
            v-for="a in TICKET_ACTIONS_BY_STATUS[ticket.status]"
            :key="a.action"
            @click="handleAction(a)"
            :disabled="updating"
            class="text-sm px-4 py-2 rounded border disabled:opacity-40 transition-colors"
            :class="actionBtnCls(a.type)"
          >{{ a.label }}</button>
        </div>
      </div>

      <!-- 状态流转链 (静态展示) -->
      <div class="bg-white border border-slate-200 rounded p-4">
        <h3 class="text-xs font-semibold text-slate-500 mb-3">状态流转</h3>
        <div class="flex items-center gap-2 text-xs">
          <template v-for="(s, i) in (['New','Accepted','InProgress','Completed','Verified'] as const)" :key="s">
            <span v-if="i > 0" class="text-slate-300">→</span>
            <span
              class="px-2 py-0.5 rounded border"
              :class="
                ['New','Accepted','InProgress','Completed','Verified'].indexOf(ticket.status) >= i
                  ? 'border-green-200 text-green-700 bg-green-50'
                  : 'border-slate-200 text-slate-300 bg-slate-50'
              "
            >{{ TICKET_STATUS_LABEL_MAP[s] }}</span>
          </template>
          <span class="text-slate-300 mx-2">|</span>
          <span class="text-slate-400">驳回/关闭</span>
        </div>
      </div>
    </template>
  </div>
</template>
