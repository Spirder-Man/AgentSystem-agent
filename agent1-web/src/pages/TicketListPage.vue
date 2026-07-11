<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
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

const router = useRouter();
const data = ref<TicketListResponse | null>(null);
const loading = ref(true);
const error = ref('');
const updatingId = ref<number | null>(null);

async function fetchTickets() {
  loading.value = true; error.value = '';
  try {
    const resp = await apiClient.get<TicketListResponse>('/api/Tickets');
    data.value = resp.data;
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

async function handleAction(ticket: TicketItem, actionItem: (typeof TICKET_ACTIONS_BY_STATUS)[keyof typeof TICKET_ACTIONS_BY_STATUS][0]) {
  if (['reject', 'close'].includes(actionItem.action)) {
    try {
      await ElMessageBox.confirm(`确认${actionItem.label}工单 #${ticket.id}？`, '操作确认', {
        confirmButtonText: '确认',
        cancelButtonText: '取消',
        type: 'warning',
      });
    } catch { return; }
  }

  updatingId.value = ticket.id;
  try {
    await apiClient.put(`/api/Tickets/${ticket.id}/status`, {
      action: actionItem.action,
      assignee: ticket.assignee || void 0,
    });
    const newStatus = ACTION_TO_STATUS[actionItem.action];
    ticket.status = newStatus;
    ticket.logCount += 1;
    if (isTerminalStatus(newStatus)) ticket.isOpen = false;
    if (data.value) data.value.open = data.value.tickets.filter(t => t.isOpen).length;
    ElMessage.success(`${actionItem.label}成功`);
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    ElMessage.error(ae.response?.data?.error || '操作失败');
  } finally { updatingId.value = null; }
}

function viewDetail(ticket: TicketItem) {
  router.push({ name: 'TicketDetail', params: { id: ticket.id } });
}

onMounted(fetchTickets);

const statusBadgeCls = (s: string) => {
  const m: Record<string, string> = {
    info: 'border-slate-200 text-slate-600 bg-slate-50',
    warning: 'border-amber-200 text-amber-700 bg-amber-50',
    success: 'border-green-200 text-green-700 bg-green-50',
    danger: 'border-red-200 text-red-700 bg-red-50',
    '': 'border-blue-200 text-blue-700 bg-blue-50',
  };
  const c = TICKET_STATUS_COLOR_MAP[s as keyof typeof TICKET_STATUS_COLOR_MAP] ?? '';
  return m[c] || m[''];
};

const priorityBadgeCls = (p: string) => {
  const m: Record<string, string> = {
    danger: 'border-red-200 text-red-700 bg-red-50',
    warning: 'border-amber-200 text-amber-700 bg-amber-50',
    info: 'border-slate-200 text-slate-600 bg-slate-50',
    '': 'border-blue-200 text-blue-700 bg-blue-50',
  };
  const c = TICKET_PRIORITY_COLOR_MAP[p] ?? '';
  return m[c] || m[''];
};

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
  <div class="space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">工单管理</h1>
      <span v-if="data" class="text-xs text-slate-400">共 {{ data.total }} 条，未关闭 {{ data.open }} 条</span>
    </div>

    <SkeletonTable v-if="loading" :rows="5" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchTickets" />
    <EmptyState v-else-if="!data || data.tickets.length === 0" icon="empty" title="暂无工单" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-12">#</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">问题描述</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">优先级</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">状态</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">负责人</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-36">法规引用</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">建议期限</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-52">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="t in data.tickets" :key="t.id"
            class="border-b border-slate-100 hover:bg-slate-50"
            :class="{ 'opacity-60': !t.isOpen }"
          >
            <td class="px-4 py-3 font-mono text-slate-400 text-xs">{{ t.id }}</td>
            <td class="px-4 py-3">
              <button @click="viewDetail(t)" class="text-slate-800 hover:text-blue-600 text-left cursor-pointer leading-relaxed">
                {{ t.issue }}
              </button>
            </td>
            <td class="px-4 py-3">
              <span :class="priorityBadgeCls(t.priority)" class="inline-block text-xs px-1.5 py-0.5 rounded border font-medium">
                {{ t.priority }}
              </span>
            </td>
            <td class="px-4 py-3">
              <span :class="statusBadgeCls(t.status)" class="inline-block text-xs px-1.5 py-0.5 rounded border">
                {{ TICKET_STATUS_LABEL_MAP[t.status] }}
              </span>
            </td>
            <td class="px-4 py-3 text-slate-500 text-xs">{{ t.assignee || '—' }}</td>
            <td class="px-4 py-3 font-mono text-xs text-slate-400">{{ t.regulationRef }}</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ new Date(t.suggestedDeadline).toLocaleDateString('zh-CN') }}</td>
            <td class="px-4 py-3">
              <div class="flex gap-1 flex-wrap">
                <button
                  v-for="a in TICKET_ACTIONS_BY_STATUS[t.status]"
                  :key="a.action"
                  @click="handleAction(t, a)"
                  :disabled="updatingId === t.id"
                  class="text-xs px-2 py-1 rounded border disabled:opacity-40 transition-colors"
                  :class="actionBtnCls(a.type)"
                >{{ a.label }}</button>
                <span v-if="TICKET_ACTIONS_BY_STATUS[t.status].length === 0" class="text-xs text-slate-300">—</span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
