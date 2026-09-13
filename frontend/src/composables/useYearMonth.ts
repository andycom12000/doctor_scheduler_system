/**
 * 全域「目前年月」——年月放在路徑（`/schedules/2026-09`），不需要 Pinia。
 * `/settings/*`、`/staff` 沒有 `:ym` 參數，這裡記住「最後一個合法的 :ym」再退回去，
 * 而不是直接退回當月：不然 `/schedules/2026-12` → `/staff` → 回排班主表會落到當月，
 * 使用者剛選好的年月就這樣不見了。
 */
import { computed, ref, watch, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export const YEAR_MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/

export function currentYearMonth(now: Date = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
}

/** 模組層級：最後一個從路由讀到的合法 `:ym`，初值是當月。 */
const lastYm = ref(currentYearMonth())

/** 測試專用：重置模組層級的「最後年月」，避免測試之間互相汙染。 */
export function resetLastYearMonthForTests(now?: Date): void {
  lastYm.value = currentYearMonth(now)
}

export interface UseYearMonthResult {
  /** 目前路由的年月；路由沒有 `:ym` 時回退到「最後一個合法的 :ym」（初值當月）。 */
  ym: ComputedRef<string>
  /** 路由有 `:ym` 時原地替換；沒有時導到排班主表的那個年月。 */
  setYearMonth: (next: string) => void
}

export function useYearMonth(): UseYearMonthResult {
  const route = useRoute()
  const router = useRouter()

  watch(
    () => route.params.ym,
    (raw) => {
      const value = Array.isArray(raw) ? raw[0] : raw
      if (value && YEAR_MONTH_PATTERN.test(value)) {
        lastYm.value = value
      }
    },
    { immediate: true },
  )

  const ym = computed(() => lastYm.value)

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
