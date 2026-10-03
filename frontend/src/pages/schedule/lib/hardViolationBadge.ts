/**
 * 違規側欄頂端的硬違規摘要卡（#68）：把違規清單歸納成摘要文字。
 * 純函式，靠 `hardViolationBadge.test.ts` 守住；畫面在 `HardViolationBadge.vue`。
 */
import type { Violation } from '@/api/types'

/** 同一人同一天排在兩區的結構規則代碼（Domain `StructuralRules.StaffDoubleBooked`）。發布時不能略過。 */
export const DOUBLE_BOOKED_CODE = 'X1_STAFF_DOUBLE_BOOKED'

export interface HardViolationBadgeInfo {
  /** 第一行：「N 項硬違規」。 */
  headline: string
  /** 第二行：有同人同日兩區時才有，其餘為 null。 */
  detail: string | null
  /** 螢幕閱讀器用的完整一句話。 */
  label: string
}

/** 沒有硬違規回 `null`（標籤不顯示）；軟違規不算。 */
export function hardViolationBadgeInfo(violations: readonly Violation[]): HardViolationBadgeInfo | null {
  const hard = violations.filter((v) => v.severity === 'hard')
  if (hard.length === 0) return null
  const doubleBooked = hard.filter((v) => v.code === DOUBLE_BOOKED_CODE).length
  const headline = `${hard.length} 項硬違規`
  const detail = doubleBooked > 0 ? `其中 ${doubleBooked} 項同人同日兩區，排除後才能發布／匯出／列印` : null
  return { headline, detail, label: detail ? `${headline}，${detail}` : headline }
}

/**
 * 匯出／列印前的前端把關（#68）：違規清單含同人同日兩區就回要顯示的錯誤訊息，沒有回 `null`。
 * 後端匯出遇到同一情況也會 409 `DOUBLE_BOOKING_PRESENT`，這裡只是不必打 API 就先擋。
 */
export function doubleBookingBlockMessage(violations: readonly Violation[], action: '匯出' | '列印'): string | null {
  return violations.some((v) => v.severity === 'hard' && v.code === DOUBLE_BOOKED_CODE)
    ? `還有人同一天排在兩區，排除後才能${action}。`
    : null
}

/**
 * 列印前的 fail-closed 把關（#68）：列印沒有後端守門，所以前端列印前重新抓過違規清單，
 * 用這個判斷能不能印。清單拿不到（`violations` 為 null）或重抓失敗（`loadFailed`）一律不印，
 * 不能因為快取是空的或留著舊清單就放行。能印回 `null`。
 */
export function printBlockMessage(violations: readonly Violation[] | null, loadFailed: boolean): string | null {
  if (loadFailed || violations === null) return '無法確認違規清單，暫時不能列印，請稍後再試。'
  return doubleBookingBlockMessage(violations, '列印')
}
