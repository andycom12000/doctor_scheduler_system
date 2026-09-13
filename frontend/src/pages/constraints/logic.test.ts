import { describe, expect, it } from 'vitest'
import { reactive } from 'vue'
import { constraintSettings } from '@/mocks/fixtures/constraints'
import {
  cloneJson,
  describeDirection,
  describeHardConstraintParams,
  describeMetric,
  describeParams,
  describeScope,
  isEqualJson,
  isValidWeight,
} from './logic'

function hard(code: string) {
  const found = constraintSettings.hard.find((h) => h.code === code)
  if (!found) throw new Error(`fixture 缺少 ${code}`)
  return found
}

function soft(code: string) {
  const found = constraintSettings.soft.find((s) => s.code === code)
  if (!found) throw new Error(`fixture 缺少 ${code}`)
  return found
}

describe('describeScope', () => {
  it('沒有 scope 時回「全體」（H1、H2、H5）', () => {
    expect(describeScope(hard('H1_AREA_COVERAGE').scope)).toBe('全體')
    expect(describeScope(undefined)).toBe('全體')
    expect(describeScope(null)).toBe('全體')
  })

  it('exemptRankCodes 標「除外」（H3、H4、S1、S2、S7）', () => {
    expect(describeScope(hard('H3_QUOTA_CAP').scope)).toBe('NP 除外')
    expect(describeScope(hard('H4_MIN_GAP').scope)).toBe('NP 除外')
  })

  it('rankCodes 與 areaTypeCodes 一起顯示（S3）', () => {
    expect(describeScope(soft('S3_R2R3_PREFER_ICU').scope)).toBe('R2/R3 · ICU')
  })

  it('rankCodes 與 dayKinds 一起顯示，日類三種不可混淆（S6 NP 避開假日）', () => {
    expect(describeScope(soft('S6_NP_AVOID_HOLIDAY').scope)).toBe('NP · 假日')
  })

  it('日類三種各自的字串不同（平日／假日／國定假日）', () => {
    expect(describeScope({ dayKinds: ['weekday'] })).toBe('平日')
    expect(describeScope({ dayKinds: ['holiday'] })).toBe('假日')
    expect(describeScope({ dayKinds: ['publicHoliday'] })).toBe('國定假日')
  })

  it('只有 rankCodes 時只顯示身分（H6、H7、S5）', () => {
    expect(describeScope(hard('H6_NP_MONTHLY_DAYS').scope)).toBe('NP')
    expect(describeScope(hard('H7_NP_MAX_CONSECUTIVE').scope)).toBe('NP')
  })
})

describe('describeMetric', () => {
  it('Budget／Fairness 原語回度量的中文名稱', () => {
    expect(describeMetric(hard('H3_QUOTA_CAP').metric)).toBe('額度點數')
    expect(describeMetric(hard('H6_NP_MONTHLY_DAYS').metric)).toBe('值班天數')
    expect(describeMetric(soft('S7_FAIRNESS_POINT').metric)).toBe('公平性點數')
  })

  it('沒有 metric 的原語回 null（不印出空字串或 undefined 字樣）', () => {
    expect(describeMetric(hard('H1_AREA_COVERAGE').metric)).toBeNull()
    expect(describeMetric(undefined)).toBeNull()
  })
})

describe('describeParams', () => {
  it('days／cap 顯示對應文字（H4、H6、H7）', () => {
    expect(describeParams(hard('H4_MIN_GAP').params)).toBe('3 天')
    expect(describeParams(hard('H6_NP_MONTHLY_DAYS').params)).toBe('上限 20')
    expect(describeParams(hard('H7_NP_MAX_CONSECUTIVE').params)).toBe('6 天')
  })

  it('只有 direction 時不顯示在 describeParams（避免跟方向文字重複）', () => {
    expect(describeParams(soft('S3_R2R3_PREFER_ICU').params)).toBe('—')
  })

  it('沒有 params 時回「—」', () => {
    expect(describeParams(undefined)).toBe('—')
    expect(describeParams(null)).toBe('—')
  })
})

describe('describeDirection', () => {
  it('prefer 顯示「優先」，avoid 顯示「避開」，不顯示負數', () => {
    expect(describeDirection(soft('S3_R2R3_PREFER_ICU').params)).toBe('優先')
    expect(describeDirection(soft('S4_R4R6_PREFER_CHIEF').params)).toBe('優先')
    expect(describeDirection(soft('S5_NP_LAST_RESORT').params)).toBe('避開')
    expect(describeDirection(soft('S6_NP_AVOID_HOLIDAY').params)).toBe('避開')
  })

  it('Fairness／Consistency 沒有 direction，回 null', () => {
    expect(describeDirection(soft('S1_QUOTA_FAIRNESS').params)).toBeNull()
    expect(describeDirection(soft('S2_AREA_CONSISTENCY').params)).toBeNull()
  })
})

describe('isValidWeight', () => {
  it('接受 0–100 的整數，0 代表停用', () => {
    expect(isValidWeight(0)).toBe(true)
    expect(isValidWeight(100)).toBe(true)
    expect(isValidWeight(40)).toBe(true)
  })

  it('拒絕超出範圍、小數、負數與非數字（含 v-model.number 解析失敗的空字串回退）', () => {
    expect(isValidWeight(101)).toBe(false)
    expect(isValidWeight(-1)).toBe(false)
    expect(isValidWeight(1.5)).toBe(false)
    expect(isValidWeight('')).toBe(false)
    expect(isValidWeight(NaN)).toBe(false)
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
    const src = reactive({ hard: [{ code: 'H1', enabled: true }] })
    expect(() => structuredClone(src)).toThrow()
  })

  it('接受 Vue reactive proxy（useResource 的 data 就是這種），不像 structuredClone 會丟 DataCloneError', () => {
    const src = reactive({ hard: [{ code: 'H1', enabled: true }] })
    const out = cloneJson(src)
    expect(out).toEqual({ hard: [{ code: 'H1', enabled: true }] })
    expect(out.hard).not.toBe(src.hard)
  })
})

describe('describeHardConstraintParams', () => {
  it('metric 與 params 同時存在時兩個都顯示（H6_NP_MONTHLY_DAYS：duty_day + cap）', () => {
    const h6 = hard('H6_NP_MONTHLY_DAYS')
    expect(describeHardConstraintParams(h6.metric, h6.params)).toBe('值班天數 · 上限 20')
  })

  it('只有 metric 時只顯示 metric（H3_QUOTA_CAP）', () => {
    const h3 = hard('H3_QUOTA_CAP')
    expect(describeHardConstraintParams(h3.metric, h3.params)).toBe('額度點數')
  })

  it('只有 params 時只顯示 params（H4_MIN_GAP）', () => {
    const h4 = hard('H4_MIN_GAP')
    expect(describeHardConstraintParams(h4.metric, h4.params)).toBe('3 天')
  })

  it('兩者都沒有時回「—」（H1_AREA_COVERAGE）', () => {
    const h1 = hard('H1_AREA_COVERAGE')
    expect(describeHardConstraintParams(h1.metric, h1.params)).toBe('—')
  })
})
