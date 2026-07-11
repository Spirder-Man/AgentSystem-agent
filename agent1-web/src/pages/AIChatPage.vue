<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue';
import apiClient from '@/lib/axios';
import type { ComplianceResponse } from '@/types/api';
import EmptyState from '@/components/common/EmptyState.vue';
import { useLoadingBar } from '@/lib/useLoadingBar';

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolsUsed?: string[];
  timestamp: number;
}

const messages = ref<ChatMessage[]>([]);
const inputText = ref('');
const loading = ref(false);
const error = ref('');
const chatContainer = ref<HTMLElement | null>(null);
const { start, stop } = useLoadingBar();

function genId() { return Date.now().toString(36) + Math.random().toString(36).slice(2, 8); }

async function scrollToBottom() {
  await nextTick();
  if (chatContainer.value) {
    chatContainer.value.scrollTop = chatContainer.value.scrollHeight;
  }
}

async function sendMessage() {
  const text = inputText.value.trim();
  if (!text || loading.value) return;
  error.value = '';

  const userMsg: ChatMessage = { id: genId(), role: 'user', content: text, timestamp: Date.now() };
  messages.value.push(userMsg);
  inputText.value = '';
  await scrollToBottom();

  loading.value = true;
  start('AI 正在思考…');
  try {
    const { data } = await apiClient.post<ComplianceResponse>('/api/Compliance/check', { query: text });
    const assistantMsg: ChatMessage = {
      id: genId(),
      role: 'assistant',
      content: data.response || '（未收到有效响应）',
      toolsUsed: data.toolsUsed,
      timestamp: Date.now(),
    };
    messages.value.push(assistantMsg);
  } catch (e: unknown) {
    const ae = e as { response?: { data?: { error?: string } } };
    error.value = ae.response?.data?.error || '请求失败，请稍后重试';
  } finally { loading.value = false; stop(); await scrollToBottom(); }
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
}

const suggestions = [
  '苯和丙酮能放在同一个仓库吗',
  '硝酸应该如何储存',
  '甲类仓库与明火点的安全距离',
  '化工企业三级标准化要求',
  '危化品重大危险源辨识标准',
];

function useSuggestion(s: string) { inputText.value = s; sendMessage(); }

function clearChat() { messages.value = []; error.value = ''; }

onMounted(() => { inputText.value = ''; });
</script>

<template>
  <div class="flex flex-col h-[calc(100vh-8rem)] max-w-3xl mx-auto">
    <div class="flex items-center justify-between mb-3 shrink-0">
      <div>
        <h1 class="text-xl font-bold text-slate-900">AI 合规助手</h1>
        <p class="text-xs text-slate-500">基于化工行业知识库的智能问答，支持法规查询、合规审核、风险分析</p>
      </div>
      <button
        v-if="messages.length > 0"
        @click="clearChat"
        class="text-xs px-3 py-1.5 border border-slate-200 rounded text-slate-500 hover:bg-slate-50 transition-colors"
      >清空对话</button>
    </div>

    <!-- 消息区域 -->
    <div ref="chatContainer" class="flex-1 overflow-y-auto space-y-4 mb-4 pr-1">
      <EmptyState
        v-if="messages.length === 0 && !loading"
        icon="search"
        title="开始与 AI 合规助手对话"
        description="可以询问化工法规、危险品储存、合规审核等问题"
      />

      <div v-for="msg in messages" :key="msg.id" class="flex" :class="msg.role === 'user' ? 'justify-end' : 'justify-start'">
        <div class="max-w-[85%]">
          <div
            :class="msg.role === 'user'
              ? 'bg-blue-600 text-white rounded-br-sm'
              : 'bg-white border border-slate-200 text-slate-700 rounded-bl-sm'"
            class="px-4 py-2.5 rounded-xl text-sm whitespace-pre-wrap leading-relaxed"
          >{{ msg.content }}</div>
          <div class="flex items-center gap-2 mt-1 px-1" :class="msg.role === 'user' ? 'justify-end' : 'justify-start'">
            <span class="text-xs text-slate-400">{{ new Date(msg.timestamp).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }) }}</span>
            <span v-if="msg.toolsUsed && msg.toolsUsed.length > 0" class="text-xs text-slate-400">· {{ msg.toolsUsed.join(' → ') }}</span>
          </div>
        </div>
      </div>

      <!-- 加载提示 -->
      <div v-if="loading" class="flex justify-start">
        <div class="bg-white border border-slate-200 rounded-xl rounded-bl-sm px-4 py-3">
          <div class="flex items-center gap-2">
            <span class="flex gap-1">
              <span class="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style="animation-delay:0ms" />
              <span class="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style="animation-delay:150ms" />
              <span class="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style="animation-delay:300ms" />
            </span>
            <span class="text-xs text-slate-400">AI 分析中…</span>
          </div>
        </div>
      </div>

      <!-- 错误提示 -->
      <div v-if="error" class="flex justify-center">
        <div class="bg-red-50 border border-red-200 rounded px-4 py-2 text-sm text-red-700">{{ error }}</div>
      </div>
    </div>

    <!-- 快捷建议 -->
    <div v-if="messages.length === 0" class="flex gap-2 mb-3 flex-wrap shrink-0">
      <button
        v-for="s in suggestions" :key="s"
        @click="useSuggestion(s)"
        :disabled="loading"
        class="text-xs px-3 py-1.5 border border-slate-200 rounded-full text-slate-500 hover:bg-slate-50 hover:border-slate-300 transition-colors disabled:opacity-50"
      >{{ s }}</button>
    </div>

    <!-- 输入区域 -->
    <div class="shrink-0 bg-white border border-slate-200 rounded-xl p-3">
      <div class="flex gap-2">
        <textarea
          v-model="inputText"
          @keydown="handleKeydown"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm border-0 resize-none focus:outline-none bg-transparent min-h-[40px] max-h-[120px]"
          placeholder="输入合规问题… (Enter 发送，Shift+Enter 换行)"
          rows="1"
        />
        <button
          @click="sendMessage"
          :disabled="loading || !inputText.trim()"
          class="px-5 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors self-end"
        >发送</button>
      </div>
    </div>
  </div>
</template>
