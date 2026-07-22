// ============================================================
// ssh-health.ts — SSH 隧道 + 远程服务健康验证工具
//
// 用于 e2e-real 的 globalSetup 和前置检查
// ============================================================

import { expect } from '@playwright/test';

declare var process: { exit(code?: number): never; };

/**
 * 通过 SSH 隧道检查远程 API 健康状态
 * 前置：SSH 隧道已建立 (localhost:15001 → 远程 API:5000)
 */
export async function checkRemoteHealth(baseURL = 'http://localhost:15001') {
  const healthUrl = `${baseURL}/health`;

  let response: Response;
  try {
    response = await fetch(healthUrl, { signal: AbortSignal.timeout(10_000) });
  } catch (e) {
    throw new Error(
      `远程 API 健康检查失败: ${healthUrl}\n` +
      `请确认: 1) SSH 隧道已运行  2) 远程 API 已启动\n` +
      `启动隧道: npm run tunnel:start\n` +
      `错误: ${e instanceof Error ? e.message : String(e)}`,
    );
  }

  if (!response.ok) {
    throw new Error(`远程 API 返回 ${response.status}: ${response.statusText}`);
  }

  const health = await response.json() as Record<string, unknown>;

  // 验证关键字段
  const status = health.status as string;
  const dbStatus = health.database as string;
  const llmStatus = health.llm as string;
  const docs = health.knowledge_base_docs as number;

  const issues: string[] = [];
  if (dbStatus !== 'connected') issues.push(`数据库未连接: ${dbStatus}`);
  if (llmStatus !== 'reachable') issues.push(`LLM 不可达: ${llmStatus}`);
  if (typeof docs === 'number' && docs <= 0) issues.push(`知识库文档数为 ${docs}`);

  if (issues.length > 0) {
    throw new Error(`远程服务健康检查不通过:\n  ${issues.join('\n  ')}`);
  }

  return {
    status,
    dbStatus,
    llmStatus,
    docs,
    healthy: true,
  };
}

/**
 * 获取数据库统计信息（用于断言基准）
 */
export async function getDbStats(baseURL = 'http://localhost:15001') {
  try {
    const response = await fetch(`${baseURL}/api/Compliance/summary`, {
      signal: AbortSignal.timeout(10_000),
    });
    if (response.ok) {
      return await response.json() as Record<string, unknown>;
    }
  } catch {
    console.warn('[SSH-HEALTH] 无法获取 DB 统计信息');
  }
  return null;
}

/**
 * Playwright globalSetup: 在测试套件启动前验证远程环境
 */
export default async function globalSetup() {
  console.log('\n[E2E-REAL] 验证远程环境...\n');
  try {
    const health = await checkRemoteHealth();
    console.log(`  API状态: ${health.status}`);
    console.log(`  数据库: ${health.dbStatus}`);
    console.log(`  LLM: ${health.llmStatus}`);
    console.log(`  知识库文档: ${health.docs}`);
    console.log('\n[E2E-REAL] 远程环境就绪，开始执行真实 GPU E2E 测试\n');
  } catch (e) {
    console.error('\n[E2E-REAL] 远程环境检查失败!');
    console.error((e as Error).message);
    console.error('\n请先运行: npm run tunnel:start');
    process.exit(1);
  }
}
