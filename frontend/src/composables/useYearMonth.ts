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

/**
 * 年月位移 `delta` 個月。`YearMonthSwitcher.vue`（頂列全域年月）與 SCREEN 02「指定月份覆寫」
 * 獨立月份選擇器（issue #45）共用，避免兩處各自重寫一份月份進位／借位邏輯。
 *
 * 非法輸入（不符 `YEAR_MONTH_PATTERN`）原樣回傳、不拋錯——呼叫端本來就該只餵合法值
 * （`<select>` 的候選清單一律是 `yearMonthOptions` 產生的合法字串），這裡只是最後一道防呆。
 * 年份用 `setFullYear` 而非 `new Date(year, month, day)` 建構子：後者對 0–99 的年份會套用
 * ECMA-262 的「兩位數年份自動補 1900」規則（`new Date(50, 0, 1)` 會變成西元 1950 年），
 * `setFullYear` 沒有這條特殊規則，年份原樣寫入、`getFullYear()` 讀回來也原樣，才能正確處理
 * 理論上合法但反常的年份（見 `useYearMonth.test.ts` 的邊界測試）。
 */
export function shiftYearMonth(ym: string, delta: number): string {
  if (!YEAR_MONTH_PATTERN.test(ym)) return ym
  const [year, month] = ym.split('-').map(Number)
  const date = new Date()
  date.setFullYear(year, month - 1 + delta, 1)
  return `${String(date.getFullYear()).padStart(4, '0')}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

/**
 * 以 `baseYm` 為中心前後各 12 個月的候選年月清單，聯集上 `extra`（例如有值班表的月份、
 * 目前選到但落在範圍外的月份）。`YearMonthSwitcher.vue` 與 SCREEN 02「指定月份覆寫」的獨立
 * 月份選擇器共用同一份組法，避免兩處清單的範圍與排序規則各自漂移。
 */
export function yearMonthOptions(baseYm: string, extra: string[] = []): string[] {
  const set = new Set<string>(extra)
  for (let offset = -12; offset <= 12; offset++) set.add(shiftYearMonth(baseYm, offset))
  return [...set].sort()
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
