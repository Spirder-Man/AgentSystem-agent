// ============================================================
// data-manifest.ts — 数据契约声明
//
// 每个测试模块声明其数据依赖，pretest 阶段自动检查 + 补种。
// 通过远程 API 创建测试数据，不依赖"运气好库里刚好有"。
//
// 使用方式:
//   import { ensureAll } from './fixtures/data-manifest';
//   await ensureAll(baseURL, authToken);
// ============================================================

export interface DataContract {
  /** 模块名称，用于日志 */
  name: string;
  /** 检查数据是否满足最低要求 */
  verify: (api: ApiClient) => Promise<{ ok: boolean; detail: string }>;
  /** 补种数据（仅在 verify 失败时调用） */
  seed: (api: ApiClient) => Promise<string>;
}

/** 简化的 API 调用接口 */
export interface ApiClient {
  get: (url: string) => Promise<{ status: number; data: unknown }>;
  post: (url: string, body?: unknown) => Promise<{ status: number; data: unknown }>;
}

// ── 数据契约定义 ──

export const DATA_CONTRACTS: DataContract[] = [
  // ── 巡检模块：至少需要 1 条巡检计划 ──
  {
    name: 'inspection.plans',
    verify: async (api) => {
      const { data } = await api.get('/api/Inspection/plans');
      const plans = (data as unknown[]) ?? [];
      const count = Array.isArray(plans) ? plans.length : 0;
      return {
        ok: count >= 1,
        detail: `巡检计划: ${count} 条 (需要 >= 1)`,
      };
    },
    seed: async (api) => {
      // 创建一条基本巡检计划
      const { data, status } = await api.post('/api/Inspection/plans', {
        name: 'E2E测试-甲类仓库周检',
        type: 'DailyWeekly',
        area: '甲类仓库A区',
        items: [
          { query: '苯储存条件是否合规', capability: 'storage-compliance' },
          { query: '仓库防火间距是否满足GB 15603要求', capability: 'safety-distance' },
        ],
        notes: 'E2E测试自动创建的巡检计划',
      });
      if (status >= 400) throw new Error(`创建巡检计划失败: ${JSON.stringify(data)}`);
      return `已创建巡检计划: ${(data as Record<string, unknown>)?.planId ?? 'unknown'}`;
    },
  },

  // ── 知识库：确保有文档可用（通过增量加载触发） ──
  {
    name: 'knowledge.incremental',
    verify: async (api) => {
      const { data } = await api.get('/api/KnowledgeBase/search-mode');
      // 知识库 API 可访问即视为就绪
      const hasMode = typeof (data as Record<string, unknown>)?.mode === 'string';
      return {
        ok: hasMode,
        detail: hasMode ? '知识库 API 可达' : '知识库 API 不可达',
      };
    },
    seed: async (api) => {
      // 触发增量加载确保知识库有最新文档
      const { data, status } = await api.post('/api/KnowledgeBase/incremental-load');
      if (status >= 400) throw new Error(`知识库增量加载失败: ${JSON.stringify(data)}`);
      const d = data as Record<string, unknown>;
      return `知识库增量加载完成: ${d.documentCountBefore ?? '?'} → ${d.documentCountAfter ?? '?'} 文档`;
    },
  },

  // ── Dashboard：确保 overview API 可达 ──
  {
    name: 'dashboard.overview',
    verify: async (api) => {
      const { status, data } = await api.get('/api/Dashboard/overview');
      const hasTotal = typeof (data as Record<string, unknown>)?.totalAssets === 'number';
      return {
        ok: status < 400 && hasTotal,
        detail: `Dashboard overview ${status < 400 ? '可达' : '不可达'}, totalAssets=${(data as Record<string, unknown>)?.totalAssets ?? 'N/A'}`,
      };
    },
    seed: async (_api) => {
      // Dashboard 数据由系统自动生成，无需手动播种
      return 'Dashboard 数据依赖系统自动生成，无需手动播种';
    },
  },
];

// ── 编排器 ──

export interface EnsureResult {
  contract: string;
  ok: boolean;
  detail: string;
  seeded: boolean;
  seedMessage?: string;
}

/**
 * 按顺序检查所有数据契约，不满足则自动播种。
 * 注意：seed 操作的 API 调用可能需要 Auditor 或 Admin 权限。
 */
export async function ensureAll(api: ApiClient): Promise<EnsureResult[]> {
  const results: EnsureResult[] = [];

  for (const contract of DATA_CONTRACTS) {
    console.log(`  [${contract.name}] 检查中...`);
    try {
      const { ok, detail } = await contract.verify(api);
      if (ok) {
        console.log(`  [${contract.name}] ✅ ${detail}`);
        results.push({ contract: contract.name, ok: true, detail, seeded: false });
      } else {
        console.log(`  [${contract.name}] ⚠️ ${detail} → 自动补种...`);
        try {
          const seedMsg = await contract.seed(api);
          console.log(`  [${contract.name}] ✅ ${seedMsg}`);
          results.push({ contract: contract.name, ok: true, detail, seeded: true, seedMessage: seedMsg });
        } catch (seedErr) {
          const msg = seedErr instanceof Error ? seedErr.message : String(seedErr);
          console.error(`  [${contract.name}] ❌ 补种失败: ${msg}`);
          results.push({ contract: contract.name, ok: false, detail: `${detail} | 补种失败: ${msg}`, seeded: true });
        }
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error(`  [${contract.name}] ❌ 检查失败: ${msg}`);
      results.push({ contract: contract.name, ok: false, detail: `检查异常: ${msg}`, seeded: false });
    }
  }

  return results;
}
