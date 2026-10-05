/**
 * 「名冊裡沒有任何在職人員」的判斷（issue #81）。
 *
 * 依據是 `GET /api/staff` 的 `counts.active`。目前呼叫端都不帶 `?status=` 篩選，
 * 這裡的判斷也以此為前提。
 *
 * 三態：`true` 沒有在職人員、`false` 有、`null` 還不知道（名冊還沒載入或載入失敗）。
 * 呼叫端的規則：引導只在 `=== true` 時顯示；求解入口只在 `=== false` 時開放，
 * 不能把 `null` 當成有人或沒人，否則載入瞬間會閃引導，或讓求解按鈕露出來。
 */
import type { ListStaffResponse } from '@/api/types'

export function hasNoActiveStaff(res: ListStaffResponse | null | undefined): boolean | null {
  if (!res) return null
  return res.counts.active === 0
}

/** 名冊有人，但全部停用（引導文案要改成請去啟用，而不是請去新增）。 */
export function allStaffInactive(res: ListStaffResponse | null | undefined): boolean {
  return !!res && res.counts.active === 0 && (res.counts.inactive ?? 0) > 0
}

export const NO_ACTIVE_STAFF_REASON = '名冊沒有在職人員，無法求解'
