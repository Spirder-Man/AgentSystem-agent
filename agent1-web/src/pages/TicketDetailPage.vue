<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { TicketListResponse, TicketItem, TicketFollowupResult } from '@/types/api';
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
import { useLoadingBar } from '@/lib/useLoadingBar';

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

// ── 工单跟进 ──
const followupResult = ref('');
const followupTickets = ref<TicketItem[]>([]);
const followupLoading = ref(false);
const followupError = ref('');
const { start: flStart, stop: flStop } = useLoadingBar();

async function runFollowup() {
  if (!followupResult.value.trim()) return;
  followupError.value = '';
  followupTickets.value = [];
  followupLoading.value = true;
  flStart('正在生成跟进工单…');
  try {
    const { data } = await apiClient.post<TicketFollowupResult>('/api/tickets/followup', {
      complianceResult: followupResult.value.trim(),
    });
    followupTickets.value = data.tickets;
    ElMessage.success(`生成 ${data.tickets.length} 个跟进工单`);
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    followupError.value = ae.response?.data?.error || '跟进失败';
  } finally { followupLoading.value = false; flStop(); }
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

    <!-- 工单跟进 (独立功能区) -->
    <div class="bg-white border border-slate-200 rounded p-4 mt-6">
      <h2 class="text-sm font-semibold text-slate-700 mb-3">工单跟进</h2>
      <p class="text-xs text-slate-500 mb-3">输入合规审核结果文本，AI 自动解析并生成跟进工单</p>
      <div class="flex gap-2 mb-3">
        <textarea
          v-model="followupResult"
          :disabled="followupLoading"
          rows="3"
          class="flex-1 px-3 py-2 text-sm border border-slate-300 rounded focus:outline-none focus:border-blue-400 resize-none"
          placeholder="粘贴合规审核结果，如：苯与丙酮同库储存违规 → GB 15603-2022 §4.2.2…"
        />
      </div>
      <button
        @click="runFollowup"
        :disabled="followupLoading || !followupResult.trim()"
        class="text-xs px-4 py-2 rounded border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 disabled:opacity-50 transition-colors"
      >{{ followupLoading ? '解析中…' : '📋 生成跟进工单' }}</button>

      <div v-if="followupError" class="mt-3 bg-red-50 border border-red-200 rounded p-3 text-sm text-red-700">{{ followupError }}</div>

      <div v-if="followupTickets.length" class="mt-4 space-y-2">
        <div class="text-xs text-slate-400 mb-2">生成的工单：</div>
        <div v-for="t in followupTickets" :key="t.id"
          class="flex items-center justify-between p-3 bg-slate-50 border border-slate-200 rounded text-xs"
        >
          <div class="flex-1">
            <span class="font-medium text-slate-800">#{{ t.id }} {{ t.issue }}</span>
            <div class="text-slate-400 mt-0.5">{{ t.action }} · {{ t.regulationRef }}</div>
          </div>
          <router-link :to="`/tickets/${t.id}`" class="text-blue-600 hover:text-blue-800 ml-3 shrink-0">查看 →</router-link>
        </div>
      </div>
    </div>
  </div>
</template>
