// ============================================================
// FE-1: 知识图谱 API 模块
// 对齐后端 KnowledgeGraphController 端点。
// ============================================================

import { post } from './client';
import type { KnowledgeGraphRequest, KnowledgeGraphResult } from '@/types/api';

export const graphApi = {
  /** 知识图谱查询 —— 输入化学品或法规关键词，返回关联法规、事故案例与物质属性 */
  query: (data: KnowledgeGraphRequest) =>
    post<KnowledgeGraphResult>('/api/KnowledgeGraph/query', data),
};
