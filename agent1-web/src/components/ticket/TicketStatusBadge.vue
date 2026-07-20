<script setup lang="ts">
// ============================================================
// TicketStatusBadge — 工单状态徽章
// 使用 Element Plus el-tag 基于 TicketStatus 枚举渲染
// ============================================================

import { computed } from 'vue';
import type { TicketStatus } from '@/types/api';
import { TICKET_STATUS_COLOR_MAP, TICKET_STATUS_LABEL_MAP } from '@/utils/constants';

const props = withDefaults(
  defineProps<{
    status: TicketStatus;
    /** 是否显示中文标签文字 */
    showLabel?: boolean;
  }>(),
  {
    showLabel: true,
  }
);

const tagType = computed(() => {
  const color = TICKET_STATUS_COLOR_MAP[props.status];
  return color || undefined;
});

const label = computed(() => TICKET_STATUS_LABEL_MAP[props.status]);
</script>

<template>
  <el-tag :type="tagType" size="small">
    {{ showLabel ? label : status }}
  </el-tag>
</template>
