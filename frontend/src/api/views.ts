/** `views` 標籤：由值班表推導的唯讀檢視。`listViolations` 契約上掛 `schedules` 標籤，放這裡因為它是檢視類查詢。 */
import { apiGet } from './client'
import type {
  DayDetail,
  GetPointBoardResponse,
  ListCandidatesResponse,
  ListVacanciesResponse,
  ListViolationsResponse,
  Severity,
} from './types'

export interface ListViolationsParams {
  severity?: Severity
  date?: string
}

export function listViolations(
  ym: string,
  params: ListViolationsParams = {},
  signal?: AbortSignal,
): Promise<ListViolationsResponse> {
  const query = new URLSearchParams()
  if (params.severity) query.set('severity', params.severity)
  if (params.date) query.set('date', params.date)
  const suffix = query.size > 0 ? `?${query}` : ''
  return apiGet<ListViolationsResponse>(`/schedules/${ym}/violations${suffix}`, { signal })
}

export function getPointBoard(ym: string, signal?: AbortSignal): Promise<GetPointBoardResponse> {
  return apiGet<GetPointBoardResponse>(`/schedules/${ym}/point-board`, { signal })
}

export function getDayDetail(ym: string, date: string, signal?: AbortSignal): Promise<DayDetail> {
  return apiGet<DayDetail>(`/schedules/${ym}/days/${date}`, { signal })
}

export function listVacancies(ym: string, signal?: AbortSignal): Promise<ListVacanciesResponse> {
  return apiGet<ListVacanciesResponse>(`/schedules/${ym}/vacancies`, { signal })
}

export function listCandidates(
  ym: string,
  areaId: string,
  date: string,
  signal?: AbortSignal,
): Promise<ListCandidatesResponse> {
  const query = new URLSearchParams({ areaId, date })
  return apiGet<ListCandidatesResponse>(`/schedules/${ym}/candidates?${query}`, { signal })
}
