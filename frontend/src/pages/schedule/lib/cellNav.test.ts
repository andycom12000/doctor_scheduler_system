import { describe, expect, it } from 'vitest'
import { domIdForCellKey, parseCellKey, tabForCellKey } from './cellNav'

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
