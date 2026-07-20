// ============================================================
// Axios 实例 — 生产级拦截器
//   400/401/403/422/429/500/503/Network Error 全覆盖
// ============================================================

import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import type { ApiError } from '@/types/api';

// ═══ Token ═══

let tokenGetter: (() => string | null) = () => null;
let refreshTokenGetter: (() => string | null) = () => null;
let onTokenRefreshed: ((token: string, refreshToken: string) => void) | null = null;
let onLogout: (() => void) | null = null;

export function configureAuth(config: {
  getToken: () => string | null;
  getRefreshToken: () => string | null;
  onTokenRefreshed: (token: string, refreshToken: string) => void;
  onLogout: () => void;
}) {
  tokenGetter = config.getToken;
  refreshTokenGetter = config.getRefreshToken;
  onTokenRefreshed = config.onTokenRefreshed;
  onLogout = config.onLogout;
}

// ═══ Error Handler ═══

type ErrorHandler = (code: string, message: string, details?: Record<string, string[]>) => void;
let onError: ErrorHandler | null = null;
let onForbidden: (() => void) | null = null;

export function configureErrorHandler(handlers: {
  onError?: ErrorHandler;
  onForbidden?: () => void;
}) {
  if (handlers.onError) onError = handlers.onError;
  if (handlers.onForbidden) onForbidden = handlers.onForbidden;
}

const ERROR_MESSAGES: Record<string, string> = {
  RATE_LIMITED: '请求过于频繁，请稍后重试',
  INVALID_INPUT: '输入内容不符合要求',
  SESSION_EXPIRED: '会话已过期，请重新登录',
  UNAUTHORIZED: '您没有权限执行此操作',
  VALIDATION_FAILED: '数据校验失败',
  SERVER_ERROR: '服务异常，已记录日志',
  NETWORK_ERROR: '网络连接异常',
};

// ═══ Instance ═══

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/',
  timeout: 120_000,
  headers: { 'Content-Type': 'application/json' },
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = tokenGetter();
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ═══ Response ═══

let isRefreshing = false;
let failedQueue: Array<{ resolve: (v: string) => void; reject: (e: unknown) => void }> = [];

function getUserMessage(error: AxiosError<ApiError>): string {
  const code = error.response?.data?.code;
  if (code && ERROR_MESSAGES[code]) return ERROR_MESSAGES[code];
  return error.response?.data?.error || '请求失败，请稍后重试';
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiError>) => {
    const req = error.config as InternalAxiosRequestConfig & { _retry?: boolean; _retryCount?: number };
    const status = error.response?.status;

    if (!error.response) {
      const msg = navigator.onLine ? ERROR_MESSAGES.SERVER_ERROR : ERROR_MESSAGES.NETWORK_ERROR;
      onError?.('NETWORK_ERROR', msg);
      return Promise.reject(error);
    }

    if (status === 503) {
      const retryCount = req._retryCount ?? 0;
      if (retryCount < 2) {
        req._retryCount = retryCount + 1;
        const retryAfter = error.response.data?.retryAfter ?? 5;
        const backoff = retryAfter * 1000 * (retryCount + 1);
        onError?.('RETRYING', `服务繁忙，${Math.ceil(backoff / 1000)}s 后自动重试 (${retryCount + 1}/2)`);
        await new Promise((r) => setTimeout(r, backoff));
        return apiClient(req);
      }
      onError?.('SERVICE_UNAVAILABLE', '服务暂时不可用');
      return Promise.reject(error);
    }

    if (status === 401 && !req._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then((token) => {
          if (req.headers) req.headers.Authorization = `Bearer ${token}`;
          return apiClient(req);
        });
      }

      req._retry = true;
      isRefreshing = true;

      try {
        const refreshToken = refreshTokenGetter();
        if (!refreshToken) throw new Error('No refresh token');

        const { data } = await axios.post<{ token: string; refreshToken: string }>(
          `${apiClient.defaults.baseURL}/api/Auth/refresh`, { refreshToken }
        );

        onTokenRefreshed?.(data.token, data.refreshToken);
        failedQueue.forEach((p) => p.resolve(data.token));
        failedQueue = [];
        if (req.headers) req.headers.Authorization = `Bearer ${data.token}`;
        return apiClient(req);
      } catch {
        failedQueue.forEach((p) => p.reject(error));
        failedQueue = [];
        onLogout?.();
        window.location.href = '/login';
        return Promise.reject(error);
      } finally {
        isRefreshing = false;
      }
    }

    if (status === 403) { onForbidden?.(); return Promise.reject(error); }
    if (status === 400) { onError?.('VALIDATION_FAILED', getUserMessage(error), error.response?.data?.details); return Promise.reject(error); }
    if (status === 422) { onError?.('BUSINESS_ERROR', getUserMessage(error)); return Promise.reject(error); }
    if (status === 429) { onError?.('RATE_LIMITED', `请求过于频繁，${error.response.data?.retryAfter ?? 60}s 后可重试`); return Promise.reject(error); }
    if (status === 500) { onError?.('SERVER_ERROR', ERROR_MESSAGES.SERVER_ERROR); return Promise.reject(error); }

    const traceId = error.response?.headers['x-request-id'] || 'N/A';
    console.error(`[API Error] ${error.config?.method?.toUpperCase()} ${error.config?.url} | TraceId: ${traceId} | Status: ${status}`);
    return Promise.reject(error);
  }
);

export default apiClient;
