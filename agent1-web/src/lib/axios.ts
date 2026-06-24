// ============================================================
// Axios 实例 — JWT 自动附加 + Token 自动刷新 + 503 重试
//
// 无论 Mock 模式还是真实 API 模式，前端组件使用同一个 apiClient，
// 网络层区别完全由 MSW Service Worker 控制。
// ============================================================

import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';

// 登录后由 auth-store 写入，此处声明以便拦截器访问
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

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  timeout: 120_000, // LLM 推理最长 2 分钟
  headers: { 'Content-Type': 'application/json' },
});

// ── 请求拦截器: 自动附加 JWT ──
apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = tokenGetter();
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ── 响应拦截器: 401 刷新 / 503 重试 / TraceId 追踪 ──
let isRefreshing = false;
let failedQueue: Array<{ resolve: (v: string) => void; reject: (e: unknown) => void }> = [];

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<{ error?: string; retryAfter?: number }>) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean; _retryCount?: number };

    // 503 — 自动重试（最多 2 次）
    if (error.response?.status === 503) {
      const retryCount = originalRequest._retryCount ?? 0;
      if (retryCount < 2) {
        originalRequest._retryCount = retryCount + 1;
        const retryAfter = error.response.data?.retryAfter ?? 5;
        await new Promise((r) => setTimeout(r, retryAfter * 1000));
        return apiClient(originalRequest);
      }
    }

    // 401 — Token 刷新
    if (error.response?.status === 401 && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then((token) => {
          if (originalRequest.headers) {
            originalRequest.headers.Authorization = `Bearer ${token}`;
          }
          return apiClient(originalRequest);
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const refreshToken = refreshTokenGetter();
        if (!refreshToken) throw new Error('No refresh token');

        const { data } = await axios.post<{
          token: string; refreshToken: string;
        }>(`${apiClient.defaults.baseURL}/api/Auth/refresh`, { refreshToken });

        onTokenRefreshed?.(data.token, data.refreshToken);
        failedQueue.forEach((p) => p.resolve(data.token));
        failedQueue = [];
        if (originalRequest.headers) {
          originalRequest.headers.Authorization = `Bearer ${data.token}`;
        }
        return apiClient(originalRequest);
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

    // TraceId 追踪
    const traceId = error.response?.headers['x-request-id'] || 'N/A';
    console.error(
      `[API Error] ${error.config?.method?.toUpperCase()} ${error.config?.url} | TraceId: ${traceId} | Status: ${error.response?.status}`
    );

    return Promise.reject(error);
  }
);

export default apiClient;
