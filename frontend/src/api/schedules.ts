/**
 * `schedules` 標籤：值班表本體的讀寫，含求解變體套用（契約掛在 `solver` 標籤，
 * 但操作對象是值班表，跟這裡放在一起比較好找）。
 */
import { apiGet, apiGetBlob, apiPatch, apiPost, type BlobResponse } from './client'
import type {
  ApplyVariantRequest,
  ExportLayout,
  ListSchedulesResponse,
  MutationResult,
  PublishResult,
  Schedule,
  SetDutyRequest,
  SwapDutiesRequest,
  ValidationResult,
} from './types'

export function listSchedules(signal?: AbortSignal): Promise<ListSchedulesResponse> {
  return apiGet<ListSchedulesResponse>('/schedules', { signal })
}

export function getSchedule(ym: string, signal?: AbortSignal): Promise<Schedule> {
  return apiGet<Schedule>(`/schedules/${ym}`, { signal })
}

export function setDuty(ym: string, body: SetDutyRequest, signal?: AbortSignal): Promise<MutationResult> {
  return apiPatch<MutationResult>(`/schedules/${ym}/duties`, body, { signal })
}

export function swapDuties(ym: string, body: SwapDutiesRequest, signal?: AbortSignal): Promise<MutationResult> {
  return apiPost<MutationResult>(`/schedules/${ym}/duties/swap`, body, { signal })
}

export function validateSchedule(ym: string, signal?: AbortSignal): Promise<ValidationResult> {
  return apiPost<ValidationResult>(`/schedules/${ym}/validate`, undefined, { signal })
}

export function publishSchedule(
  ym: string,
  acknowledgeViolations = false,
  signal?: AbortSignal,
): Promise<PublishResult> {
  return apiPost<PublishResult>(`/schedules/${ym}/publish`, { acknowledgeViolations }, { signal })
}

export function exportSchedule(
  ym: string,
  layout: ExportLayout = 'area-by-day',
  signal?: AbortSignal,
): Promise<BlobResponse> {
  const query = new URLSearchParams({ layout })
  return apiGetBlob(`/schedules/${ym}/export?${query}`, { signal })
}

export function applyVariant(ym: string, body: ApplyVariantRequest, signal?: AbortSignal): Promise<Schedule> {
  return apiPost<Schedule>(`/schedules/${ym}/apply-variant`, body, { signal })
}
