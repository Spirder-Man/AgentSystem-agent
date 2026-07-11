<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { ChemicalAsset, HazardQueryResponse } from '@/types/api';
import { ElMessage } from 'element-plus';
import {
  ArrowLeft, Box, MapLocation, User, Warning, CircleCheck, CircleClose,
  QuestionFilled, Search,
} from '@element-plus/icons-vue';
import SkeletonCard from '@/components/common/SkeletonCard.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const route = useRoute();
const router = useRouter();
const assetId = route.params.assetId as string;

const asset = ref<ChemicalAsset | null>(null);
const loading = ref(true);
const error = ref('');

// 化学品属性查询
const propertyResult = ref<HazardQueryResponse | null>(null);
const queryingProperty = ref(false);
const propertyError = ref('');

const statusIcon = computed(() => {
  if (asset.value?.lastCheckResult === true) return CircleCheck;
  if (asset.value?.lastCheckResult === false) return CircleClose;
  return QuestionFilled;
});

const statusColor = computed(() => {
  if (asset.value?.lastCheckResult === true) return 'text-green-600';
  if (asset.value?.lastCheckResult === false) return 'text-red-600';
  return 'text-slate-400';
});

const statusLabel = computed(() => {
  if (asset.value?.lastCheckResult === true) return '合规';
  if (asset.value?.lastCheckResult === false) return '不合规';
  return '未检查';
});

async function fetchAsset() {
  loading.value = true;
  error.value = '';
  try {
    const { data } = await apiClient.get<ChemicalAsset[]>('/api/Inspection/assets');
    const found = data.find((a: ChemicalAsset) => a.assetId === assetId);
    if (!found) {
      error.value = `资产 ${assetId} 不存在`;
      return;
    }
    asset.value = found;
  } catch {
    error.value = '加载资产信息失败';
  } finally {
    loading.value = false;
  }
}

async function queryProperties() {
  if (!asset.value) return;
  queryingProperty.value = true;
  propertyError.value = '';
  propertyResult.value = null;
  try {
    const { data } = await apiClient.post<HazardQueryResponse>('/api/Compliance/hazard/query', {
      substanceName: asset.value.name,
    });
    propertyResult.value = data;
  } catch (err: unknown) {
    const axiosErr = err as { response?: { status?: number } };
    if (axiosErr.response?.status === 403) {
      propertyError.value = '需要 auditor 权限才能查询化学品属性';
    } else {
      propertyError.value = '属性查询失败';
    }
  } finally {
    queryingProperty.value = false;
  }
}

function goBack() {
  router.push('/assets');
}

onMounted(fetchAsset);
</script>

<template>
  <div class="space-y-4">
    <!-- 返回导航 -->
    <div class="flex items-center gap-2">
      <el-button :icon="ArrowLeft" size="small" text @click="goBack">返回资产列表</el-button>
    </div>

    <!-- 加载态 -->
    <SkeletonCard v-if="loading" />

    <!-- 错误态 -->
    <div v-else-if="error" class="bg-white border border-slate-200 rounded">
      <EmptyState icon="error" :title="error" @action="fetchAsset" />
    </div>

    <!-- 正常态 -->
    <template v-else-if="asset">
      <!-- 头部 -->
      <div class="bg-white border border-slate-200 rounded p-6">
        <div class="flex items-start justify-between">
          <div class="flex items-center gap-3">
            <el-icon :size="28" class="text-blue-600"><Box /></el-icon>
            <div>
              <h1 class="text-xl font-bold text-slate-900">{{ asset.name }}</h1>
              <p class="text-sm text-slate-500 font-mono">CAS: {{ asset.casNumber }}</p>
            </div>
          </div>
          <el-tag
            :type="asset.lastCheckResult === true ? 'success' : asset.lastCheckResult === false ? 'danger' : 'info'"
            size="large"
          >
            <el-icon class="mr-1"><component :is="statusIcon" :class="statusColor" /></el-icon>
            {{ statusLabel }}
          </el-tag>
        </div>
      </div>

      <!-- 资产信息 -->
      <div class="grid grid-cols-2 gap-4">
        <div class="bg-white border border-slate-200 rounded p-5">
          <h2 class="text-sm font-semibold text-slate-700 mb-4 flex items-center gap-2">
            <el-icon><MapLocation /></el-icon>
            基本信息
          </h2>
          <dl class="space-y-3 text-sm">
            <div class="flex justify-between">
              <dt class="text-slate-500">存放位置</dt>
              <dd class="text-slate-800 font-medium">{{ asset.location }}</dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-slate-500">现存量（吨）</dt>
              <dd class="text-slate-800 font-medium">{{ asset.quantityTons }}</dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-slate-500">储存条件</dt>
              <dd class="text-slate-800">{{ asset.storageCondition }}</dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-slate-500">责任人</dt>
              <dd class="text-slate-800">
                <el-icon class="mr-1"><User /></el-icon>{{ asset.responsiblePerson }}
              </dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-slate-500">重大危险源</dt>
              <dd>
                <el-tag :type="asset.isMajorHazardSource ? 'danger' : 'success'" size="small">
                  {{ asset.isMajorHazardSource ? '是' : '否' }}
                </el-tag>
              </dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-slate-500">最后检查时间</dt>
              <dd class="text-slate-800">{{ asset.lastCheckedAt || '从未检查' }}</dd>
            </div>
          </dl>
        </div>

        <!-- 化学品属性查询 -->
        <div class="bg-white border border-slate-200 rounded p-5">
          <h2 class="text-sm font-semibold text-slate-700 mb-4 flex items-center gap-2">
            <el-icon><Search /></el-icon>
            化学品属性
          </h2>

          <div v-if="!propertyResult && !propertyError" class="py-8 text-center">
            <p class="text-sm text-slate-400 mb-4">查询该化学品的危险类别和适用国标</p>
            <el-button
              type="primary"
              :loading="queryingProperty"
              @click="queryProperties"
            >
              {{ queryingProperty ? '查询中…' : '查询化学品属性' }}
            </el-button>
          </div>

          <!-- 查询结果 -->
          <div v-else-if="propertyResult" class="space-y-3">
            <div class="p-3 bg-blue-50 rounded border border-blue-100">
              <p class="text-xs text-blue-600 font-medium mb-1">工具调用</p>
              <div class="flex flex-wrap gap-1">
                <el-tag v-for="tool in propertyResult.toolsUsed" :key="tool" size="small" type="info">
                  {{ tool }}
                </el-tag>
              </div>
            </div>
            <div
              v-if="propertyResult.response"
              class="text-sm text-slate-700 leading-relaxed whitespace-pre-wrap max-h-80 overflow-y-auto"
            >
              {{ propertyResult.response }}
            </div>
            <el-button size="small" text type="primary" @click="queryProperties">
              重新查询
            </el-button>
          </div>

          <!-- 查询错误 -->
          <div v-else-if="propertyError" class="text-center py-4">
            <el-icon :size="28" class="text-slate-300 mb-2"><Warning /></el-icon>
            <p class="text-sm text-slate-500 mb-3">{{ propertyError }}</p>
            <el-button size="small" @click="queryProperties">重试</el-button>
          </div>
        </div>
      </div>

      <!-- 合规检查结果 -->
      <div class="bg-white border border-slate-200 rounded p-5">
        <h2 class="text-sm font-semibold text-slate-700 mb-4 flex items-center gap-2">
          <el-icon><CircleCheck /></el-icon>
          合规状态
        </h2>

        <div class="flex items-center gap-6">
          <div class="text-center">
            <el-icon :size="48" :class="statusColor">
              <component :is="statusIcon" />
            </el-icon>
            <p class="text-xs text-slate-500 mt-1">{{ statusLabel }}</p>
          </div>
          <div class="text-sm text-slate-600">
            <p v-if="asset.lastCheckedAt">
              最近一次合规检查于 <strong>{{ asset.lastCheckedAt }}</strong> 完成，
              结果判定为 <strong :class="statusColor">{{ statusLabel }}</strong>。
            </p>
            <p v-else>
              该资产尚未进行合规检查。点击「资产台账」页面的「自动扫描」或通过巡检计划执行检查。
            </p>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
