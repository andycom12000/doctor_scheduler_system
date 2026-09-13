import { describe, expect, it } from 'vitest'
import type { Duty, Rank, RankGroup, Staff } from '@/api/types'
import { buildHeatmap, staffGroupIndex } from './heatmap'

const groups: RankGroup[] = [
  { code: 'JUNIOR', name: '低年級' },
  { code: 'MID', name: '中階' },
  { code: 'SENIOR', name: '資深' },
  { code: 'NP', name: 'NP' },
]

const ranks: Rank[] = [
  { code: 'R2', name: 'R2', groupCode: 'MID', quotaCap: 8, pointType: 'A' },
  { code: 'R4', name: 'R4', groupCode: 'SENIOR', quotaCap: 6, pointType: 'B' },
]

const staff: Staff[] = [
  { id: 's-r2', employeeNo: '001', name: 'R2甲', rankCode: 'R2', status: 'active', eligibleAreaTypes: ['WARD', 'ICU'] },
  { id: 's-r4', employeeNo: '002', name: 'R4甲', rankCode: 'R4', status: 'active', eligibleAreaTypes: ['ICU', 'CHIEF'] },
]

describe('staffGroupIndex', () => {
  it('依 groups 陣列的順位對應身分組', () => {
    const index = staffGroupIndex(staff, ranks, groups)
    expect(index.get('s-r2')).toBe(1) // MID 在 groups[1]
    expect(index.get('s-r4')).toBe(2) // SENIOR 在 groups[2]
  })

  it('身分找不到對應的身分組時不列入', () => {
    const orphan: Staff = { id: 's-x', employeeNo: '003', name: 'X', rankCode: 'GHOST', status: 'active', eligibleAreaTypes: [] }
    const index = staffGroupIndex([orphan], ranks, groups)
    expect(index.has('s-x')).toBe(false)
  })
})

describe('buildHeatmap', () => {
  const duties: Duty[] = [
    { areaId: 'area-icu', date: '2026-09-01', staffId: 's-r2', cellKey: 'area:area-icu:2026-09-01' },
    { areaId: 'area-chief', date: '2026-09-02', staffId: 's-r4', cellKey: 'area:area-chief:2026-09-02' },
  ]
  const groupIndexByStaff = staffGroupIndex(staff, ranks, groups)

  it('依 areaIds × days 的順序回傳每一格', () => {
    const grid = buildHeatmap(duties, ['area-icu', 'area-chief'], ['2026-09-01', '2026-09-02'], groupIndexByStaff)
    expect(grid).toHaveLength(2)
    expect(grid[0]).toHaveLength(2)
    expect(grid[0][0]).toEqual({ areaId: 'area-icu', date: '2026-09-01', staffId: 's-r2', groupIndex: 1 })
    expect(grid[0][1]).toEqual({ areaId: 'area-icu', date: '2026-09-02', staffId: null, groupIndex: null })
    expect(grid[1][1]).toEqual({ areaId: 'area-chief', date: '2026-09-02', staffId: 's-r4', groupIndex: 2 })
  })

  it('空缺（沒有值班的格子）staffId 與 groupIndex 皆為 null', () => {
    const grid = buildHeatmap([], ['area-a'], ['2026-09-01'], groupIndexByStaff)
    expect(grid[0][0]).toEqual({ areaId: 'area-a', date: '2026-09-01', staffId: null, groupIndex: null })
  })
})
