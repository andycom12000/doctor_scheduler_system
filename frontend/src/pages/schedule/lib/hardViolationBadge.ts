/**
 * 工具列「發布」旁的硬違規警示標籤（#68）：把違規清單歸納成標籤文字。
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
  const detail = doubleBooked > 0 ? `其中 ${doubleBooked} 項同人同日兩區，排除後才能發布` : null
  return { headline, detail, label: detail ? `${headline}，${detail}` : headline }
}
