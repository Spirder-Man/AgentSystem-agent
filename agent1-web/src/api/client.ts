// ============================================================
// FE-1: 统一 API 客户端
// 基于 lib/axios.ts 的生产级拦截器实例，封装通用请求方法。
// ============================================================

import apiClient from '@/lib/axios';
import type { AxiosRequestConfig } from 'axios';

/** 通用 GET 请求 */
export async function get<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const { data } = await apiClient.get<T>(url, config);
  return data;
}

/** 通用 POST 请求 */
export async function post<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const { data } = await apiClient.post<T>(url, body, config);
  return data;
}

/** 通用 PUT 请求 */
export async function put<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const { data } = await apiClient.put<T>(url, body, config);
  return data;
}

/** 通用 DELETE 请求 */
export async function del<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const { data } = await apiClient.delete<T>(url, config);
  return data;
}

export { apiClient };
