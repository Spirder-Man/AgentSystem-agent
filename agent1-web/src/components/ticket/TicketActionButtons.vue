<script setup lang="ts">
// ============================================================
// TicketActionButtons — 工单操作按钮组
// 根据当前工单状态，动态显示合法的操作按钮
// 对齐后端 TicketsController.UpdateStatus 的状态机约束
// ============================================================

import { computed } from 'vue';
import type { TicketStatus, TicketStatusUpdateRequest } from '@/types/api';
import { TICKET_ACTIONS_BY_STATUS } from '@/utils/constants';

const props = defineProps<{
  status: TicketStatus;
  ticketId: number;
  assignee: string;
  disabled?: boolean;
}>();

const emit = defineEmits<{
  (e: 'action', payload: { action: TicketStatusUpdateRequest['action']; ticketId: number; assignee?: string }): void;
}>();

const availableActions = computed(() => {
  return TICKET_ACTIONS_BY_STATUS[props.status] ?? [];
});

function handleAction(action: TicketStatusUpdateRequest['action']) {
  emit('action', {
    action,
    ticketId: props.ticketId,
    assignee: props.assignee || undefined,
  });
}
</script>

<template>
  <div v-if="availableActions.length > 0" class="flex gap-2">
    <el-button
      v-for="item in availableActions"
      :key="item.action"
      :type="item.type"
      size="small"
      :disabled="disabled"
      @click="handleAction(item.action)"
    >
      {{ item.label }}
    </el-button>
  </div>
  <span v-else class="text-gray-400 text-sm">—</span>
</template>
