import { get, put, post } from './client';
import type {
  TicketListResponse,
  TicketItem,
  TicketStatusUpdateRequest,
  TicketFollowupRequest,
  TicketFollowupResult,
} from '../types/api';

export const ticketsApi = {
  /** 获取工单列表 */
  list: () =>
    get<TicketListResponse>('/api/Tickets'),

  /** 获取单个工单详情 */
  getDetail: (id: number) =>
    get<TicketItem>(`/api/Tickets/${id}`),

  /** 更新工单状态 */
  updateStatus: (id: number, data: TicketStatusUpdateRequest) =>
    put<TicketItem>(`/api/Tickets/${id}/status`, data),

  /** 执行工单跟进 */
  followup: (data: TicketFollowupRequest) =>
    post<TicketFollowupResult>('/api/Tickets/followup', data),
};
