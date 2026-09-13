/** `settings` 標籤（契約分類），但概念上獨立於五份設定文件：以年為單位的行事曆事實來源。 */
import { apiGet, apiPatch } from './client'
import type { Calendar, CalendarDay, CalendarDayOverride } from './types'

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
