import { describe, expect, it } from 'vitest'
import { computeGroupUtilization } from './pointBoardStats'
import type { PointBoardGroup, PointBoardRow } from '@/api/types'

function row(overrides: Partial<PointBoardRow> & { staffId: string }): PointBoardRow {
  return {
    name: overrides.staffId,
    rankCode: 'R2',
    quotaPoints: 0,
    quotaCap: 8,
    quotaRemaining: 8,
    duties: 0,
    holidayDuties: 0,
    ...overrides,
  }
}

describe('computeGroupUtilization', () => {
  it('組內 Σ已排 ÷ Σ上限，四捨五入到整數百分比', () => {
    const groups: PointBoardGroup[] = [
      {
        groupCode: 'MID',
        groupName: '中階',
        rows: [row({ staffId: 'staff-1', quotaPoints: 6, quotaCap: 8 }), row({ staffId: 'staff-2', quotaPoints: 7, quotaCap: 7 })],
      },
    ]
    const [util] = computeGroupUtilization(groups)
    expect(util.usedPoints).toBe(13)
    expect(util.capPoints).toBe(15)
    expect(util.percent).toBe(87)
  })

  it('quotaCap 全為 null（NP）時回 percent: null，不除以零', () => {
    const groups: PointBoardGroup[] = [
      {
        groupCode: 'NP',
        groupName: 'NP',
        rows: [row({ staffId: 'staff-np', quotaCap: null, quotaRemaining: null, duties: 5 })],
      },
    ]
    const [util] = computeGroupUtilization(groups)
    expect(util.capPoints).toBeNull()
    expect(util.percent).toBeNull()
  })

  it('組內有人 quotaCap 為 null 時只算有上限的人（不應發生於目前 fixture，但函式要防禦）', () => {
    const groups: PointBoardGroup[] = [
      {
        groupCode: 'MIXED',
        groupName: '混合',
        rows: [row({ staffId: 'staff-1', quotaPoints: 4, quotaCap: 8 }), row({ staffId: 'staff-np', quotaPoints: 3, quotaCap: null })],
      },
    ]
    const [util] = computeGroupUtilization(groups)
    expect(util.usedPoints).toBe(4)
    expect(util.capPoints).toBe(8)
    expect(util.percent).toBe(50)
  })

  it('空群組陣列回空陣列', () => {
    expect(computeGroupUtilization([])).toEqual([])
  })
})
