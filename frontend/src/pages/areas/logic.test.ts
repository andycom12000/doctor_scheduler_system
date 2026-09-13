import { describe, expect, it } from 'vitest'
import { reactive } from 'vue'
import {
  areasOfType,
  cloneJson,
  eligibleAreaTypeNames,
  isEqualJson,
  isNonNegativeInteger,
  isPositiveInteger,
  pointTypeDisplay,
  quotaCapDisplay,
} from './logic'
import { areaTypes, areas } from '@/mocks/fixtures/areas'
import { eligibilityMatrix } from '@/mocks/fixtures/ranks'

describe('eligibleAreaTypeNames', () => {
  it('依 areaTypes 給定的順序 join 可值的區域類型名稱', () => {
    expect(eligibleAreaTypeNames(eligibilityMatrix.matrix, 'R2', areaTypes)).toBe('一般病房、加護病房')
  })

  it('全部不可值時回「—」', () => {
    const matrix = { X: { WARD: false, ICU: false, CHIEF: false } }
    expect(eligibleAreaTypeNames(matrix, 'X', areaTypes)).toBe('—')
  })

  it('查不到該身分時回「—」', () => {
    expect(eligibleAreaTypeNames({}, 'GHOST', areaTypes)).toBe('—')
  })
})

describe('quotaCapDisplay／pointTypeDisplay', () => {
  it('NP 的 null 顯示「不計」', () => {
    expect(quotaCapDisplay(null)).toBe('不計')
    expect(pointTypeDisplay(null)).toBe('不計')
  })

  it('其餘身分顯示實際數值／類型', () => {
    expect(quotaCapDisplay(8)).toBe('8')
    expect(pointTypeDisplay('A')).toBe('Type A')
  })
})

describe('areasOfType', () => {
  it('回傳屬於該區域類型的區域，維持原順序', () => {
    expect(areasOfType('WARD', areas).map((a) => a.code)).toEqual(['A', 'B', 'C'])
    expect(areasOfType('ICU', areas).map((a) => a.code)).toEqual(['ICU'])
  })
})

describe('isNonNegativeInteger／isPositiveInteger', () => {
  it('接受非負整數', () => {
    expect(isNonNegativeInteger(0)).toBe(true)
    expect(isNonNegativeInteger(5)).toBe(true)
  })

  it('拒絕負數、小數、字串（含空字串，v-model.number 解析失敗的回退值）', () => {
    expect(isNonNegativeInteger(-1)).toBe(false)
    expect(isNonNegativeInteger(1.5)).toBe(false)
    expect(isNonNegativeInteger('')).toBe(false)
    expect(isNonNegativeInteger('3')).toBe(false)
    expect(isNonNegativeInteger(NaN)).toBe(false)
  })

  it('isPositiveInteger 額外拒絕 0', () => {
    expect(isPositiveInteger(0)).toBe(false)
    expect(isPositiveInteger(1)).toBe(true)
  })
})

describe('isEqualJson', () => {
  it('相同內容視為相等，即使是不同物件實例', () => {
    expect(isEqualJson({ a: 1, b: [1, 2] }, { a: 1, b: [1, 2] })).toBe(true)
  })

  it('內容不同時視為不相等', () => {
    expect(isEqualJson({ a: 1 }, { a: 2 })).toBe(false)
    expect(isEqualJson(null, { a: 1 })).toBe(false)
  })
})

describe('cloneJson', () => {
  it('接受 Vue reactive proxy（useResource 的 data 就是這種），structuredClone 會丟 DataCloneError', () => {
    const src = reactive({ ranks: [{ code: 'R6', quotaCap: 5 }] })
    const out = cloneJson(src)
    expect(out).toEqual({ ranks: [{ code: 'R6', quotaCap: 5 }] })
    expect(out.ranks).not.toBe(src.ranks)
  })

  it('回傳的是獨立物件，改動不影響原始資料', () => {
    const src = { a: 1 }
    const out = cloneJson(src)
    out.a = 2
    expect(src.a).toBe(1)
  })
})
