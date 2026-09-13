/**
 * `GET /calendars/{year}` 攤成 `Map<date, CalendarDay>`。
 * 所有畫面的表頭、假日底色、額度點數值都從這裡拿，不各自算（frontend-plan.md §2 第 5 點）。
 *
 * `indexCalendar` 與 `daysOf` 是純函式，方便用假資料測試、不需要打網路。
 */
import { computed, type ComputedRef, type Ref } from 'vue'
import { getCalendar } from '@/api/calendars'
import type { Calendar, CalendarDay } from '@/api/types'
import { useResource } from './useResource'

export function indexCalendar(calendar: Calendar | null | undefined): Map<string, CalendarDay> {
  const index = new Map<string, CalendarDay>()
  if (!calendar) return index
  for (const day of calendar.days) index.set(day.date, day)
  return index
}

/** 某個 `YYYY-MM` 的所有日子，已依日期排序。 */
export function daysOf(index: Map<string, CalendarDay>, ym: string): CalendarDay[] {
  const prefix = `${ym}-`
  return [...index.values()]
    .filter((day) => day.date.startsWith(prefix))
    .sort((a, b) => a.date.localeCompare(b.date))
}

export interface UseCalendarResult {
  calendar: ComputedRef<Calendar | null>
  index: ComputedRef<Map<string, CalendarDay>>
  loading: ComputedRef<boolean>
  error: ComputedRef<unknown>
  isHoliday: (date: string) => boolean
  quotaPointValue: (date: string) => number | null
  daysOf: (ym: string) => CalendarDay[]
}

export function useCalendar(year: Ref<number>): UseCalendarResult {
  const key = computed(() => `calendars/${year.value}`)
  const { data, error, loading } = useResource(key, () => getCalendar(year.value))

  const index = computed(() => indexCalendar(data.value))

  return {
    calendar: data,
    index,
    loading,
    error,
    isHoliday: (date) => index.value.get(date)?.isHoliday ?? false,
    quotaPointValue: (date) => index.value.get(date)?.quotaPointValue ?? null,
    daysOf: (ym) => daysOf(index.value, ym),
  }
}
