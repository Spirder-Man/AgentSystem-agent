<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

interface AuditEntry {
  id: string; action: string; user: string; resource: string;
  timestamp: string; result: string; ip: string;
}

const logs = ref<AuditEntry[]>([]);
const loading = ref(true);
const error = ref('');

async function fetchAuditLogs() {
  loading.value = true; error.value = '';
  try {
    // 模拟审计日志数据（mock 后端暂无专用审计端点）
    await new Promise(r => setTimeout(r, 200));
    logs.value = [
      { id: 'a1', action: '登录', user: 'admin', resource: '系统', timestamp: '2026-07-10 09:15:23', result: '成功', ip: '192.168.1.100' },
      { id: 'a2', action: '执行巡检', user: '张三', resource: '甲类仓库周检', timestamp: '2026-07-10 09:18:45', result: '成功', ip: '192.168.1.101' },
      { id: 'a3', action: '受理工单', user: 'admin', resource: '工单 #1', timestamp: '2026-07-10 09:20:12', result: '成功', ip: '192.168.1.100' },
      { id: 'a4', action: '合规查询', user: 'auditor', resource: '/api/Compliance/check', timestamp: '2026-07-10 09:22:30', result: '成功', ip: '192.168.1.102' },
      { id: 'a5', action: '修改配置', user: 'admin', resource: '系统设置', timestamp: '2026-07-10 09:25:00', result: '成功', ip: '192.168.1.100' },
      { id: 'a6', action: '导出报告', user: '张三', resource: '甲类仓库周检报告', timestamp: '2026-07-10 09:28:17', result: '成功', ip: '192.168.1.101' },
      { id: 'a7', action: '登录', user: 'viewer', resource: '系统', timestamp: '2026-07-10 09:30:05', result: '失败', ip: '10.0.0.55' },
      { id: 'a8', action: '关闭工单', user: 'admin', resource: '工单 #3', timestamp: '2026-07-10 09:32:40', result: '成功', ip: '192.168.1.100' },
    ];
  } catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

onMounted(fetchAuditLogs);
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-xl font-bold text-slate-900">审计日志</h1>

    <SkeletonTable v-if="loading" :rows="5" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchAuditLogs" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">时间</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">用户</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">操作</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">资源</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">结果</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-36">IP</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in logs" :key="log.id" class="border-b border-slate-100 hover:bg-slate-50">
            <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ log.timestamp }}</td>
            <td class="px-4 py-3 text-xs text-slate-700">{{ log.user }}</td>
            <td class="px-4 py-3 text-xs text-slate-700">{{ log.action }}</td>
            <td class="px-4 py-3 text-xs text-slate-600">{{ log.resource }}</td>
            <td class="px-4 py-3">
              <span :class="log.result === '成功' ? 'text-green-600' : 'text-red-600'" class="text-xs font-medium">{{ log.result }}</span>
            </td>
            <td class="px-4 py-3 text-xs text-slate-400 font-mono">{{ log.ip }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
