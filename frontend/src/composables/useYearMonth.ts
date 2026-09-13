/**
 * 全域「目前年月」——年月放在路徑（`/schedules/2026-09`），不需要 Pinia。
 * `/settings/*`、`/staff` 沒有 `:ym` 參數，這裡回退到當月，方便頂列切換器一直有值可顯示。
 */
import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export const YEAR_MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/

export function currentYearMonth(now: Date = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
}

export interface UseYearMonthResult {
  /** 目前路由的年月；路由沒有 `:ym` 時回退到當月。 */
  ym: ComputedRef<string>
  /** 路由有 `:ym` 時原地替換；沒有時導到排班主表的那個年月。 */
  setYearMonth: (next: string) => void
}

export function useYearMonth(): UseYearMonthResult {
  const route = useRoute()
  const router = useRouter()

  const ym = computed(() => {
    const raw = route.params.ym
    const value = Array.isArray(raw) ? raw[0] : raw
    return value && YEAR_MONTH_PATTERN.test(value) ? value : currentYearMonth()
  })

  function setYearMonth(next: string): void {
    if (!YEAR_MONTH_PATTERN.test(next)) return

    if (route.params.ym !== undefined && route.name) {
      void router.replace({ name: route.name, params: { ...route.params, ym: next }, query: route.query })
      return
    }

    void router.push({ name: 'schedule', params: { ym: next } })
  }

  return { ym, setYearMonth }
}
