<script setup lang="ts">
import { ref, onUnmounted } from 'vue';
import { evalApi } from '@/api';
import type { EvalReport } from '@/types/api';
import { ElMessage } from 'element-plus';

const taskId = ref('');
const status = ref('');
const progress = ref('');
const report = ref<EvalReport | null>(null);
const running = ref(false);
const error = ref('');
let pollTimer: ReturnType<typeof setInterval> | null = null;

async function startEval() {
  error.value = '';
  report.value = null;
  taskId.value = '';
  status.value = '';
  progress.value = '';
  running.value = true;
  try {
    const data = await evalApi.run();
    taskId.value = data.taskId;
    ElMessage.success(data.message);
    startPolling();
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '启动评测失败';
    running.value = false;
  }
}

function startPolling() {
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = setInterval(pollStatus, 2000);
  pollStatus();
}

async function pollStatus() {
  if (!taskId.value) return;
  try {
    const data = await evalApi.getStatus(taskId.value);
    status.value = data.status;
    progress.value = data.progress;
    if (data.report) {
      report.value = data.report;
      stopPolling();
      running.value = false;
      ElMessage.success('评测完成');
    }
    if (data.status === 'failed') {
      stopPolling();
      running.value = false;
      ElMessage.error('评测失败');
    }
  } catch {
    /* polling will retry */
  }
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

async function deleteTask() {
  if (!taskId.value) return;
  try {
    await evalApi.cancel(taskId.value);
    ElMessage.success('任务已删除');
    resetState();
  } catch {
    ElMessage.error('删除失败');
  }
}

function resetState() {
  stopPolling();
  taskId.value = '';
  status.value = '';
  progress.value = '';
  report.value = null;
  running.value = false;
  error.value = '';
}

onUnmounted(stopPolling);

function formatPct(v: number) {
  return (v * 100).toFixed(1) + '%';
}
</script>

<template>
  <div class="space-y-6 max-w-5xl">
    <h1 class="text-xl font-bold text-slate-900">合规评测</h1>
    <p class="text-xs text-slate-500 -mt-4">对 50 条合规测试用例进行 Function Calling 准确性评测</p>

    <!-- 控制区 -->
    <div class="bg-white border border-slate-200 rounded p-4">
      <div class="flex items-center gap-4 flex-wrap">
        <button
          @click="startEval"
          :disabled="running"
          data-testid="eval-start-btn"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >
          {{ running ? '评测运行中…' : '🚀 启动评测' }}
        </button>
        <button v-if="taskId" @click="deleteTask" class="text-xs text-slate-400 hover:text-red-500">删除任务</button>
      </div>
      <div v-if="error" class="mt-3 bg-red-50 border border-red-200 rounded p-3 text-sm text-red-700">{{ error }}</div>

      <!-- 进度 -->
      <div v-if="taskId" class="mt-4 p-3 bg-slate-50 border border-slate-200 rounded">
        <div class="flex items-center justify-between text-xs mb-2">
          <span class="font-mono text-slate-500">Task: {{ taskId }}</span>
          <span
            :class="status === 'completed' ? 'text-green-600' : status === 'failed' ? 'text-red-600' : 'text-blue-600'"
            class="font-medium"
            >{{ status }}</span
          >
        </div>
        <div v-if="status === 'running'" class="text-xs text-slate-500">{{ progress }}</div>
      </div>
    </div>

    <!-- 评测结果 -->
    <div v-if="report" data-testid="eval-report" class="bg-white border border-slate-200 rounded p-5 space-y-5">
      <h2 class="text-lg font-bold text-slate-800">评测报告</h2>
      <div class="grid grid-cols-2 sm:grid-cols-5 gap-3 text-xs">
        <div class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">模型</div>
          <div class="text-slate-700 font-mono">{{ report.model }}</div>
        </div>
        <div class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">用例数</div>
          <div class="text-slate-700 font-bold text-lg">{{ report.total }}</div>
        </div>
        <div class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">工具调用率</div>
          <div :class="report.toolCallRate >= 0.8 ? 'text-green-600' : 'text-red-600'" class="font-bold text-lg">
            {{ formatPct(report.toolCallRate) }}
          </div>
        </div>
        <div class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">参数准确率</div>
          <div :class="report.parameterAccuracy >= 0.8 ? 'text-green-600' : 'text-red-600'" class="font-bold text-lg">
            {{ formatPct(report.parameterAccuracy) }}
          </div>
        </div>
        <div class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">结论准确率</div>
          <div :class="report.conclusionAccuracy >= 0.8 ? 'text-green-600' : 'text-red-600'" class="font-bold text-lg">
            {{ formatPct(report.conclusionAccuracy) }}
          </div>
        </div>
        <div v-if="report.casesCount != null" class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">用例数</div>
          <div class="text-slate-700 font-bold text-lg">{{ report.casesCount }}</div>
        </div>
        <div v-if="report.casesWithErrors != null" class="bg-slate-50 rounded p-3 text-center">
          <div class="text-slate-400 mb-1">异常用例</div>
          <div :class="report.casesWithErrors === 0 ? 'text-green-600' : 'text-red-600'" class="font-bold text-lg">
            {{ report.casesWithErrors }}
          </div>
        </div>
      </div>

      <!-- cases 详细结果 -->
      <details v-if="report.cases?.length" class="text-xs">
        <summary class="cursor-pointer text-slate-500 hover:text-slate-700 font-medium">
          查看 {{ report.cases.length }} 条详细结果
        </summary>
        <div class="mt-3 space-y-2 max-h-96 overflow-y-auto">
          <div
            v-for="(c, i) in report.cases"
            :key="i"
            class="p-2.5 rounded border text-xs"
            :class="c.error ? 'border-red-200 bg-red-50' : 'border-slate-200 bg-slate-50'"
          >
            <div class="flex items-center gap-2 mb-1">
              <span class="text-slate-400 font-mono">#{{ i + 1 }}</span>
              <span v-if="c.error" class="text-red-600">⚠ {{ c.error }}</span>
              <span v-else class="text-slate-700">{{ c.query }}</span>
              <span class="ml-auto flex gap-1">
                <span :class="c.toolMatch ? 'text-green-500' : 'text-red-500'" class="text-[10px]">🔧</span>
                <span :class="c.paramMatch ? 'text-green-500' : 'text-red-500'" class="text-[10px]">📋</span>
                <span :class="c.conclusionMatch ? 'text-green-500' : 'text-red-500'" class="text-[10px]">✅</span>
              </span>
            </div>
            <div class="text-[11px] text-slate-500">
              期望: {{ c.expectedTools.join(', ') }} → 实际: {{ c.actualTools.join(', ') || '无' }}
            </div>
          </div>
        </div>
      </details>
    </div>
  </div>
</template>
