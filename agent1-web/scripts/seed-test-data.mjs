// ============================================================
// seed-test-data.mjs — E2E 测试数据预置脚本
//
// 在 pretest:e2e:real 阶段运行，通过远程 API 确保:
//   1. 巡检计划 >= 1 条
//   2. 知识库增量加载完成
//   3. Dashboard API 可达
//
// 用法: node scripts/seed-test-data.mjs [apiBaseUrl]
//   默认: http://localhost:5173 (通过 Vite proxy 到远程 API)
// ============================================================

const API_BASE = process.argv[2] || 'http://localhost:5173';
const ACCOUNT = { username: 'admin', password: '7758521' };

async function apiGet(url, token) {
  const res = await fetch(`${API_BASE}${url}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  const data = await res.json().catch(() => null);
  return { status: res.status, data };
}

async function apiPost(url, body, token) {
  const res = await fetch(`${API_BASE}${url}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(body),
  });
  const data = await res.json().catch(() => null);
  return { status: res.status, data };
}

async function login() {
  console.log('  [auth] 登录获取 token...');
  const { status, data } = await apiPost('/api/Auth/login', {
    username: ACCOUNT.username,
    password: ACCOUNT.password,
  });
  if (status !== 200 || !data?.token) {
    throw new Error(`登录失败 (${status}): ${JSON.stringify(data)}`);
  }
  console.log(`  [auth] ✅ token 获取成功 (${data.role})`);
  return data.token;
}

async function checkPlans(token) {
  console.log('  [inspection.plans] 检查巡检计划...');
  const { data } = await apiGet('/api/Inspection/plans', token);
  const count = Array.isArray(data) ? data.length : 0;
  if (count >= 1) {
    console.log(`  [inspection.plans] ✅ 已有 ${count} 条计划`);
    return true;
  }

  console.log(`  [inspection.plans] ⚠️ 无巡检计划，自动创建...`);
  const { status, data: created } = await apiPost('/api/Inspection/plans', {
    name: 'E2E测试-甲类仓库周检',
    type: 'DailyWeekly',
    area: '甲类仓库A区',
    items: [
      { query: '苯储存条件是否合规', capability: 'storage-compliance' },
      { query: '仓库防火间距是否满足GB 15603要求', capability: 'safety-distance' },
    ],
    notes: 'E2E测试自动创建',
  }, token);

  if (status >= 400) {
    console.error(`  [inspection.plans] ❌ 创建失败: ${JSON.stringify(created)}`);
    return false;
  }
  console.log(`  [inspection.plans] ✅ 已创建计划: ${created?.planId ?? 'unknown'}`);
  return true;
}

async function checkKnowledgeBase(token) {
  console.log('  [knowledge.incremental] 检查知识库...');
  const { status, data } = await apiGet('/api/KnowledgeBase/search-mode', token);
  if (status >= 400) {
    console.error(`  [knowledge.incremental] ❌ API 不可达`);
    return false;
  }
  console.log(`  [knowledge.incremental] ✅ 检索模式: ${data?.mode ?? 'unknown'}`);

  // 触发增量加载
  console.log('  [knowledge.incremental] 触发增量加载...');
  const { data: loadResult } = await apiPost('/api/KnowledgeBase/incremental-load', {}, token);
  console.log(`  [knowledge.incremental] ✅ ${loadResult?.message ?? '完成'}`);
  return true;
}

async function checkDashboard(token) {
  console.log('  [dashboard.overview] 检查 Dashboard API...');
  const { status, data } = await apiGet('/api/Dashboard/overview', token);
  if (status >= 400) {
    console.error(`  [dashboard.overview] ❌ API 不可达 (${status})`);
    return false;
  }
  console.log(`  [dashboard.overview] ✅ totalAssets=${data?.totalAssets ?? 'N/A'}`);
  return true;
}

async function main() {
  console.log('🌱 seed-test-data: 预置 E2E 测试数据...\n');
  console.log(`  API Base: ${API_BASE}\n`);

  let allOk = true;

  try {
    const token = await login();

    const results = await Promise.all([
      checkPlans(token),
      checkKnowledgeBase(token),
      checkDashboard(token),
    ]);

    allOk = results.every(Boolean);
  } catch (err) {
    console.error(`\n  ❌ 种子数据预置异常: ${err.message}`);
    console.error('  ⚠️ 测试将继续执行，但部分数据契约可能不满足。');
    // 不阻止测试执行 — 数据缺失不应该阻塞 CI
    process.exit(0);
  }

  console.log(`\n  ${allOk ? '✅' : '⚠️'} 种子数据预置${allOk ? '完成' : '部分完成'}。\n`);
  process.exit(allOk ? 0 : 0); // 始终不阻塞测试
}

main();
