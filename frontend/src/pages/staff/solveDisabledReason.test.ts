import { describe, expect, it } from 'vitest'
import type { ListStaffResponse } from '@/api/types'
import {
  NO_ACTIVE_STAFF_REASON,
  STAFF_LOAD_FAILED_REASON,
  solveDisabledReason,
} from './rosterGuard'

const res = (active: number, inactive = 0) => ({ items: [], counts: { active, inactive } }) as unknown as ListStaffResponse

describe('solveDisabledReason（issue #95）', () => {
  it('有在職人員：可求解，無原因', () => {
    expect(solveDisabledReason(res(3), false)).toBeNull()
  })
  it('沒有在職人員：回名冊原因', () => {
    expect(solveDisabledReason(res(0), false)).toBe(NO_ACTIVE_STAFF_REASON)
    expect(solveDisabledReason(res(0, 2), false)).toBe(NO_ACTIVE_STAFF_REASON)
  })
  it('讀取失敗：回失敗提示，不是沉默的 disabled', () => {
    expect(solveDisabledReason(null, true)).toBe(STAFF_LOAD_FAILED_REASON)
  })
  it('載入中：不出字（只停用按鈕）', () => {
    expect(solveDisabledReason(undefined, false)).toBeNull()
  })
})
