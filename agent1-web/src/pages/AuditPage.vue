<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type {
  AuditLogEntry,
  AuditLogListResponse,
  AuditIntegrityResponse,
  AuditStatsResponse,
} from '@/types/api';
import { ElMessage } from 'element-plus';
import {
  Search, RefreshRight, CircleCheck, CircleClose, WarningFilled,
} from '@element-plus/icons-vue';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

// ── 状态 ──
const logs = ref<AuditLogEntry[]>([]);
const loading = ref(true);
const error = ref('');
const total = ref(0);
const page = ref(1);
const pageSize = ref(50);

// 筛选条件
const filterUser = ref('');
const filterFrom = ref('');
const filterTo = ref('');

// 哈希链完整性
const integrity = ref<AuditIntegrityResponse | null>(null);
const checkingIntegrity = ref(false);

// 统计
const stats = ref<AuditStatsResponse | null>(null);

// ── 方法 ──

async function fetchAuditLogs() {
  loading.value = true;
  error.value = '';
  try {
    const params: Record<string, string | number> = { page: page.value, pageSize: pageSize.value };
    if (filterUser.value) params.user = filterUser.value;
    if (filterFrom.value) params.from = filterFrom.value;
    if (filterTo.value) params.to = filterTo.value;

    const { data } = await apiClient.get<AuditLogListResponse>('/api/Audit/logs', { params });
    logs.value = data.logs;
    total.value = data.total;
  } catch (err: unknown) {
    const axiosErr = err as { response?: { status?: number } };
    if (axiosErr.response?.status === 403) {
      error.value = '无权限访问审计日志（需要 admin 角色）';
    } else {
      error.value = '加载失败，请稍后重试';
    }
  } finally {
    loading.value = false;
  }
}

async function verifyIntegrity() {
  checkingIntegrity.value = true;
  try {
    const { data } = await apiClient.get<AuditIntegrityResponse>('/api/Audit/integrity');
    integrity.value = data;
    ElMessage[data.intact ? 'success' : 'warning'](data.detail);
  } catch {
    ElMessage.error('哈希链验证失败');
  } finally {
    checkingIntegrity.value = false;
  }
}

async function fetchStats() {
  try {
    const { data } = await apiClient.get<AuditStatsResponse>('/api/Audit/stats');
    stats.value = data;
  } catch { /* non-critical */ }
}

async function exportReport() {
  try {
    const params = { from: filterFrom.value || '2026-01-01', to: filterTo.value || new Date().toISOString() };
    const { data } = await apiClient.get<{ report: string }>('/api/Audit/export', { params });
    const blob = new Blob([data.report], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `audit-report-${new Date().toISOString().slice(0, 10)}.txt`;
    a.click();
    URL.revokeObjectURL(url);
    ElMessage.success('报告已导出');
  } catch {
    ElMessage.error('导出失败');
  }
}

function handlePageChange(p: number) {
  page.value = p;
  fetchAuditLogs();
}

// ── 生命周期 ──
onMounted(() => {
  fetchAuditLogs();
  fetchStats();
});
</script>

<template>
  <div class="space-y-4">
    <!-- 标题栏 -->
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">审计日志</h1>
      <div class="flex gap-2">
        <el-button
          :loading="checkingIntegrity"
          :icon="integrity?.intact ? CircleCheck : WarningFilled"
          :type="integrity?.intact ? 'success' : 'warning'"
          size="small"
          @click="verifyIntegrity"
        >
          {{ integrity?.intact ? '哈希链完整' : checkingIntegrity ? '验证中…' : '验证哈希链' }}
        </el-button>
        <el-button size="small" @click="exportReport">导出报告</el-button>
        <el-button size="small" :icon="RefreshRight" @click="fetchAuditLogs">刷新</el-button>
      </div>
    </div>

    <!-- 统计卡片 -->
    <div v-if="stats" class="grid grid-cols-4 gap-3">
      <div class="bg-white border border-slate-200 rounded px-4 py-3">
        <div class="text-xs text-slate-500 mb-1">总记录</div>
        <div class="text-2xl font-bold text-slate-800">{{ stats.totalCount }}</div>
      </div>
      <div class="bg-white border border-slate-200 rounded px-4 py-3 col-span-3">
        <div class="text-xs text-slate-500 mb-2">操作分布</div>
        <div class="flex flex-wrap gap-2">
          <el-tag
            v-for="(count, op) in stats.byOperation"
            :key="op"
            size="small"
            type="info"
          >
            {{ op }}: {{ count }}
          </el-tag>
        </div>
      </div>
    </div>

    <!-- 筛选栏 -->
    <div class="flex items-center gap-3 bg-white border border-slate-200 rounded px-4 py-2">
      <el-input
        v-model="filterUser"
        placeholder="用户名"
        size="small"
        style="width: 140px"
        clearable
        :prefix-icon="Search"
      />
      <el-date-picker
        v-model="filterFrom"
        type="date"
        placeholder="起始日期"
        size="small"
        style="width: 150px"
        value-format="YYYY-MM-DD"
      />
      <el-date-picker
        v-model="filterTo"
        type="date"
        placeholder="截止日期"
        size="small"
        style="width: 150px"
        value-format="YYYY-MM-DD"
      />
      <el-button type="primary" size="small" @click="fetchAuditLogs">查询</el-button>
    </div>

    <!-- 完整性验证结果 -->
    <el-alert
      v-if="integrity && !integrity.intact"
      :title="`哈希链断裂: ${integrity.detail}`"
      type="error"
      :closable="false"
      show-icon
    />

    <!-- 日志表格 -->
    <SkeletonTable v-if="loading" :rows="8" />

    <div v-else-if="error" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="error" :title="error" @action="fetchAuditLogs" />
    </div>

    <div v-else-if="logs.length === 0" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="empty" title="暂无审计日志" description="系统操作记录将自动生成" />
    </div>

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-16">ID</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-36">时间</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">用户</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">操作</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">详情</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">哈希链</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="log in logs"
            :key="log.id"
            class="border-b border-slate-100 hover:bg-slate-50"
          >
            <td class="px-4 py-3 text-xs text-slate-400 font-mono">{{ log.id }}</td>
            <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ log.timestamp }}</td>
            <td class="px-4 py-3 text-xs text-slate-700">{{ log.user }}</td>
            <td class="px-4 py-3">
              <el-tag size="small" :type="log.isSensitive ? 'warning' : ''">
                {{ log.operation }}
              </el-tag>
            </td>
            <td class="px-4 py-3 text-xs text-slate-600 max-w-xs truncate">
              {{ log.details }}
            </td>
            <td class="px-4 py-3 text-xs text-slate-400 font-mono">
              {{ log.chainHash || '—' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 分页 -->
    <div v-if="total > pageSize" class="flex justify-center">
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="prev, pager, next"
        small
        @current-change="handlePageChange"
      />
    </div>
  </div>
</template>
