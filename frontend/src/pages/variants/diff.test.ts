import { describe, expect, it } from 'vitest'
import type { Duty } from '@/api/types'
import { diffVariants, isVariantSelected } from './diff'

describe('diffVariants', () => {
  it('只回傳兩邊指派不同的格子', () => {
    const a: Duty[] = [
      { areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'area:area-a:2026-09-01' },
      { areaId: 'area-b', date: '2026-09-01', staffId: 's-2', cellKey: 'area:area-b:2026-09-01' },
    ]
    const b: Duty[] = [
      { areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'area:area-a:2026-09-01' },
      { areaId: 'area-b', date: '2026-09-01', staffId: 's-3', cellKey: 'area:area-b:2026-09-01' },
    ]
    const diffs = diffVariants(a, b)
    expect(diffs).toEqual([
      { areaId: 'area-b', date: '2026-09-01', cellKey: 'area:area-b:2026-09-01', from: 's-2', to: 's-3' },
    ])
  })

  it('一邊有值班、一邊空缺也算差異', () => {
    const a: Duty[] = [{ areaId: 'area-icu', date: '2026-09-05', staffId: 's-1', cellKey: 'area:area-icu:2026-09-05' }]
    const b: Duty[] = []
    const diffs = diffVariants(a, b)
    expect(diffs).toEqual([
      { areaId: 'area-icu', date: '2026-09-05', cellKey: 'area:area-icu:2026-09-05', from: 's-1', to: null },
    ])
  })

  it('完全相同回傳空陣列', () => {
    const a: Duty[] = [{ areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'x' }]
    expect(diffVariants(a, a)).toEqual([])
  })

  it('依日期、區域排序', () => {
    const a: Duty[] = []
    const b: Duty[] = [
      { areaId: 'area-b', date: '2026-09-01', staffId: 's-1', cellKey: 'x' },
      { areaId: 'area-a', date: '2026-09-01', staffId: 's-2', cellKey: 'y' },
      { areaId: 'area-a', date: '2026-08-31', staffId: 's-3', cellKey: 'z' },
    ]
    const diffs = diffVariants(a, b)
    expect(diffs.map((d) => `${d.date}:${d.areaId}`)).toEqual([
      '2026-08-31:area-a',
      '2026-09-01:area-a',
      '2026-09-01:area-b',
    ])
  })
})

describe('isVariantSelected', () => {
  it('逐格完全相同視為已選定', () => {
    const duties: Duty[] = [{ areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'x' }]
    expect(isVariantSelected(duties, duties)).toBe(true)
  })
  it('有任何一格不同就不是已選定', () => {
    const variant: Duty[] = [{ areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'x' }]
    const schedule: Duty[] = [{ areaId: 'area-a', date: '2026-09-01', staffId: 's-2', cellKey: 'x' }]
    expect(isVariantSelected(variant, schedule)).toBe(false)
  })
  it('草稿是空班表時不算已選定', () => {
    const variant: Duty[] = [{ areaId: 'area-a', date: '2026-09-01', staffId: 's-1', cellKey: 'x' }]
    expect(isVariantSelected(variant, [])).toBe(false)
  })
  it('變體本身是空陣列時不算已選定，即使草稿也是空的', () => {
    expect(isVariantSelected([], [])).toBe(false)
  })
})
