import { describe, expect, it } from 'vitest'
import { cellKindOf, groupColorIndex } from './cellStyle'

describe('cellKindOf', () => {
  it('違規斜紋蓋過一切（H5）', () => {
    expect(cellKindOf({ hasDuty: true, isHoliday: true, renderKind: 'violation-stripe', isBlocked: true })).toBe(
      'violation-stripe',
    )
  })

  it('違規底色蓋過空缺／登記／值班', () => {
    expect(cellKindOf({ hasDuty: true, isHoliday: false, renderKind: 'violation-bg' })).toBe('violation-bg')
  })

  it('空缺（H1）蓋過登記', () => {
    expect(cellKindOf({ hasDuty: false, isHoliday: false, renderKind: 'vacancy', isBlocked: true })).toBe('vacancy')
  })

  it('沒有違規時，登記蓋過一般值班／空格', () => {
    expect(cellKindOf({ hasDuty: true, isHoliday: false, renderKind: null, isBlocked: true })).toBe('blocked')
    expect(cellKindOf({ hasDuty: false, isHoliday: false, renderKind: null, isBlocked: true })).toBe('blocked')
  })

  it('值班：平日 duty、假日 duty-holiday', () => {
    expect(cellKindOf({ hasDuty: true, isHoliday: false, renderKind: null })).toBe('duty')
    expect(cellKindOf({ hasDuty: true, isHoliday: true, renderKind: null })).toBe('duty-holiday')
  })

  it('空格：平日 empty、假日 holiday（整列底色）', () => {
    expect(cellKindOf({ hasDuty: false, isHoliday: false, renderKind: null })).toBe('empty')
    expect(cellKindOf({ hasDuty: false, isHoliday: true, renderKind: null })).toBe('holiday')
  })

  it('isBlocked 預設為 false', () => {
    expect(cellKindOf({ hasDuty: false, isHoliday: false, renderKind: null })).toBe('empty')
  })
})

describe('groupColorIndex', () => {
  it('0-based 組序對到 1-based 色階', () => {
    expect(groupColorIndex(0)).toBe(1)
    expect(groupColorIndex(1)).toBe(2)
    expect(groupColorIndex(2)).toBe(3)
    expect(groupColorIndex(3)).toBe(4)
  })

  it('超過 4 組時循環', () => {
    expect(groupColorIndex(4)).toBe(1)
    expect(groupColorIndex(5)).toBe(2)
  })
})
