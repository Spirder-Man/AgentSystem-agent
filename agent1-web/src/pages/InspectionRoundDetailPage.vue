<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { InspectionRoundDetail } from '@/types/api';
import { ArrowLeft, CircleCheck, CircleClose, WarningFilled } from '@element-plus/icons-vue';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const route = useRoute();
const router = useRouter();
const roundId = route.params.roundId as string;

const round = ref<InspectionRoundDetail | null>(null);
const loading = ref(true);
const error = ref('');

const complianceColor = computed(() => {
  const r = round.value?.complianceRate ?? 0;
  if (r >= 0.9) return 'text-green-600';
  if (r >= 0.7) return 'text-amber-600';
  return 'text-red-600';
});

async function fetchRound() {
  loading.value = true; error.value = '';
  try {
    const { data } = await apiClient.get<InspectionRoundDetail>(`/api/Inspection/rounds/${roundId}`);
    round.value = data;
  } catch { error.value = '加载巡检记录失败'; }
  finally { loading.value = false; }
}

function viewReport() { router.push(`/inspection/report/${roundId}`); }
function viewPlan() { if (round.value) router.push(`/inspection/plans/${round.value.planId}`); }
function goBack() { router.push('/inspection/rounds'); }

function elapsed(ms: number) {
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

onMounted(fetchRound);
</script>

<template>
  <div class="space-y-4">
    <!-- 返回导航 -->
    <div class="flex items-center gap-2">
      <el-button :icon="ArrowLeft" size="small" text @click="goBack">返回巡检记录</el-button>
    </div>

    <!-- 加载态 -->
    <SkeletonCard v-if="loading" />

    <!-- 错误态 -->
    <div v-else-if="error" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="error" :title="error" @action="fetchRound" />
    </div>

    <!-- 正常态 -->
    <template v-else-if="round">
      <!-- 头部 -->
      <div class="bg-white border border-slate-200 rounded p-6">
        <div class="flex items-start justify-between">
          <div>
            <h1 class="text-xl font-bold text-slate-900 font-mono text-sm">{{ round.roundId }}</h1>
            <div class="flex items-center gap-3 mt-2">
              <button
                @click="viewPlan"
                class="text-sm text-blue-600 hover:text-blue-800 hover:underline transition-colors"
              >📋 计划 {{ round.planId }}</button>
              <span class="text-xs text-slate-400">
                {{ new Date(round.startedAt).toLocaleDateString('zh-CN') }}
                {{ new Date(round.startedAt).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }) }}
              </span>
            </div>
          </div>
          <div class="flex items-center gap-2">
            <button
              @click="viewReport"
              class="px-3 py-1.5 rounded text-xs font-medium border border-blue-200 text-blue-700 bg-blue-50 hover:bg-blue-100 transition-colors"
            >📄 查看报告</button>
          </div>
        </div>
      </div>

      <!-- 巡检概览 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4">巡检概览</h2>
        <div class="grid grid-cols-2 sm:grid-cols-5 gap-4">
          <div class="text-center p-3 bg-slate-50 rounded">
            <p class="text-2xl font-bold" :class="complianceColor">{{ Math.round(round.complianceRate * 100) }}%</p>
            <p class="text-xs text-slate-400 mt-1">合规率</p>
          </div>
          <div class="text-center p-3 bg-green-50 rounded">
            <p class="text-2xl font-bold text-green-600">{{ round.compliantCount }}</p>
            <p class="text-xs text-slate-400 mt-1">合格</p>
          </div>
          <div class="text-center p-3 rounded" :class="round.nonCompliantCount > 0 ? 'bg-red-50' : 'bg-slate-50'">
            <p class="text-2xl font-bold" :class="round.nonCompliantCount > 0 ? 'text-red-600' : 'text-slate-400'">{{ round.nonCompliantCount }}</p>
            <p class="text-xs text-slate-400 mt-1">不合格</p>
          </div>
          <div class="text-center p-3 rounded" :class="round.warningCount > 0 ? 'bg-amber-50' : 'bg-slate-50'">
            <p class="text-2xl font-bold" :class="round.warningCount > 0 ? 'text-amber-600' : 'text-slate-400'">{{ round.warningCount }}</p>
            <p class="text-xs text-slate-400 mt-1">警告</p>
          </div>
          <div class="text-center p-3 rounded" :class="round.ticketCount > 0 ? 'bg-orange-50' : 'bg-slate-50'">
            <p class="text-2xl font-bold" :class="round.ticketCount > 0 ? 'text-orange-600' : 'text-slate-400'">{{ round.ticketCount }}</p>
            <p class="text-xs text-slate-400 mt-1">工单</p>
          </div>
        </div>
        <dl class="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2 mt-4 pt-4 border-t border-slate-100 text-sm">
          <div>
            <dt class="text-slate-400 text-xs">执行人</dt>
            <dd class="text-slate-800">{{ round.executedBy }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs">开始时间</dt>
            <dd class="text-slate-800 font-mono text-xs">{{ new Date(round.startedAt).toLocaleString('zh-CN') }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs">完成时间</dt>
            <dd class="text-slate-800 font-mono text-xs">{{ round.completedAt ? new Date(round.completedAt).toLocaleString('zh-CN') : '—' }}</dd>
          </div>
          <div>
            <dt class="text-slate-400 text-xs">总耗时</dt>
            <dd class="text-slate-800 font-mono">{{ elapsed(round.totalElapsedMs) }}</dd>
          </div>
        </dl>
      </div>

      <!-- 检查结果详情 -->
      <div class="bg-white border border-slate-200 rounded overflow-hidden">
        <div class="px-5 py-3 border-b border-slate-200 bg-slate-50">
          <h2 class="text-sm font-semibold text-slate-700">检查结果 ({{ round.results.length }} 项)</h2>
        </div>
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-slate-200 bg-slate-50">
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-12">#</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-16">合规</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">结论</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-36">适用法规</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-16">警告</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-32">调用工具</th>
              <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">耗时</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, idx) in round.results" :key="item.itemId" class="border-b border-slate-100 hover:bg-slate-50">
              <td class="px-4 py-3 text-xs text-slate-400 font-mono">{{ idx + 1 }}</td>
              <td class="px-4 py-3">
                <el-icon v-if="item.isCompliant === true" :size="18" class="text-green-600"><CircleCheck /></el-icon>
                <el-icon v-else-if="item.isCompliant === false" :size="18" class="text-red-600"><CircleClose /></el-icon>
                <span v-else class="text-xs text-slate-300">—</span>
              </td>
              <td class="px-4 py-3 text-slate-800 max-w-xs truncate" :title="item.conclusion">{{ item.conclusion }}</td>
              <td class="px-4 py-3">
                <span v-if="item.regulationRef" class="text-xs font-mono text-blue-600 bg-blue-50 px-1.5 py-0.5 rounded border border-blue-100">{{ item.regulationRef }}</span>
                <span v-else class="text-xs text-slate-300">—</span>
              </td>
              <td class="px-4 py-3">
                <span v-if="item.warnings > 0" class="inline-flex items-center gap-1 text-xs text-amber-600">
                  <el-icon :size="14"><WarningFilled /></el-icon>{{ item.warnings }}
                </span>
                <span v-else class="text-xs text-slate-300">—</span>
              </td>
              <td class="px-4 py-3">
                <div class="flex flex-wrap gap-1">
                  <span v-for="tool in item.tools" :key="tool" class="text-xs px-1 py-0.5 rounded bg-slate-100 text-slate-600 border border-slate-200 font-mono">{{ tool }}</span>
                </div>
              </td>
              <td class="px-4 py-3 text-xs text-slate-500 font-mono">{{ elapsed(item.elapsedMs) }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="round.results.length === 0" class="px-5 py-8 text-center text-sm text-slate-400">暂无检查结果</div>
      </div>
    </template>
  </div>
</template>
