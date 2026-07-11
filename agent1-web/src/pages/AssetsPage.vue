<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import apiClient from '@/lib/axios';
import type { ChemicalAsset } from '@/types/api';
import SkeletonTable from '@/components/common/SkeletonTable.vue';
import EmptyState from '@/components/common/EmptyState.vue';

const assets = ref<ChemicalAsset[]>([]);
const loading = ref(true);
const error = ref('');
const searchQuery = ref('');

const router = useRouter();

async function fetchAssets() {
  loading.value = true; error.value = '';
  try { const { data } = await apiClient.get<ChemicalAsset[]>('/api/Inspection/assets'); assets.value = data; }
  catch { error.value = '加载失败'; }
  finally { loading.value = false; }
}

const filteredAssets = computed(() => {
  if (!searchQuery.value.trim()) return assets.value;
  const q = searchQuery.value.toLowerCase();
  return assets.value.filter(a =>
    a.name.toLowerCase().includes(q) ||
    a.casNumber.includes(q) ||
    a.location.toLowerCase().includes(q) ||
    a.responsiblePerson.toLowerCase().includes(q)
  );
});

onMounted(fetchAssets);
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-bold text-slate-900">资产台账</h1>
      <span class="text-xs text-slate-400" v-if="assets.length">共 {{ assets.length }} 种</span>
    </div>

    <div class="bg-white border border-slate-200 rounded px-4 py-2">
      <input
        v-model="searchQuery"
        placeholder="搜索名称 / CAS号 / 位置 / 负责人…"
        class="w-full text-sm py-1.5 focus:outline-none text-slate-700 placeholder-slate-300"
      />
    </div>

    <SkeletonTable v-if="loading" :rows="5" />
    <EmptyState v-else-if="error" icon="error" :title="error" @action="fetchAssets" />
    <EmptyState v-else-if="filteredAssets.length === 0" icon="empty" :title="searchQuery ? '无匹配结果' : '暂无资产数据'" />

    <div v-else class="bg-white border border-slate-200 rounded overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b border-slate-200 bg-slate-50">
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500">名称</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">CAS号</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-36">位置</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">存量(t)</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">储存条件</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-20">负责人</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-28">重大危险源</th>
            <th class="text-left px-4 py-2 text-xs font-medium text-slate-500 w-24">最近检查</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="a in filteredAssets" :key="a.assetId" class="border-b border-slate-100 hover:bg-slate-50">
            <td class="px-4 py-3 text-slate-800 font-medium">
              <span
                class="cursor-pointer text-blue-700 hover:text-blue-500 hover:underline"
                @click="router.push(`/assets/${a.assetId}`)"
              >{{ a.name }}</span>
            </td>
            <td class="px-4 py-3 font-mono text-xs text-slate-500">{{ a.casNumber }}</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ a.location }}</td>
            <td class="px-4 py-3 text-xs text-slate-700">{{ a.quantityTons }}</td>
            <td class="px-4 py-3 text-xs text-slate-500 max-w-32 truncate" :title="a.storageCondition">{{ a.storageCondition }}</td>
            <td class="px-4 py-3 text-xs text-slate-500">{{ a.responsiblePerson }}</td>
            <td class="px-4 py-3">
              <span v-if="a.isMajorHazardSource" class="text-xs px-1.5 py-0.5 rounded border border-red-200 text-red-700 bg-red-50">是</span>
              <span v-else class="text-xs px-1.5 py-0.5 rounded border border-slate-200 text-slate-500 bg-slate-50">否</span>
            </td>
            <td class="px-4 py-3">
              <span v-if="a.lastCheckResult === true" class="text-xs text-green-600">✓ 合规</span>
              <span v-else-if="a.lastCheckResult === false" class="text-xs text-red-600">✗ 不合规</span>
              <span v-else class="text-xs text-slate-300">未检查</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
