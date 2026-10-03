import { describe, expect, it } from 'vitest'
import { buildCellRenderIndex, projectRenderIndexToAreaView, projectRenderIndexToStaffView, renderKindOf } from './violationStyle'
import type { Violation } from '@/api/types'

function violation(overrides: Partial<Violation> & { code: string; cellKeys: string[] }): Violation {
  return { id: overrides.code, severity: 'hard', message: 'mock', ...overrides }
}

describe('renderKindOf', () => {
  it('H1 空缺 → vacancy', () => {
    expect(renderKindOf('H1_AREA_COVERAGE', 'hard')).toBe('vacancy')
  })

  it('H5 不可排班日 → violation-stripe', () => {
    expect(renderKindOf('H5_BLOCKED_DAY', 'hard')).toBe('violation-stripe')
  })

  it('其他硬違規 → violation-bg', () => {
    expect(renderKindOf('H2_ELIGIBILITY', 'hard')).toBe('violation-bg')
    expect(renderKindOf('H3_QUOTA_CAP', 'hard')).toBe('violation-bg')
    expect(renderKindOf('H4_MIN_GAP', 'hard')).toBe('violation-bg')
  })

  it('X1 同人同日兩區（結構規則）→ 與其他硬違規一致的 violation-bg', () => {
    expect(renderKindOf('X1_STAFF_DOUBLE_BOOKED', 'hard')).toBe('violation-bg')
  })

  it('軟項一律不上格', () => {
    expect(renderKindOf('S3_R2R3_PREFER_ICU', 'soft')).toBeNull()
    expect(renderKindOf('H1_AREA_COVERAGE', 'soft')).toBeNull()
  })
})

describe('buildCellRenderIndex', () => {
  it('依優先度取最高：H5 的斜紋蓋過其他硬違規的底色', () => {
    const violations = [
      violation({ code: 'H2_ELIGIBILITY', cellKeys: ['area:area-a:2026-09-05'] }),
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['area:area-a:2026-09-05'] }),
    ]
    const index = buildCellRenderIndex(violations)
    expect(index.get('area:area-a:2026-09-05')).toBe('violation-stripe')
  })

  it('不受清單順序影響（反過來排列結果相同）', () => {
    const violations = [
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['area:area-a:2026-09-05'] }),
      violation({ code: 'H2_ELIGIBILITY', cellKeys: ['area:area-a:2026-09-05'] }),
    ]
    const index = buildCellRenderIndex(violations)
    expect(index.get('area:area-a:2026-09-05')).toBe('violation-stripe')
  })

  it('H1 空缺格單獨出現時是 vacancy', () => {
    const violations = [violation({ code: 'H1_AREA_COVERAGE', cellKeys: ['area:area-c:2026-09-10'] })]
    const index = buildCellRenderIndex(violations)
    expect(index.get('area:area-c:2026-09-10')).toBe('vacancy')
  })

  it('X1 的兩個 area 格都上色', () => {
    const violations = [
      violation({ code: 'X1_STAFF_DOUBLE_BOOKED', cellKeys: ['area:area-a:2026-09-05', 'area:area-b:2026-09-05'] }),
    ]
    const index = buildCellRenderIndex(violations)
    expect(index.get('area:area-a:2026-09-05')).toBe('violation-bg')
    expect(index.get('area:area-b:2026-09-05')).toBe('violation-bg')
  })

  it('軟違規不會進索引', () => {
    const violations = [
      violation({ code: 'S3_R2R3_PREFER_ICU', severity: 'soft', cellKeys: ['area:area-icu:2026-09-05'] }),
    ]
    const index = buildCellRenderIndex(violations)
    expect(index.size).toBe(0)
  })

  it('一筆違規可能命中多個 cellKeys（例如 staff: 的連續格違規）', () => {
    const violations = [
      violation({
        code: 'H4_MIN_GAP',
        cellKeys: ['staff:staff-001:2026-09-14', 'staff:staff-001:2026-09-15'],
      }),
    ]
    const index = buildCellRenderIndex(violations)
    expect(index.get('staff:staff-001:2026-09-14')).toBe('violation-bg')
    expect(index.get('staff:staff-001:2026-09-15')).toBe('violation-bg')
  })
})

describe('projectRenderIndexToStaffView', () => {
  it('把 area: 的 H5 轉到當天在該區值班的人（PR #44 review：H5 在日 × 人畫不出來）', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['area:area-a:2026-09-05'] }),
    ])
    const dutiesByArea = new Map([['area-a|2026-09-05', 'staff-003']])
    const projected = projectRenderIndexToStaffView(index, dutiesByArea)
    expect(projected.get('staff:staff-003:2026-09-05')).toBe('violation-stripe')
    expect(projected.has('area:area-a:2026-09-05')).toBe(false)
  })

  it('H1 空缺轉不出人，跳過（該格本來就沒有值班的人）', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H1_AREA_COVERAGE', cellKeys: ['area:area-c:2026-09-10'] }),
    ])
    const projected = projectRenderIndexToStaffView(index, new Map())
    expect(projected.size).toBe(0)
  })

  it('staff: 開頭的違規原樣併入', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H4_MIN_GAP', cellKeys: ['staff:staff-001:2026-09-14'] }),
    ])
    const projected = projectRenderIndexToStaffView(index, new Map())
    expect(projected.get('staff:staff-001:2026-09-14')).toBe('violation-bg')
  })

  it('同一格 area: 與 staff: 兩邊都命中時，取優先度較高者', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H2_ELIGIBILITY', cellKeys: ['area:area-a:2026-09-05'] }),
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['staff:staff-003:2026-09-05'] }),
    ])
    const dutiesByArea = new Map([['area-a|2026-09-05', 'staff-003']])
    const projected = projectRenderIndexToStaffView(index, dutiesByArea)
    expect(projected.get('staff:staff-003:2026-09-05')).toBe('violation-stripe')
  })
})

describe('projectRenderIndexToAreaView', () => {
  it('把 staff: 的 H4 轉到當天值班的區域格（PR #57 審查回饋 B1：區域 × 日原本畫不出這幾條）', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H4_MIN_GAP', cellKeys: ['staff:staff-001:2026-09-14'] }),
    ])
    const dutiesByStaff = new Map([['staff-001|2026-09-14', ['area-icu']]])
    const projected = projectRenderIndexToAreaView(index, dutiesByStaff)
    expect(projected.get('area:area-icu:2026-09-14')).toBe('violation-bg')
    expect(projected.has('staff:staff-001:2026-09-14')).toBe(false)
  })

  it('同人同日兩區（X1）時 staff: 違規兩個區域格都投影', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H3_QUOTA_CAP', cellKeys: ['staff:staff-001:2026-09-14'] }),
    ])
    const projected = projectRenderIndexToAreaView(index, new Map([['staff-001|2026-09-14', ['area-a', 'area-icu']]]))
    expect(projected.get('area:area-a:2026-09-14')).toBe('violation-bg')
    expect(projected.get('area:area-icu:2026-09-14')).toBe('violation-bg')
  })

  it('查不到當天值班區域就跳過（理論上不該發生，防禦用）', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H4_MIN_GAP', cellKeys: ['staff:staff-001:2026-09-14'] }),
    ])
    const projected = projectRenderIndexToAreaView(index, new Map())
    expect(projected.size).toBe(0)
  })

  it('area: 開頭的違規原樣併入', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['area:area-a:2026-09-05'] }),
    ])
    const projected = projectRenderIndexToAreaView(index, new Map())
    expect(projected.get('area:area-a:2026-09-05')).toBe('violation-stripe')
  })

  it('同一格 area: 與 staff: 兩邊都命中時，取優先度較高者', () => {
    const index = buildCellRenderIndex([
      violation({ code: 'H2_ELIGIBILITY', cellKeys: ['area:area-a:2026-09-05'] }),
      violation({ code: 'H5_BLOCKED_DAY', cellKeys: ['staff:staff-003:2026-09-05'] }),
    ])
    const dutiesByStaff = new Map([['staff-003|2026-09-05', ['area-a']]])
    const projected = projectRenderIndexToAreaView(index, dutiesByStaff)
    expect(projected.get('area:area-a:2026-09-05')).toBe('violation-stripe')
  })
})
