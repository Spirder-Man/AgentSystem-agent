// ============================================================
// global-setup.ts — Playwright Global Setup for Real E2E
//
// 在所有测试开始前:
//   1. 登录获取 admin JWT token
//   2. 检查并补种测试数据（巡检计划、知识库加载）
//   3. 验证 Dashboard API 可达
//
// 运行时机: webServer (Vite) 启动后 → globalSetup → 测试执行
// ============================================================

/// <reference types="node" />

import { type FullConfig } from '@playwright/test';

const ACCOUNT = { username: 'admin', password: '7758521' };

interface ApiClient {
  get: (url: string) => Promise<{ status: number; data: unknown }>;
  post: (url: string, body?: unknown) => Promise<{ status: number; data: unknown }>;
}

function createApiClient(baseURL: string, token: string): ApiClient {
  const headers = {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
  };

  return {
    get: async (url: string) => {
      const res = await fetch(`${baseURL}${url}`, { headers: { Authorization: `Bearer ${token}` } });
      const data = await res.json().catch(() => null);
      return { status: res.status, data };
    },
    post: async (url: string, body?: unknown) => {
      const res = await fetch(`${baseURL}${url}`, {
        method: 'POST',
        headers,
        body: body ? JSON.stringify(body) : undefined,
      });
      const data = await res.json().catch(() => null);
      return { status: res.status, data };
    },
  };
}

async function globalSetup(_config: FullConfig) {
  const baseURL = process.env.VITE_PROXY_TARGET || 'http://localhost:5173';
  console.log('\n🌱 [global-setup] 预置 E2E 测试数据...');
  console.log(`   API Base: ${baseURL}\n`);

  // 1. 登录获取 token
  console.log('   [auth] 登录...');
  const loginRes = await fetch(`${baseURL}/api/Auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(ACCOUNT),
  });
  const loginData = (await loginRes.json()) as Record<string, unknown>;
  if (!loginData?.token) {
    console.error('   [auth] ❌ 登录失败');
    return; // 不阻塞测试
  }
  console.log(`   [auth] ✅ token 获取成功 (${loginData.role})`);
  const api = createApiClient(baseURL, loginData.token as string);

  // 2. 检查巡检计划
  console.log('   [inspection.plans] 检查巡检计划...');
  try {
    const { data } = await api.get('/api/Inspection/plans');
    const count = Array.isArray(data) ? data.length : 0;
    if (count >= 1) {
      console.log(`   [inspection.plans] ✅ 已有 ${count} 条计划`);
    } else {
      console.log('   [inspection.plans] ⚠️ 无计划，自动创建...');
      const { data: created } = await api.post('/api/Inspection/plans', {
        name: 'E2E测试-甲类仓库周检',
        type: 'DailyWeekly',
        area: '甲类仓库A区',
        items: [
          { query: '苯储存条件是否合规', capability: 'storage-compliance' },
          { query: '仓库防火间距是否满足GB 15603要求', capability: 'safety-distance' },
        ],
        notes: 'E2E测试自动创建',
      });
      console.log(`   [inspection.plans] ✅ 已创建: ${(created as Record<string, unknown>)?.planId ?? 'unknown'}`);
    }
  } catch (err) {
    console.error(`   [inspection.plans] ⚠️ ${(err as Error).message}`);
  }

  // 3. 知识库增量加载
  console.log('   [knowledge] 触发增量加载...');
  try {
    await api.post('/api/KnowledgeBase/incremental-load');
    console.log('   [knowledge] ✅ 增量加载请求已发送');
  } catch (err) {
    console.error(`   [knowledge] ⚠️ ${(err as Error).message}`);
  }

  // 4. 验证 Dashboard
  console.log('   [dashboard] 验证 Dashboard API...');
  try {
    const { status, data } = await api.get('/api/Dashboard/overview');
    console.log(
      `   [dashboard] ${status < 400 ? '✅' : '⚠️'} totalAssets=${(data as Record<string, unknown>)?.totalAssets ?? 'N/A'}`,
    );
  } catch (err) {
    console.error(`   [dashboard] ⚠️ ${(err as Error).message}`);
  }

  console.log('\n✅ [global-setup] 数据预置完成\n');
}

export default globalSetup;
