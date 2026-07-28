<script setup lang="ts">
// ============================================================
// DashboardPage — 合规总览仪表盘
// 消费 DashboardController 全部 6 个 REST 端点:
//   GET  /api/Dashboard/overview       → 概览统计
//   GET  /api/Dashboard/findings       → 合规发现列表
//   GET  /api/Dashboard/history        → 巡检历史
//   GET  /api/Dashboard/report/hazard  → 隐患报告
//   POST /api/Dashboard/scan           → 自动合规扫描
//   GET  /api/Dashboard/assets         → (保留: 可用于资产详情页跳转)
// ============================================================

import { ref, onMounted, computed } from 'vue';
import { useAuthStore } from '@/stores/auth';
import apiClient from '@/lib/axios';
import type {
  DashboardOverview,
  DashboardFindingsResponse,
  DashboardHistoryResponse,
  DashboardHazardReport,
  DashboardScanResult,
  QuickCheckResult,
} from '@/types/api';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const auth = useAuthStore();

// ── 数据状态 ──
const overview = ref<DashboardOverview | null>(null);
const findings = ref<DashboardFindingsResponse | null>(null);
const history = ref<DashboardHistoryResponse | null>(null);
const hazardReport = ref<DashboardHazardReport | null>(null);

const loading = ref(true);
const error = ref('');

// ── UI 状态 ──
const activeTab = ref<'findings' | 'history' | 'hazard'>('findings');

// ── 合规率百分比 ──
const complianceRatePercent = computed(() => (overview.value ? Math.round(overview.value.complianceRate * 100) : 0));

const remediationRatePercent = computed(() => (overview.value ? Math.round(overview.value.remediationRate * 100) : 0));

// ── 严重程度颜色映射 ──
const severityColor = (s: string) =>
  ({
    Critical: 'text-red-700 bg-red-50',
    High: 'text-orange-700 bg-orange-50',
    Medium: 'text-amber-700 bg-amber-50',
    Low: 'text-blue-700 bg-blue-50',
    Info: 'text-slate-500 bg-slate-50',
  })[s] ?? 'text-slate-500 bg-slate-50';

const severityBarColor = (s: string) =>
  ({ Critical: 'bg-red-500', High: 'bg-orange-500', Medium: 'bg-amber-500', Low: 'bg-blue-400', Info: 'bg-slate-300' })[
    s
  ] ?? 'bg-slate-300';

// ── 巡检状态中文 ──
const planStatusLabel = (s: string) =>
  ({ Draft: '草稿', InProgress: '进行中', Completed: '已完成', Archived: '已归档' })[s] ?? s;

// ── 数据加载 ──
async function fetchAll() {
  loading.value = true;
  error.value = '';
  try {
    const [ov, f, h] = await Promise.all([
      apiClient.get<DashboardOverview>('/api/Dashboard/overview'),
      apiClient.get<DashboardFindingsResponse>('/api/Dashboard/findings', { params: { openOnly: true } }),
      apiClient.get<DashboardHistoryResponse>('/api/Dashboard/history'),
    ]);
    overview.value = ov.data;
    findings.value = f.data;
    history.value = h.data;
  } catch {
    error.value = '加载仪表盘数据失败，请检查后端服务';
  } finally {
    loading.value = false;
  }
}

async function fetchHazardReport() {
  try {
    const { data } = await apiClient.get<DashboardHazardReport>('/api/Dashboard/report/hazard');
    hazardReport.value = data;
  } catch {
    /* 静默失败，用户可手动重试 */
  }
}

onMounted(() => {
  fetchAll();
  fetchHazardReport();
});

// ── 统计卡片 ──
const statCards = computed(() => {
  if (!overview.value) return [];
  const o = overview.value;
  return [
    {
      label: '合规率',
      value: `${complianceRatePercent.value}%`,
      color: o.complianceRate >= 0.8 ? 'text-emerald-600' : o.complianceRate >= 0.6 ? 'text-amber-600' : 'text-red-600',
      sub: `${o.compliantAssets}/${o.checkedAssets} 合规`,
    },
    {
      label: '资产总量',
      value: String(o.totalAssets),
      color: 'text-slate-900',
      sub: `${o.checkedAssets} 已检查 · ${o.totalAssets - o.checkedAssets} 未检查`,
    },
    {
      label: '未闭环发现',
      value: String(o.openFindings),
      color: o.openFindings > 0 ? 'text-red-600' : 'text-emerald-600',
      sub: `共 ${o.totalFindings} 条 · 整改率 ${remediationRatePercent.value}%`,
    },
    {
      label: '最近扫描',
      value: o.lastAutoScanAt ? new Date(o.lastAutoScanAt).toLocaleDateString('zh-CN') : '暂无',
      color: o.lastAutoScanAt ? 'text-blue-600' : 'text-slate-400',
      sub: o.lastAutoScanAt
        ? new Date(o.lastAutoScanAt).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
        : '尚未执行自动扫描',
    },
  ];
});

// ── 严重程度分布 ──
const severityBars = computed(() => {
  if (!overview.value) return [];
  const m = overview.value.findingsBySeverity;
  const max = Math.max(1, ...Object.values(m));
  const order = ['Critical', 'High', 'Medium', 'Low', 'Info'];
  return order.filter((k) => k in m).map((k) => ({ name: k, count: m[k], pct: Math.round((m[k] / max) * 100) }));
});

// ── 一键快检 ──
const quickQuery = ref('');
const quickResult = ref<QuickCheckResult | null>(null);
const quickLoading = ref(false);
const quickError = ref('');

async function runQuickCheck() {
  if (!quickQuery.value.trim()) return;
  quickError.value = '';
  quickResult.value = null;
  quickLoading.value = true;
  try {
    const { data } = await apiClient.post<QuickCheckResult>('/api/Inspection/quick-check', {
      query: quickQuery.value.trim(),
    });
    quickResult.value = data;
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    quickError.value = ae.response?.data?.error || '快检失败';
  } finally {
    quickLoading.value = false;
  }
}

// ── 自动扫描 ──
const scanResult = ref<DashboardScanResult | null>(null);
const scanLoading = ref(false);
const scanError = ref('');

async function runAutoScan() {
  scanError.value = '';
  scanResult.value = null;
  scanLoading.value = true;
  try {
    const { data } = await apiClient.post<DashboardScanResult>('/api/Dashboard/scan', null, { timeout: 600_000 });
    scanResult.value = data;
    // 扫描完成后刷新总览
    await fetchAll();
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    scanError.value = ae.response?.data?.error || '扫描启动失败';
  } finally {
    scanLoading.value = false;
  }
}
</script>

<template>
  <div class="space-y-5">
    <!-- 页头 -->
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">合规仪表盘</h1>
      <span class="text-xs text-slate-400">{{ auth.username }} · {{ auth.role }}</span>
    </div>

    <!-- 加载态 -->
    <SkeletonCard v-if="loading" :count="4" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchAll" />

    <template v-else-if="overview">
      <!-- ── 统计卡片 ── -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div
          v-for="c in statCards"
          :key="c.label"
          class="bg-white border border-slate-200 rounded-lg p-4 hover:shadow-sm transition-shadow"
        >
          <p class="text-xs text-slate-500 mb-1">{{ c.label }}</p>
          <p class="text-2xl font-bold" :class="c.color">{{ c.value }}</p>
          <p class="text-xs text-slate-400 mt-1">{{ c.sub }}</p>
        </div>
      </div>

      <!-- ── 快速操作区 ── -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <!-- 一键快检 -->
        <div class="bg-white border border-slate-200 rounded-lg p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-3">🔍 一键快检</h3>
          <div class="flex gap-2 mb-3">
            <input
              v-model="quickQuery"
              @keyup.enter="runQuickCheck"
              :disabled="quickLoading"
              class="flex-1 px-3 py-1.5 text-xs border border-slate-300 rounded focus:outline-none focus:border-blue-400"
              placeholder="输入合规问题，如：硝酸储存条件是否合规"
            />
            <button
              @click="runQuickCheck"
              :disabled="quickLoading || !quickQuery.trim()"
              class="px-4 py-1.5 text-xs font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >
              {{ quickLoading ? '检测中…' : '检测' }}
            </button>
          </div>
          <div v-if="quickError" class="text-xs text-red-600">{{ quickError }}</div>
          <div
            v-if="quickResult"
            class="p-3 rounded border text-xs space-y-1"
            :class="quickResult.isCompliant ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'"
          >
            <p class="font-medium" :class="quickResult.isCompliant ? 'text-green-800' : 'text-red-800'">
              {{ quickResult.isCompliant ? '✅ 合规' : '❌ 不合规' }}
              <span class="text-slate-400 font-normal ml-2">{{ quickResult.elapsedMs }}ms</span>
            </p>
            <p class="text-slate-600">{{ quickResult.conclusion }}</p>
            <p v-if="quickResult.regulationRef" class="text-slate-400">法规: {{ quickResult.regulationRef }}</p>
            <p v-if="quickResult.warnings.length" class="text-amber-600">⚠ {{ quickResult.warnings.join('; ') }}</p>
          </div>
        </div>

        <!-- 自动扫描 (Dashboard/scan) -->
        <div class="bg-white border border-slate-200 rounded-lg p-4">
          <h3 class="text-sm font-semibold text-slate-700 mb-3">🔄 自动合规扫描</h3>
          <p class="text-xs text-slate-500 mb-3">对全部化学资产执行 AI 合规规则扫描，生成最新发现报告</p>
          <button
            v-permission="['admin', 'auditor']"
            @click="runAutoScan"
            :disabled="scanLoading"
            data-testid="dashboard-scan-btn"
            class="px-5 py-2 text-sm font-medium text-white bg-emerald-600 rounded hover:bg-emerald-700 disabled:opacity-50 transition-colors"
          >
            {{ scanLoading ? '扫描中…' : '触发全库扫描' }}
          </button>
          <div v-if="scanError" class="text-xs text-red-600 mt-2">{{ scanError }}</div>
          <div
            v-if="scanResult"
            data-testid="dashboard-scan-result"
            class="mt-3 p-3 bg-emerald-50 border border-emerald-200 rounded text-xs space-y-1"
          >
            <p class="font-medium text-emerald-800">✅ 扫描完成</p>
            <p class="text-slate-600">
              资产 {{ scanResult.overview.checkedAssets }}/{{ scanResult.overview.totalAssets }} · 发现
              {{ scanResult.totalFindings }} 条 · 新增 {{ scanResult.newFindings }} 条
            </p>
            <p class="text-slate-400">{{ new Date(scanResult.scannedAt).toLocaleString('zh-CN') }}</p>
          </div>
        </div>
      </div>

      <!-- ── Tab 面板: 发现 / 历史 / 报告 ── -->
      <div class="bg-white border border-slate-200 rounded-lg">
        <!-- Tab 头 -->
        <div class="flex border-b border-slate-200">
          <button
            v-for="tab in [
              { key: 'findings' as const, label: '合规发现', count: findings?.total ?? '-' },
              { key: 'history' as const, label: '巡检历史', count: history?.total ?? '-' },
              { key: 'hazard' as const, label: '隐患报告', count: hazardReport?.summary?.openFindings ?? '-' },
            ]"
            :key="tab.key"
            @click="activeTab = tab.key"
            class="px-5 py-2.5 text-xs font-medium border-b-2 transition-colors"
            :class="
              activeTab === tab.key
                ? 'border-blue-600 text-blue-700'
                : 'border-transparent text-slate-500 hover:text-slate-700'
            "
          >
            {{ tab.label }}
            <span
              class="ml-1.5 px-1.5 py-0.5 rounded text-xs"
              :class="activeTab === tab.key ? 'bg-blue-50 text-blue-600' : 'bg-slate-100 text-slate-400'"
            >
              {{ tab.count }}
            </span>
          </button>
        </div>

        <!-- 合规发现 -->
        <div v-if="activeTab === 'findings'" class="p-4">
          <div v-if="!findings?.items.length" class="text-xs text-slate-400 text-center py-8">暂无合规发现</div>
          <div v-else class="overflow-x-auto">
            <table class="w-full text-xs">
              <thead>
                <tr class="text-left text-slate-400 border-b border-slate-100">
                  <th class="pb-2 font-medium">严重级别</th>
                  <th class="pb-2 font-medium">描述</th>
                  <th class="pb-2 font-medium hidden md:table-cell">资产/位置</th>
                  <th class="pb-2 font-medium hidden md:table-cell">法规依据</th>
                  <th class="pb-2 font-medium">状态</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="f in findings.items.slice(0, 8)"
                  :key="f.findingId"
                  class="border-b border-slate-50 hover:bg-slate-50"
                >
                  <td class="py-2">
                    <span class="px-1.5 py-0.5 rounded text-xs font-medium" :class="severityColor(f.severity)">
                      {{ f.severity }}
                    </span>
                  </td>
                  <td class="py-2 text-slate-700 max-w-xs truncate" :title="f.description">
                    {{ f.description }}
                  </td>
                  <td class="py-2 text-slate-500 hidden md:table-cell">
                    {{ f.assetName }}<br /><span class="text-slate-400">{{ f.assetLocation }}</span>
                  </td>
                  <td class="py-2 text-slate-400 hidden md:table-cell">{{ f.regulationRef }}</td>
                  <td class="py-2">
                    <span class="text-slate-500">{{ f.status }}</span>
                  </td>
                </tr>
              </tbody>
            </table>
            <p v-if="findings.items.length > 8" class="text-xs text-slate-400 mt-2 text-center">
              显示前 8 条，共 {{ findings.items.length }} 条
            </p>
          </div>
        </div>

        <!-- 巡检历史 -->
        <div v-if="activeTab === 'history'" class="p-4">
          <div v-if="!history?.items.length" class="text-xs text-slate-400 text-center py-8">暂无巡检记录</div>
          <div v-else class="space-y-3">
            <div
              v-for="plan in history.items.slice(0, 5)"
              :key="plan.planId"
              class="border border-slate-100 rounded p-3 hover:border-blue-200 transition-colors"
            >
              <div class="flex items-center justify-between mb-2">
                <div>
                  <span class="text-sm font-medium text-slate-800">{{ plan.name }}</span>
                  <span
                    class="ml-2 px-1.5 py-0.5 text-xs rounded"
                    :class="
                      plan.status === 'Completed'
                        ? 'bg-emerald-50 text-emerald-600'
                        : plan.status === 'InProgress'
                          ? 'bg-blue-50 text-blue-600'
                          : 'bg-slate-100 text-slate-500'
                    "
                  >
                    {{ planStatusLabel(plan.status) }}
                  </span>
                </div>
                <span class="text-xs text-slate-400">{{ plan.inspector }} · {{ plan.area }}</span>
              </div>
              <div class="flex items-center gap-4 text-xs text-slate-500">
                <span>{{ plan.itemCount }} 检查项</span>
                <span>{{ plan.roundCount }} 轮次</span>
                <span
                  v-if="plan.rounds.length && plan.rounds[0].complianceRate != null"
                  :class="(plan.rounds[0].complianceRate ?? 0) >= 0.8 ? 'text-emerald-600' : 'text-red-600'"
                >
                  最新合规率: {{ Math.round((plan.rounds[0].complianceRate ?? 0) * 100) }}%
                </span>
                <span class="ml-auto text-slate-400">{{ new Date(plan.createdAt).toLocaleDateString('zh-CN') }}</span>
              </div>
            </div>
            <p v-if="history.items.length > 5" class="text-xs text-slate-400 text-center">
              显示最近 5 条，共 {{ history.items.length }} 条
            </p>
          </div>
        </div>

        <!-- 隐患报告 -->
        <div v-if="activeTab === 'hazard'" class="p-4">
          <div v-if="!hazardReport">
            <div class="flex items-center justify-between mb-3">
              <p class="text-xs text-slate-500">点击加载隐患报告</p>
              <button
                @click="fetchHazardReport"
                class="px-3 py-1 text-xs font-medium text-blue-600 border border-blue-200 rounded hover:bg-blue-50"
              >
                加载
              </button>
            </div>
          </div>
          <div v-else-if="!hazardReport.items.length" class="text-xs text-emerald-600 text-center py-8">
            ✅ 当前无未闭环隐患
          </div>
          <div v-else>
            <div class="flex items-center gap-3 mb-3 text-xs text-slate-500">
              <span>生成时间: {{ new Date(hazardReport.generatedAt).toLocaleString('zh-CN') }}</span>
              <span class="text-slate-300">|</span>
              <span :class="hazardReport.summary.openFindings > 0 ? 'text-red-600' : 'text-emerald-600'">
                未闭环 {{ hazardReport.summary.openFindings }} / 共 {{ hazardReport.summary.totalFindings }}
              </span>
            </div>
            <div class="space-y-2">
              <div
                v-for="item in hazardReport.items.slice(0, 10)"
                :key="item.findingId"
                class="border border-slate-100 rounded p-2.5 text-xs"
              >
                <div class="flex items-start gap-2">
                  <span
                    class="px-1.5 py-0.5 rounded text-xs font-medium shrink-0"
                    :class="severityColor(item.severity)"
                  >
                    {{ item.severity }}
                  </span>
                  <div class="flex-1 min-w-0">
                    <p class="text-slate-700 mb-1">{{ item.description }}</p>
                    <div class="flex flex-wrap gap-x-3 gap-y-0.5 text-slate-400">
                      <span v-if="item.asset">📍 {{ item.asset.name }} / {{ item.asset.location }}</span>
                      <span v-if="item.regulationRef">📋 {{ item.regulationRef }}</span>
                      <span v-if="item.remediationPlan">🔧 {{ item.remediationPlan }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <p class="text-xs text-amber-500 mt-3">{{ hazardReport.disclaimer }}</p>
          </div>
        </div>
      </div>

      <!-- ── 底部: 严重程度分布图 ── -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div class="bg-white border border-slate-200 rounded-lg p-4">
          <h3 class="text-xs font-semibold text-slate-500 uppercase mb-4">发现按严重程度分布</h3>
          <div v-if="!severityBars.length" class="text-xs text-slate-400 text-center py-6">暂无数据</div>
          <div v-else class="space-y-2">
            <div v-for="b in severityBars" :key="b.name" class="flex items-center gap-3">
              <span class="text-xs text-slate-500 w-16">{{ b.name }}</span>
              <div class="flex-1 h-5 bg-slate-100 rounded overflow-hidden">
                <div
                  class="h-full rounded transition-all duration-500"
                  :class="severityBarColor(b.name)"
                  :style="{ width: `${b.pct}%` }"
                />
              </div>
              <span class="text-xs font-mono text-slate-600 w-6 text-right">{{ b.count }}</span>
            </div>
          </div>
        </div>

        <div class="bg-white border border-slate-200 rounded-lg p-4">
          <h3 class="text-xs font-semibold text-slate-500 uppercase mb-4">发现按状态分布</h3>
          <div
            v-if="!overview.findingsByStatus || !Object.keys(overview.findingsByStatus).length"
            class="text-xs text-slate-400 text-center py-6"
          >
            暂无数据
          </div>
          <div v-else class="flex flex-wrap gap-3">
            <div
              v-for="(count, status) in overview.findingsByStatus"
              :key="status"
              class="flex items-center gap-2 px-3 py-2 bg-slate-50 rounded"
            >
              <span class="text-xs text-slate-600">{{ status }}</span>
              <span class="text-sm font-bold text-slate-800">{{ count }}</span>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
