import { describe, expect, it } from 'vitest'
import { reactive } from 'vue'
import {
  areasOfType,
  areaTypeCapacityNote,
  cloneJson,
  eligibleAreaTypeNames,
  isEqualJson,
  isNonNegativeInteger,
  isPositiveInteger,
  monthlyOverrideKey,
  normalizeOverride,
  pointTypeDisplay,
  quotaCapDisplay,
  s7Status,
} from './logic'
import { areaTypes, areas } from '@/mocks/fixtures/areas'
import { constraintSettings } from '@/mocks/fixtures/constraints'
import { eligibilityMatrix } from '@/mocks/fixtures/ranks'
import type { ConstraintSettings } from '@/api/types'

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

describe('areaTypeCapacityNote', () => {
  it('兩個以上區域且 requiredPerDay 一致時顯示每日與每區兩個數字（WARD：A/B/C 三區）', () => {
    expect(areaTypeCapacityNote('WARD', areas)).toBe('每日 3 人 · 每區 1 人')
  })

  it('只有一個區域時只顯示每日，不重複印每區（ICU／總值就是這樣，設計稿同此）', () => {
    expect(areaTypeCapacityNote('ICU', areas)).toBe('每日 1 人')
    expect(areaTypeCapacityNote('CHIEF', areas)).toBe('每日 1 人')
  })

  it('兩個以上區域但 requiredPerDay 不一致時只顯示每日，不硬湊每區數字', () => {
    const mixed = [
      { id: 'x', code: 'X', name: 'X', areaTypeCode: 'MIXED', requiredPerDay: 1 },
      { id: 'y', code: 'Y', name: 'Y', areaTypeCode: 'MIXED', requiredPerDay: 2 },
    ]
    expect(areaTypeCapacityNote('MIXED', mixed)).toBe('每日 3 人')
  })

  it('該類型底下沒有區域時回空字串', () => {
    expect(areaTypeCapacityNote('GHOST', areas)).toBe('')
  })
})

describe('s7Status', () => {
  it('權重 0（出廠預設）視為停用', () => {
    expect(s7Status(constraintSettings)).toEqual({ label: 'S7 權重 0 · 停用', enabled: false })
  })

  it('權重 > 0 視為啟用', () => {
    const edited: ConstraintSettings = {
      ...constraintSettings,
      soft: constraintSettings.soft.map((soft) =>
        soft.code === 'S7_FAIRNESS_POINT' ? { ...soft, weight: 40 } : soft,
      ),
    }
    expect(s7Status(edited)).toEqual({ label: 'S7 權重 40 · 啟用', enabled: true })
  })

  it('設定還沒載入（null）回 null', () => {
    expect(s7Status(null)).toBeNull()
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

describe('monthlyOverrideKey', () => {
  it('組出 settings/monthly-overrides/{ym} 這個 useResource key', () => {
    expect(monthlyOverrideKey('2026-09')).toBe('settings/monthly-overrides/2026-09')
  })
})
