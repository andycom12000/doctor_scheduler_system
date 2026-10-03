import { describe, expect, it } from 'vitest'
import { domIdForCellKey, parseCellKey, resolveJumpTarget, tabForCellKey } from './cellNav'

describe('parseCellKey', () => {
  it('解析 area: cellKey', () => {
    expect(parseCellKey('area:area-icu:2026-09-14')).toEqual({ kind: 'area', id: 'area-icu', date: '2026-09-14' })
  })

  it('解析 staff: cellKey', () => {
    expect(parseCellKey('staff:staff-007:2026-09-14')).toEqual({ kind: 'staff', id: 'staff-007', date: '2026-09-14' })
  })

  it('格式不對回 null', () => {
    expect(parseCellKey('area-icu-2026-09-14')).toBeNull()
    expect(parseCellKey('unknown:x:2026-09-14')).toBeNull()
    expect(parseCellKey('area::2026-09-14')).toBeNull()
  })
})

describe('tabForCellKey', () => {
  it('area: 對到區域 × 日分頁', () => {
    expect(tabForCellKey('area:area-a:2026-09-01')).toBe('area-by-day')
  })

  it('staff: 對到日 × 人分頁', () => {
    expect(tabForCellKey('staff:staff-001:2026-09-01')).toBe('day-by-staff')
  })

  it('格式不對回 null', () => {
    expect(tabForCellKey('garbage')).toBeNull()
  })
})

describe('domIdForCellKey', () => {
  it('組出穩定的 DOM id', () => {
    expect(domIdForCellKey('area:area-a:2026-09-01')).toBe('sc-cell-area-area-a-2026-09-01')
    expect(domIdForCellKey('staff:staff-001:2026-09-01')).toBe('sc-cell-staff-staff-001-2026-09-01')
  })
})

describe('resolveJumpTarget', () => {
  it('area: 開頭一律留在區域 × 日', () => {
    const target = resolveJumpTarget('area:area-a:2026-09-01', new Map())
    expect(target).toEqual({ tab: 'area-by-day', domId: 'sc-cell-area-area-a-2026-09-01' })
  })

  it('staff: 開頭且查得到當天值班區域時，留在區域 × 日、捲到那個區域格（PR #57 審查回饋 B1）', () => {
    const dutyMapByStaff = new Map([['staff-001|2026-09-14', ['area-icu']]])
    const target = resolveJumpTarget('staff:staff-001:2026-09-14', dutyMapByStaff)
    expect(target).toEqual({ tab: 'area-by-day', domId: 'sc-cell-area-area-icu-2026-09-14' })
  })

  it('同人同日兩區（X1）時跳到第一個區域格（確定性）', () => {
    const dutyMapByStaff = new Map([['staff-001|2026-09-14', ['area-a', 'area-icu']]])
    const target = resolveJumpTarget('staff:staff-001:2026-09-14', dutyMapByStaff)
    expect(target).toEqual({ tab: 'area-by-day', domId: 'sc-cell-area-area-a-2026-09-14' })
  })

  it('staff: 開頭但查不到值班紀錄時，退回切去日 × 人（防禦分支）', () => {
    const target = resolveJumpTarget('staff:staff-001:2026-09-14', new Map())
    expect(target).toEqual({ tab: 'day-by-staff', domId: 'sc-cell-staff-staff-001-2026-09-14' })
  })

  it('格式不對回 null', () => {
    expect(resolveJumpTarget('garbage', new Map())).toBeNull()
  })
})
