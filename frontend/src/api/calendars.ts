/** `settings` 標籤（契約分類），但概念上獨立於五份設定文件：以年為單位的行事曆事實來源。 */
import { apiGet, apiPatch, apiPost } from './client'
import type { Calendar, CalendarDay, CalendarDayOverride, CalendarSyncStatus } from './types'

export function getCalendar(year: number, signal?: AbortSignal): Promise<Calendar> {
  return apiGet<Calendar>(`/calendars/${year}`, { signal })
}

export function overrideCalendarDay(
  year: number,
  date: string,
  body: CalendarDayOverride,
  signal?: AbortSignal,
): Promise<CalendarDay> {
  return apiPatch<CalendarDay>(`/calendars/${year}/${date}`, body, { signal })
}

/** 重試行事曆自動更新（冪等：已在跑或沒開就回目前狀態）。 */
export function startCalendarSync(signal?: AbortSignal): Promise<CalendarSyncStatus> {
  return apiPost<CalendarSyncStatus>('/calendars/sync', undefined, { signal })
}

/** 行事曆自動更新的狀態（#112）。前端啟動後輪詢，見 `composables/useCalendarSyncNotice.ts`。 */
export function getCalendarSyncStatus(signal?: AbortSignal): Promise<CalendarSyncStatus> {
  return apiGet<CalendarSyncStatus>('/calendars/sync-status', { signal })
}
