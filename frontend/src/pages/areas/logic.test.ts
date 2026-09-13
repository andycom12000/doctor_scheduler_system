import { describe, expect, it } from 'vitest'
import { reactive } from 'vue'
import {
  areasOfType,
  cloneJson,
  eligibleAreaTypeNames,
  isEqualJson,
  isNonNegativeInteger,
  isPositiveInteger,
  isValidYearMonth,
  monthlyOverrideKey,
  normalizeOverride,
  pointTypeDisplay,
  quotaCapDisplay,
  shiftYearMonth,
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
  it('前提：structuredClone 對 Vue reactive proxy 會丟 DataCloneError（Proxy 帶額外內部 slot）', () => {
    const src = reactive({ ranks: [{ code: 'R6', quotaCap: 5 }] })
    expect(() => structuredClone(src)).toThrow()
  })

  it('接受 Vue reactive proxy（useResource 的 data 就是這種），不像 structuredClone 會丟 DataCloneError', () => {
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

describe('normalizeOverride', () => {
  it('真後端形狀（quotaCapByRank 一律存在但為空物件）與 mock 形狀（省略欄位）正規化後相等', () => {
    const fromRealBackend = { yearMonth: '2026-09', quotaCapByRank: {} }
    const fromMock = { yearMonth: '2026-09' }
    expect(normalizeOverride(fromRealBackend)).toEqual(normalizeOverride(fromMock))
  })

  it('有覆寫時原樣保留', () => {
    expect(normalizeOverride({ yearMonth: '2026-09', quotaCapByRank: { R6: 5 } })).toEqual({
      yearMonth: '2026-09',
      quotaCapByRank: { R6: 5 },
    })
  })

  it('null 回 null', () => {
    expect(normalizeOverride(null)).toBeNull()
  })
})

describe('shiftYearMonth', () => {
  it('同年內加減', () => {
    expect(shiftYearMonth('2026-09', 1)).toBe('2026-10')
    expect(shiftYearMonth('2026-09', -1)).toBe('2026-08')
  })

  it('跨年進位／借位', () => {
    expect(shiftYearMonth('2026-12', 1)).toBe('2027-01')
    expect(shiftYearMonth('2026-01', -1)).toBe('2025-12')
  })

  it('delta 為 0 時原樣回傳', () => {
    expect(shiftYearMonth('2026-09', 0)).toBe('2026-09')
  })
})

describe('monthlyOverrideKey', () => {
  it('組出 settings/monthly-overrides/{ym} 這個 useResource key', () => {
    expect(monthlyOverrideKey('2026-09')).toBe('settings/monthly-overrides/2026-09')
  })
})

describe('isValidYearMonth', () => {
  it('接受 YYYY-MM', () => {
    expect(isValidYearMonth('2026-09')).toBe(true)
    expect(isValidYearMonth('2026-12')).toBe(true)
  })

  it('拒絕不符合格式的字串', () => {
    expect(isValidYearMonth('')).toBe(false)
    expect(isValidYearMonth('2026-9')).toBe(false)
    expect(isValidYearMonth('2026-13')).toBe(false)
    expect(isValidYearMonth('2026/09')).toBe(false)
  })
})
