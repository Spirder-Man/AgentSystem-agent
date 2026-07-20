import { describe, it, expect, vi } from 'vitest';
import { adminApi } from '../admin';

const mockGet = vi.fn();

vi.mock('../client', () => ({
  get: (...args: unknown[]) => mockGet(...args),
  post: vi.fn(),
  put: vi.fn(),
  del: vi.fn(),
}));

describe('adminApi', () => {
  it('getDbInfo 应调用 GET /api/Admin/db/info', () => {
    adminApi.getDbInfo();
    expect(mockGet).toHaveBeenCalledWith('/api/Admin/db/info');
  });

  it('validateDb 应调用 GET /api/Admin/db/validate', () => {
    adminApi.validateDb();
    expect(mockGet).toHaveBeenCalledWith('/api/Admin/db/validate');
  });
});
