import { describe, expect, it } from 'vitest'
import { buildCellRenderIndex, renderKindOf } from './violationStyle'
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
