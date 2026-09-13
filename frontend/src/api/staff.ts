/** `staff` 標籤：人員名冊，一次全帶、不分頁（全院約 34 人）。 */
import { apiDelete, apiGet, apiPatch, apiPost } from './client'
import type { ListStaffResponse, SetStaffStatusRequest, Staff, StaffStatus, StaffWrite } from './types'

export function listStaff(status?: StaffStatus, signal?: AbortSignal): Promise<ListStaffResponse> {
  const query = status ? `?${new URLSearchParams({ status })}` : ''
  return apiGet<ListStaffResponse>(`/staff${query}`, { signal })
}

export function createStaff(body: StaffWrite, signal?: AbortSignal): Promise<Staff> {
  return apiPost<Staff>('/staff', body, { signal })
}

export function updateStaff(id: string, body: StaffWrite, signal?: AbortSignal): Promise<Staff> {
  return apiPatch<Staff>(`/staff/${id}`, body, { signal })
}

export function deleteStaff(id: string, signal?: AbortSignal): Promise<void> {
  return apiDelete<void>(`/staff/${id}`, { signal })
}

export function setStaffStatus(
  id: string,
  body: SetStaffStatusRequest,
  signal?: AbortSignal,
): Promise<Staff> {
  return apiPatch<Staff>(`/staff/${id}/status`, body, { signal })
}
