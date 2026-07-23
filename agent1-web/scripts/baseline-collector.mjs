// ============================================================
// baseline-collector.mjs — LLM 质量基线离线采集
//
// 对 baseline.json 中的每个场景调用远程 LLM 3-5 次，
// 收集实际输出文本、关键词和耗时，供人工审核后更新基线。
//
// 用法: node scripts/baseline-collector.mjs [apiBaseUrl] [runs]
//   apiBaseUrl: 默认 http://localhost:5173
//   runs: 每个场景的采集次数，默认 3
// ============================================================

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = fileURLToPath(new URL('.', import.meta.url));
const root = join(__dirname, '..');

const API_BASE = process.argv[2] || 'http://localhost:5173';
const RUNS = parseInt(process.argv[3] || '3', 10);
const ACCOUNT = { username: 'admin', password: '7758521' };
const BASELINE_PATH = join(root, 'e2e-real', 'baseline.json');
const OUTPUT_DIR = join(root, 'e2e-real', 'baseline-reports');

async function apiPost(url, body, token) {
  const res = await fetch(`${API_BASE}${url}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(body),
  });
  const data = await res.json().catch(() => null);
  if (res.status !== 200) throw new Error(`API ${res.status}: ${JSON.stringify(data)}`);
  return data;
}

async function login() {
  const res = await fetch(`${API_BASE}/api/Auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(ACCOUNT),
  });
  const data = await res.json();
  if (!data?.token) throw new Error('登录失败');
  return data.token;
}

async function collectScenario(scenarioName, query, token) {
  const results = [];
  for (let i = 0; i < RUNS; i++) {
    console.log(`    轮次 ${i + 1}/${RUNS}...`);
    const startTime = Date.now();
    const data = await apiPost('/api/Compliance/check', { query }, token);
    const elapsed = Date.now() - startTime;

    const text = data?.response || '';
    results.push({
      run: i + 1,
      elapsedMs: elapsed,
      textLength: text.length,
      textPreview: text.substring(0, 300),
      fullText: text,
    });
  }

  // 汇总
  const times = results.map(r => r.elapsedMs);
  const lengths = results.map(r => r.textLength);
  const minTime = Math.min(...times);
  const maxTime = Math.max(...times);
  const avgTime = Math.round(times.reduce((a, b) => a + b, 0) / times.length);
  const minLen = Math.min(...lengths);
  const maxLen = Math.max(...lengths);
  const avgLen = Math.round(lengths.reduce((a, b) => a + b, 0) / lengths.length);

  return {
    scenario: scenarioName,
    query,
    runs: results,
    summary: {
      avgTimeMs: avgTime,
      minTimeMs: minTime,
      maxTimeMs: maxTime,
      avgLength: avgLen,
      minLength: minLen,
      maxLength: maxLen,
    },
  };
}

async function main() {
  console.log('📊 baseline-collector: LLM 质量基线采集\n');
  console.log(`  API Base: ${API_BASE}`);
  console.log(`  每场景采集次数: ${RUNS}\n`);

  // 读取 baseline.json 获取场景列表
  if (!existsSync(BASELINE_PATH)) {
    console.error('  ❌ 未找到 baseline.json');
    process.exit(1);
  }

  const baseline = JSON.parse(readFileSync(BASELINE_PATH, 'utf-8'));
  const scenarios = Object.entries(baseline.scenarios);

  if (scenarios.length === 0) {
    console.log('  ⚠️ baseline.json 中无场景定义');
    process.exit(0);
  }

  // 登录
  console.log('  [auth] 登录...');
  const token = await login();
  console.log('  [auth] ✅\n');

  // 采集每个场景
  const allResults = [];
  for (const [name, sc] of scenarios) {
    console.log(`  📝 [${name}] "${sc.query}"`);
    try {
      const result = await collectScenario(name, sc.query, token);
      allResults.push(result);
      console.log(`     ⏱️ 耗时: ${result.summary.minTimeMs}-${result.summary.maxTimeMs}ms (avg ${result.summary.avgTimeMs}ms)`);
      console.log(`     📏 长度: ${result.summary.minLength}-${result.summary.maxLength} chars (avg ${result.summary.avgLength})\n`);
    } catch (err) {
      console.error(`     ❌ 采集失败: ${err.message}\n`);
      allResults.push({ scenario: name, query: sc.query, error: err.message });
    }
  }

  // 保存报告
  const reportPath = join(OUTPUT_DIR, `baseline-report-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.json`);
  if (!existsSync(OUTPUT_DIR)) {
    const { mkdirSync } = await import('node:fs');
    mkdirSync(OUTPUT_DIR, { recursive: true });
  }
  writeFileSync(reportPath, JSON.stringify(allResults, null, 2), 'utf-8');
  console.log(`\n  ✅ 报告已保存: ${reportPath}`);
  console.log('  📋 请人工审核报告后更新 baseline.json 中的 commonKeywords/rareKeywords/qualityFloor。\n');
}

main().catch(err => {
  console.error('baseline-collector 执行失败:', err.message);
  process.exit(1);
});
