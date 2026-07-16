<script setup lang="ts">
import { ref, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { DbInfoResponse, DbValidateResponse } from '@/types/api';
import { ElMessage } from 'element-plus';
import {
  Refresh, DataBoard, Connection, Clock, Coin,
} from '@element-plus/icons-vue';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

// ── 数据库信息 ──
const dbInfo = ref<DbInfoResponse | null>(null);
const infoLoading = ref(true);
const infoError = ref('');

// ── 数据库验证 ──
const validateResult = ref<DbValidateResponse | null>(null);
const validating = ref(false);
const validateError = ref('');

async function fetchDbInfo() {
  infoLoading.value = true;
  infoError.value = '';
  try {
    const { data } = await apiClient.get<DbInfoResponse>('/api/admin/db/info');
    dbInfo.value = data;
  } catch {
    infoError.value = '加载数据库信息失败';
  } finally {
    infoLoading.value = false;
  }
}

async function runValidate() {
  validating.value = true;
  validateError.value = '';
  validateResult.value = null;
  try {
    const { data } = await apiClient.get<DbValidateResponse>('/api/admin/db/validate');
    validateResult.value = data;
    ElMessage.success(`验证完成 · 耗时 ${data.elapsedMs}ms`);
  } catch {
    validateError.value = '数据库验证失败，请稍后重试';
  } finally {
    validating.value = false;
  }
}

onMounted(fetchDbInfo);
</script>

<template>
  <div class="space-y-6 max-w-4xl">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-bold text-slate-900">数据库诊断</h1>
        <p class="text-xs text-slate-500 mt-1">数据库连接信息、表结构概览与连接验证（Admin only）</p>
      </div>
      <el-button
        :icon="Refresh"
        size="small"
        text
        @click="fetchDbInfo"
        :disabled="infoLoading"
      >刷新</el-button>
    </div>

    <!-- 加载态 -->
    <SkeletonCard v-if="infoLoading" :count="2" />

    <!-- 错误态 -->
    <div v-else-if="infoError" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="error" :title="infoError" @action="fetchDbInfo" />
    </div>

    <template v-else-if="dbInfo">
      <!-- 数据库信息卡片 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4 flex items-center gap-2">
          <el-icon><DataBoard /></el-icon>
          数据库信息
        </h2>

        <div class="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-4">
          <div class="bg-slate-50 rounded p-3">
            <p class="text-xs text-slate-400 mb-1">主机</p>
            <p class="text-sm font-mono text-slate-700">{{ dbInfo.info.host }}</p>
          </div>
          <div class="bg-slate-50 rounded p-3">
            <p class="text-xs text-slate-400 mb-1">端口</p>
            <p class="text-sm font-mono text-blue-700">{{ dbInfo.info.port }}</p>
          </div>
          <div class="bg-slate-50 rounded p-3">
            <p class="text-xs text-slate-400 mb-1">数据库</p>
            <p class="text-sm font-mono text-slate-700 break-all">{{ dbInfo.info.database }}</p>
          </div>
          <div class="bg-slate-50 rounded p-3">
            <p class="text-xs text-slate-400 mb-1">版本</p>
            <p class="text-sm font-mono text-green-700">{{ dbInfo.info.version }}</p>
          </div>
        </div>

        <!-- 表列表 -->
        <div>
          <p class="text-xs text-slate-500 mb-2">
            数据表 <span class="font-mono font-bold text-blue-700">{{ dbInfo.tables.length }}</span> 个
          </p>
          <div class="flex flex-wrap gap-1.5">
            <span
              v-for="table in dbInfo.tables"
              :key="table"
              class="text-xs px-2 py-0.5 bg-slate-100 border border-slate-200 rounded font-mono text-slate-600"
            >{{ table }}</span>
          </div>
        </div>
      </div>

      <!-- 连接验证 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4 flex items-center gap-2">
          <el-icon><Connection /></el-icon>
          连接验证
        </h2>
        <p class="text-xs text-slate-500 mb-4">
          测试数据库连接并返回完整诊断信息，包括连接状态、表统计和服务器配置
        </p>

        <el-button
          type="primary"
          :loading="validating"
          @click="runValidate"
        >
          {{ validating ? '验证中…' : '🔍 执行连接验证' }}
        </el-button>

        <!-- 验证错误 -->
        <div v-if="validateError" class="mt-4 p-3 bg-red-50 border border-red-200 rounded text-sm text-red-700">
          {{ validateError }}
        </div>

        <!-- 验证结果 -->
        <div v-if="validateResult" class="mt-5 space-y-4">
          <!-- 连接状态 -->
          <div class="flex items-center gap-3">
            <span
              class="w-3 h-3 rounded-full"
              :class="validateResult.connected ? 'bg-green-500' : 'bg-red-500'"
            />
            <span class="text-sm font-semibold" :class="validateResult.connected ? 'text-green-700' : 'text-red-700'">
              {{ validateResult.connected ? '连接正常' : '连接失败' }}
            </span>
            <span class="text-xs text-slate-400 ml-auto">
              <el-icon class="mr-1"><Clock /></el-icon>
              {{ validateResult.elapsedMs }}ms
            </span>
          </div>

          <!-- 服务器信息 -->
          <div class="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <div class="bg-slate-50 rounded p-2">
              <p class="text-[11px] text-slate-400">主机</p>
              <p class="text-xs font-mono text-slate-700">{{ validateResult.server.host }}</p>
            </div>
            <div class="bg-slate-50 rounded p-2">
              <p class="text-[11px] text-slate-400">端口</p>
              <p class="text-xs font-mono text-blue-700">{{ validateResult.server.port }}</p>
            </div>
            <div class="bg-slate-50 rounded p-2">
              <p class="text-[11px] text-slate-400">用户</p>
              <p class="text-xs font-mono text-slate-700">{{ validateResult.server.user }}</p>
            </div>
            <div class="bg-slate-50 rounded p-2">
              <p class="text-[11px] text-slate-400">表数量</p>
              <p class="text-xs font-mono font-bold text-green-700">{{ validateResult.tableCount }}</p>
            </div>
          </div>

          <!-- 验证时间 -->
          <p class="text-[11px] text-slate-400">
            验证时间: {{ new Date(validateResult.verifiedAt).toLocaleString('zh-CN') }}
          </p>
        </div>
      </div>
    </template>
  </div>
</template>
