// ============================================================
// P1-2: Auth API 单元测试
// ============================================================

import { describe, it, expect, beforeAll, afterAll, afterEach } from 'vitest';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { authApi } from '../auth';

const server = setupServer(
  http.post('/api/Auth/login', async ({ request }) => {
    const body = (await request.json()) as { username: string; password: string };
    return HttpResponse.json({
      token: 'mock-jwt-admin-9999999999999',
      refreshToken: 'mock-refresh-token',
      username: body.username,
      role: 'admin',
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  http.post('/api/Auth/refresh', async ({ request }) => {
    const body = (await request.json()) as { refreshToken: string };
    return HttpResponse.json({
      token: 'mock-jwt-admin-9999999999999',
      refreshToken: body.refreshToken,
      username: 'admin',
      role: 'admin',
      expiresAt: new Date(Date.now() + 3600000).toISOString(),
    });
  }),

  http.post('/api/Auth/logout', () => {
    return HttpResponse.json({ success: true });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe('authApi — 端点路径', () => {
  it('login() 请求 POST /api/Auth/login', async () => {
    const result = await authApi.login({ username: 'admin', password: 'test' });
    expect(result.token).toBeTruthy();
    expect(result.role).toBe('admin');
    expect(result.username).toBe('admin');
    expect(result.refreshToken).toBeTruthy();
  });

  it('refresh() 请求 POST /api/Auth/refresh', async () => {
    const result = await authApi.refresh({ refreshToken: 'old-token' });
    expect(result.token).toBeTruthy();
    expect(result.refreshToken).toBe('old-token');
  });

  it('logout() 请求 POST /api/Auth/logout', async () => {
    await expect(authApi.logout()).resolves.toBeDefined();
  });
});
