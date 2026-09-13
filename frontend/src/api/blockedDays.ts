/** `blocked-days` 標籤：不可排班日登記，獨立於值班表存在（ADR-0001）。 */
import { apiDelete, apiGet, apiPut } from './client'
import type { BlockedDayMutationResult, BlockedDayRegistration, FeasibilityReport } from './types'

export function getBlockedDays(ym: string, signal?: AbortSignal): Promise<BlockedDayRegistration> {
  return apiGet<BlockedDayRegistration>(`/blocked-days/${ym}`, { signal })
}

export function setBlockedDay(
  ym: string,
  staffId: string,
  date: string,
  signal?: AbortSignal,
): Promise<BlockedDayMutationResult> {
  return apiPut<BlockedDayMutationResult>(`/blocked-days/${ym}/${staffId}/${date}`, undefined, { signal })
}

export function clearBlockedDay(
  ym: string,
  staffId: string,
  date: string,
  signal?: AbortSignal,
): Promise<BlockedDayMutationResult> {
  return apiDelete<BlockedDayMutationResult>(`/blocked-days/${ym}/${staffId}/${date}`, { signal })
}

export function getFeasibility(ym: string, signal?: AbortSignal): Promise<FeasibilityReport> {
  return apiGet<FeasibilityReport>(`/blocked-days/${ym}/feasibility`, { signal })
}
