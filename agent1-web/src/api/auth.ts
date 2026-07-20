import { post } from './client';
import type { LoginRequest, LoginResponse, RefreshRequest } from '../types/api';

export const authApi = {
  login: (data: LoginRequest) =>
    post<LoginResponse>('/api/Auth/login', data),

  refresh: (data: RefreshRequest) =>
    post<LoginResponse>('/api/Auth/refresh', data),

  logout: () =>
    post<void>('/api/Auth/logout'),
};
