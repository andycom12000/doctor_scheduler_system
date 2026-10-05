/**
 * 「名冊裡沒有任何在職人員」的判斷（issue #81）。
 *
 * 依據是 `GET /api/staff` 的 `counts.active`。`counts` 是整份名冊的統計，不受 `?status=` 篩選影響，
 * 所以這裡不必管呼叫端有沒有帶篩選。
 *
 * 回傳 `null` 代表「還不知道」（名冊還沒載入或載入失敗）——呼叫端不能把它當成 0 人，
 * 否則名冊還在載入的那一瞬間會閃一下引導畫面。
 */
import type { ListStaffResponse } from '@/api/types'

export function hasNoActiveStaff(res: ListStaffResponse | null | undefined): boolean | null {
  if (!res) return null
  return res.counts.active === 0
}
