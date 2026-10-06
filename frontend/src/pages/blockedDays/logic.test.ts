import { describe, expect, it } from 'vitest'
import { computeFeasibility } from '@/mocks/domain'
import { resetStore, store } from '@/mocks/store'
import type {
  Area,
  AreaType,
  BlockedDayMutationResult,
  BlockedDayRegistration,
  CalendarDay,
  EligibilityMatrix,
  FeasibilityReport,
  Rank,
  RankGroup,
  Staff,
} from '@/api/types'
import {
  applyBlockedDayMutation,
  buildGroupViews,
  buildShortageDays,
  chiefAreaTypeCode,
  chiefAvailabilityByDate,
  computeSupplyRatio,
  dateCountMap,
  evaluateWardSqueezeHint,
  extractBusyJobId,
  groupCapNote,
  isChiefAvailabilityTight,
  overCapEntries,
  requiredPerDayOfType,
  totalRegisteredCount,
  unregisteredEntries,
} from './logic'

const areaTypes: AreaType[] = [
  { code: 'WARD', name: '一般病房' },
  { code: 'ICU', name: '加護病房' },
  { code: 'CHIEF', name: '總值' },
]
const areaTypeNameByCode = Object.fromEntries(areaTypes.map((t) => [t.code, t.name]))

const groups: RankGroup[] = [
  { code: 'JUNIOR', name: '低年級' },
  { code: 'MID', name: '中階' },
  { code: 'SENIOR', name: '資深' },
  { code: 'NP', name: 'NP' },
]

const ranks: Rank[] = [
  { code: 'PGY1', name: 'PGY1', groupCode: 'JUNIOR', quotaCap: 10, pointType: 'A' },
  { code: 'R2', name: 'R2', groupCode: 'MID', quotaCap: 8, pointType: 'A' },
  { code: 'R4', name: 'R4', groupCode: 'SENIOR', quotaCap: 6, pointType: 'B' },
  { code: 'NP', name: 'NP', groupCode: 'NP', quotaCap: null, pointType: null },
]

const matrix: EligibilityMatrix['matrix'] = {
  PGY1: { WARD: true, ICU: false, CHIEF: false },
  R2: { WARD: true, ICU: true, CHIEF: false },
  R4: { WARD: false, ICU: true, CHIEF: true },
  NP: { WARD: true, ICU: false, CHIEF: false },
}

const staff: Staff[] = [
  { id: 's-pgy1', employeeNo: 'E1', name: '陳建宏', rankCode: 'PGY1', status: 'active', eligibleAreaTypes: ['WARD'] },
  { id: 's-r2', employeeNo: 'E2', name: '許志明', rankCode: 'R2', status: 'active', eligibleAreaTypes: ['WARD', 'ICU'] },
  {
    id: 's-r4',
    employeeNo: 'E3',
    name: '賴怡君',
    rankCode: 'R4',
    status: 'active',
    eligibleAreaTypes: ['ICU', 'CHIEF'],
  },
  { id: 's-np', employeeNo: 'E4', name: '游芷若', rankCode: 'NP', status: 'active', eligibleAreaTypes: ['WARD'] },
  {
    id: 's-inactive',
    employeeNo: 'E5',
    name: '停用者',
    rankCode: 'R4',
    status: 'inactive',
    eligibleAreaTypes: ['ICU', 'CHIEF'],
  },
]

function calendarDay(date: string, overrides: Partial<CalendarDay> = {}): CalendarDay {
  return {
    date,
    weekday: 3,
    isHoliday: false,
    isPublicHoliday: false,
    isMakeUpWorkday: false,
    holidayName: null,
    quotaPointValue: 1,
    overridden: false,
    ...overrides,
  }
}

describe('chiefAreaTypeCode', () => {
  it('優先用 bySupply 最窄那層（單一代碼）', () => {
    const bySupply: FeasibilityReport['bySupply'] = [
      { areaTypeCodes: ['CHIEF'], demandPoints: 1, supplyPoints: 1, headroom: 0 },
      { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 2, supplyPoints: 2, headroom: 0 },
      { areaTypeCodes: ['CHIEF', 'ICU', 'WARD'], demandPoints: 3, supplyPoints: 3, headroom: 0 },
    ]
    expect(chiefAreaTypeCode({ bySupply, matrix, areaTypes })).toBe('CHIEF')
  })

  it('沒有可行性資料時，從資格矩陣挑可值人數最少的區域類型', () => {
    expect(chiefAreaTypeCode({ bySupply: [], matrix, areaTypes })).toBe('CHIEF')
  })

  it('bySupply 最窄層不是單一代碼時忽略，改用資格矩陣推導', () => {
    const bySupply: FeasibilityReport['bySupply'] = [
      { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 1, supplyPoints: 1, headroom: 0 },
    ]
    expect(chiefAreaTypeCode({ bySupply, matrix, areaTypes })).toBe('CHIEF')
  })

  it('兩者都沒有資料時 fallback 回 CHIEF', () => {
    expect(chiefAreaTypeCode({ bySupply: [], matrix: {}, areaTypes: [] })).toBe('CHIEF')
  })

  it('矩陣是空物件（尚未載入）但 areaTypes 已有資料時，不要誤判成 areaTypes 裡第一個代碼', () => {
    // 矩陣是 {} 時每個區域類型的可值人數都會算成 0，若不擋這個情況，
    // 排最前面的 WARD 會被誤判成「可值人數最少」，直接 fallback 回 CHIEF 才對。
    expect(chiefAreaTypeCode({ bySupply: [], matrix: {}, areaTypes })).toBe('CHIEF')
  })
})

describe('groupCapNote', () => {
  it('組出可值區域類型的說明文字', () => {
    expect(groupCapNote({ groupCode: 'JUNIOR', ranks, matrix, areaTypeNameByCode })).toBe('可值：一般病房')
    expect(groupCapNote({ groupCode: 'MID', ranks, matrix, areaTypeNameByCode })).toBe('可值：一般病房、加護病房')
    expect(groupCapNote({ groupCode: 'SENIOR', ranks, matrix, areaTypeNameByCode })).toBe('可值：加護病房、總值')
  })

  it('組內身分全部 quotaCap 為 null 時附註額度不計', () => {
    expect(groupCapNote({ groupCode: 'NP', ranks, matrix, areaTypeNameByCode })).toBe('可值：一般病房（額度不計）')
  })
})

describe('buildGroupViews', () => {
  const byStaff: BlockedDayRegistration['byStaff'] = [
    { staffId: 's-pgy1', count: 3, remaining: 13 },
    { staffId: 's-r2', count: 0, remaining: 8 },
    { staffId: 's-r4', count: 16, remaining: 0 },
    { staffId: 's-np', count: 1, remaining: 15 },
  ]
  const days = [calendarDay('2026-09-01'), calendarDay('2026-09-06', { isHoliday: true })]
  const entries = [
    { staffId: 's-pgy1', date: '2026-09-01' },
    { staffId: 's-r4', date: '2026-09-06' },
  ]

  it('依身分組分區、跳過停用者與空組', () => {
    const result = buildGroupViews({
      staff,
      ranks,
      groups,
      byStaff,
      entries,
      days,
      monthlyCap: 16,
      eligibilityMatrix: matrix,
      areaTypeNameByCode,
    })
    expect(result.map((g) => g.groupCode)).toEqual(['JUNIOR', 'MID', 'SENIOR', 'NP'])
    expect(result.every((g) => g.rows.every((r) => r.staffId !== 's-inactive'))).toBe(true)
  })

  it('每格標出是否被登記與是否為假日', () => {
    const result = buildGroupViews({
      staff,
      ranks,
      groups,
      byStaff,
      entries,
      days,
      monthlyCap: 16,
      eligibilityMatrix: matrix,
      areaTypeNameByCode,
    })
    const junior = result.find((g) => g.groupCode === 'JUNIOR')
    const row = junior?.rows.find((r) => r.staffId === 's-pgy1')
    expect(row?.cells).toEqual([
      { date: '2026-09-01', blocked: true, isHoliday: false },
      { date: '2026-09-06', blocked: false, isHoliday: true },
    ])
  })

  it('remaining 為 0（已達上限）的人 overCap 為 true', () => {
    const result = buildGroupViews({
      staff,
      ranks,
      groups,
      byStaff,
      entries,
      days,
      monthlyCap: 16,
      eligibilityMatrix: matrix,
      areaTypeNameByCode,
    })
    const senior = result.find((g) => g.groupCode === 'SENIOR')
    expect(senior?.rows.find((r) => r.staffId === 's-r4')?.overCap).toBe(true)
    const junior = result.find((g) => g.groupCode === 'JUNIOR')
    expect(junior?.rows.find((r) => r.staffId === 's-pgy1')?.overCap).toBe(false)
  })

  it('groupIndex 對齊 groups 陣列的原始位置，不受過濾空組影響', () => {
    const groupsWithEmptyOne: RankGroup[] = [
      { code: 'JUNIOR', name: '低年級' },
      { code: 'EMPTY', name: '空組' },
      { code: 'MID', name: '中階' },
      { code: 'SENIOR', name: '資深' },
      { code: 'NP', name: 'NP' },
    ]
    const result = buildGroupViews({
      staff,
      ranks,
      groups: groupsWithEmptyOne,
      byStaff,
      entries,
      days,
      monthlyCap: 16,
      eligibilityMatrix: matrix,
      areaTypeNameByCode,
    })
    // EMPTY（原始索引 1）沒有在職人員被過濾掉；SENIOR 仍要對齊它在原始陣列裡的索引 3，
    // 不是過濾後排在第三位（索引 2）。
    expect(result.map((g) => g.groupCode)).toEqual(['JUNIOR', 'MID', 'SENIOR', 'NP'])
    expect(result.find((g) => g.groupCode === 'JUNIOR')?.groupIndex).toBe(0)
    expect(result.find((g) => g.groupCode === 'MID')?.groupIndex).toBe(2)
    expect(result.find((g) => g.groupCode === 'SENIOR')?.groupIndex).toBe(3)
    expect(result.find((g) => g.groupCode === 'NP')?.groupIndex).toBe(4)
  })
})

describe('dateCountMap', () => {
  it('沒登記的日子補 0', () => {
    const map = dateCountMap([{ date: '2026-09-06', count: 3 }], ['2026-09-05', '2026-09-06'])
    expect(map.get('2026-09-05')).toBe(0)
    expect(map.get('2026-09-06')).toBe(3)
  })
})

describe('chiefAvailabilityByDate', () => {
  it('只計入有資格值總值、當天未登記的在職人員', () => {
    const map = chiefAvailabilityByDate({
      staff,
      matrix,
      chiefCode: 'CHIEF',
      entries: [{ staffId: 's-r4', date: '2026-09-06' }],
      days: ['2026-09-01', '2026-09-06'],
    })
    // 只有 s-r4 對 CHIEF 有資格；停用的 s-inactive 不計。
    expect(map.get('2026-09-01')).toBe(1)
    expect(map.get('2026-09-06')).toBe(0)
  })

  it('chiefCode 為 null（找不到總值區域類型）時全部回 0', () => {
    const map = chiefAvailabilityByDate({ staff, matrix, chiefCode: null, entries: [], days: ['2026-09-01'] })
    expect(map.get('2026-09-01')).toBe(0)
  })
})

describe('overCapEntries / unregisteredEntries / totalRegisteredCount', () => {
  const byStaff: BlockedDayRegistration['byStaff'] = [
    { staffId: 's-a', count: 0, remaining: 16 },
    { staffId: 's-b', count: 16, remaining: 0 },
    { staffId: 's-c', count: 5, remaining: 11 },
  ]
  const nameById = new Map([
    ['s-a', 'A'],
    ['s-b', 'B'],
    ['s-c', 'C'],
  ])

  it('overCapEntries 只列出 remaining 為 0（已達上限）的人', () => {
    expect(overCapEntries(byStaff, nameById)).toEqual([{ staffId: 's-b', name: 'B', count: 16 }])
  })

  it('unregisteredEntries 只列出 count 為 0 的人', () => {
    expect(unregisteredEntries(byStaff, nameById)).toEqual([{ staffId: 's-a', name: 'A', count: 0 }])
  })

  it('totalRegisteredCount 加總全部人的 count', () => {
    expect(totalRegisteredCount(byStaff)).toBe(21)
  })
})

describe('buildShortageDays', () => {
  const byDate: FeasibilityReport['byDate'] = [
    { date: '2026-09-01', shortages: [] },
    {
      date: '2026-09-02',
      shortages: [{ areaTypeCode: 'ICU', required: 1, availableStaff: 0 }],
    },
    {
      date: '2026-09-03',
      shortages: [
        { areaTypeCode: 'WARD', required: 3, availableStaff: 2 },
        { areaTypeCode: 'CHIEF', required: 1, availableStaff: 0 },
      ],
    },
  ]

  it('只列出有缺口的日子，總值缺口排最前面', () => {
    const result = buildShortageDays(byDate, 'CHIEF', areaTypeNameByCode)
    expect(result.map((d) => d.date)).toEqual(['2026-09-03', '2026-09-02'])
    expect(result[0].hasChiefShortage).toBe(true)
    expect(result[0].text).toBe('總值不足（可用 0／需 1）、一般病房不足（可用 2／需 3）')
    expect(result[1].hasChiefShortage).toBe(false)
  })
})

type Tiers = FeasibilityReport['bySupply']

/** 三層巢狀供需：只有最後一層（一般病房）的邊際供給／需求會被用到。 */
function tiers(wardSupply: number, wardDemand: number): Tiers {
  return [
    { areaTypeCodes: ['CHIEF'], demandPoints: 30, supplyPoints: 60, headroom: 30 },
    { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 60, supplyPoints: 120, headroom: 60 },
    {
      areaTypeCodes: ['CHIEF', 'ICU', 'WARD'],
      demandPoints: 60 + wardDemand,
      supplyPoints: 120 + wardSupply,
      headroom: 60 + wardSupply - wardDemand,
    },
  ]
}

describe('evaluateWardSqueezeHint', () => {
  // 基準：邊際供給 200、邊際需求 100，比值 2.0；5% 的界線是 1.9。
  const baseline = tiers(200, 100)

  it('層數不是 3 時回 null（資格非巢狀不顯示）', () => {
    expect(evaluateWardSqueezeHint([], baseline)).toBeNull()
    expect(
      evaluateWardSqueezeHint([{ areaTypeCodes: ['CHIEF'], demandPoints: 1, supplyPoints: 1, headroom: 0 }], baseline),
    ).toBeNull()
  })

  it('目前等於基準（零登記）時不顯示', () => {
    const hint = evaluateWardSqueezeHint(tiers(200, 100), baseline)
    expect(hint?.ratio).toBeCloseTo(2, 10)
    expect(hint?.baselineRatio).toBeCloseTo(2, 10)
    expect(hint?.show).toBe(false)
  })

  it('比基準下降超過 5% 時顯示', () => {
    expect(evaluateWardSqueezeHint(tiers(189, 100), baseline)?.show).toBe(true)
  })

  it('剛好下降 5% 不顯示', () => {
    const hint = evaluateWardSqueezeHint(tiers(190, 100), baseline)
    expect(hint?.ratio).toBeCloseTo(1.9, 10)
    expect(hint?.show).toBe(false)
  })

  it('比值高於基準不顯示', () => {
    expect(evaluateWardSqueezeHint(tiers(250, 100), baseline)?.show).toBe(false)
  })

  it('基準比值本身小於 1（參考名單的常態）也只看相對下降', () => {
    const lowBaseline = tiers(87, 100)
    expect(evaluateWardSqueezeHint(tiers(87, 100), lowBaseline)?.show).toBe(false)
    expect(evaluateWardSqueezeHint(tiers(80, 100), lowBaseline)?.show).toBe(true)
  })

  it('基準缺失時不顯示', () => {
    expect(evaluateWardSqueezeHint(tiers(50, 100), undefined)?.show).toBe(false)
    expect(evaluateWardSqueezeHint(tiers(50, 100), null)?.show).toBe(false)
    expect(evaluateWardSqueezeHint(tiers(50, 100), [])?.show).toBe(false)
  })

  it('基準邊際需求為 0 或負值時不顯示', () => {
    const zeroDemand = tiers(200, 0)
    const hint = evaluateWardSqueezeHint(tiers(50, 100), zeroDemand)
    expect(hint?.baselineRatio).toBeNull()
    expect(hint?.show).toBe(false)
  })

  it('目前邊際需求為 0 時不顯示', () => {
    const hint = evaluateWardSqueezeHint(tiers(200, 0), baseline)
    expect(hint?.ratio).toBeNull()
    expect(hint?.show).toBe(false)
  })
})

describe('evaluateWardSqueezeHint · 參考名單真實情境', () => {
  // 用 mock 的可行性演算（與真後端同一套巢狀累計），名冊是 34 人參考名單。
  it('零登記時目前等於基準，不顯示提示', () => {
    resetStore()
    expect(store.blockedDays.get('2026-10') ?? []).toHaveLength(0)
    const report = computeFeasibility(store, '2026-10')
    expect(report.baselineBySupply).toEqual(report.bySupply)
    const hint = evaluateWardSqueezeHint(report.bySupply, report.baselineBySupply)
    expect(hint?.ratio).toBeCloseTo(hint!.baselineRatio!, 10)
    expect(hint?.show).toBe(false)
  })

  it('低年級登記到額度上限撐不滿（只剩 10/29 到 10/31 三天可排）後比值比基準低、顯示提示；基準不受登記影響', () => {
    resetStore()
    const baseline = computeFeasibility(store, '2026-10').baselineBySupply
    const juniors = store.staff.filter((s) => s.status === 'active' && s.eligibleAreaTypes.length === 1)
    store.blockedDays.set(
      '2026-10',
      juniors.flatMap((s) =>
        Array.from({ length: 28 }, (_, i) => ({ staffId: s.id, date: `2026-10-${String(i + 1).padStart(2, '0')}` })),
      ),
    )
    const report = computeFeasibility(store, '2026-10')
    expect(report.baselineBySupply).toEqual(baseline)
    const hint = evaluateWardSqueezeHint(report.bySupply, report.baselineBySupply)
    expect(hint!.ratio!).toBeLessThan(hint!.baselineRatio!)
    expect(hint?.show).toBe(true)
    resetStore()
  })
})

describe('requiredPerDayOfType / isChiefAvailabilityTight', () => {
  const areas: Area[] = [
    { id: 'a', code: 'A', name: 'A', areaTypeCode: 'WARD', requiredPerDay: 1 },
    { id: 'c', code: 'CHIEF', name: '總值', areaTypeCode: 'CHIEF', requiredPerDay: 1 },
    { id: 'c2', code: 'CHIEF2', name: '總值二', areaTypeCode: 'CHIEF', requiredPerDay: 1 },
  ]

  it('同類型底下各區域的需求加總', () => {
    expect(requiredPerDayOfType(areas, 'CHIEF')).toBe(2)
    expect(requiredPerDayOfType(areas, 'WARD')).toBe(1)
  })

  it('設定還沒載入或找不到類型時回 1（出廠值），門檻不會變成 0', () => {
    expect(requiredPerDayOfType([], 'CHIEF')).toBe(1)
    expect(requiredPerDayOfType(areas, 'NOPE')).toBe(1)
    expect(requiredPerDayOfType(areas, null)).toBe(1)
  })

  it('標紅門檻 = 需求 + 1；需求 1 時等同舊的 ≤ 2', () => {
    expect(isChiefAvailabilityTight(2, 1)).toBe(true)
    expect(isChiefAvailabilityTight(3, 1)).toBe(false)
  })

  it('需求變 2 人時，剩 3 人就標紅（寫死的 2 會漏掉）', () => {
    expect(isChiefAvailabilityTight(3, 2)).toBe(true)
    expect(isChiefAvailabilityTight(4, 2)).toBe(false)
  })
})

describe('computeSupplyRatio', () => {
  it('倍率是供給/需求，格式化成一位小數的 ×', () => {
    const view = computeSupplyRatio({ demandPoints: 31, supplyPoints: 281, headroom: 250 })
    expect(view.ratio).toBeCloseTo(281 / 31, 5)
    expect(view.ratioLabel).toBe('9.1×')
    expect(view.headroom).toBe(250)
  })

  it('進度條是需求/供給的百分比（利用率），跟倍率方向相反', () => {
    const view = computeSupplyRatio({ demandPoints: 31, supplyPoints: 281, headroom: 250 })
    expect(view.utilizationPercent).toBe(Math.round((31 / 281) * 100))
  })

  it('需求為 0 時倍率無意義，回 null／—，利用率為 0', () => {
    const view = computeSupplyRatio({ demandPoints: 0, supplyPoints: 100, headroom: 100 })
    expect(view.ratio).toBeNull()
    expect(view.ratioLabel).toBe('—')
    expect(view.utilizationPercent).toBe(0)
  })

  it('供給 ≤ 0 但仍有需求時利用率回 100（滿條，不是 0；審查回饋 N1：供給掛零是最嚴重情況）', () => {
    const view = computeSupplyRatio({ demandPoints: 10, supplyPoints: 0, headroom: -10 })
    expect(view.utilizationPercent).toBe(100)
  })

  it('供給與需求都 ≤ 0（沒有這一層的需求）時利用率回 0，不除以零', () => {
    const view = computeSupplyRatio({ demandPoints: 0, supplyPoints: 0, headroom: 0 })
    expect(view.utilizationPercent).toBe(0)
  })

  it('利用率上限封頂在 100（需求超過供給時，例如登記過量）', () => {
    const view = computeSupplyRatio({ demandPoints: 150, supplyPoints: 100, headroom: -50 })
    expect(view.utilizationPercent).toBe(100)
    expect(view.ratio).toBeCloseTo(100 / 150, 5)
  })
})

describe('extractBusyJobId', () => {
  it('從 ErrorResponse.error.details.jobId 取值', () => {
    expect(extractBusyJobId({ error: { code: 'SOLVER_BUSY', message: 'x', details: { jobId: 'job-1' } } })).toBe(
      'job-1',
    )
  })

  it('形狀不對時回 null', () => {
    expect(extractBusyJobId(null)).toBeNull()
    expect(extractBusyJobId({})).toBeNull()
    expect(extractBusyJobId({ error: {} })).toBeNull()
    expect(extractBusyJobId({ error: { details: {} } })).toBeNull()
    expect(extractBusyJobId({ error: { details: { jobId: 42 } } })).toBeNull()
  })
})

describe('applyBlockedDayMutation', () => {
  function registration(): BlockedDayRegistration {
    return {
      yearMonth: '2026-09',
      monthlyCap: 16,
      entries: [{ staffId: 's-a', date: '2026-09-05' }],
      byStaff: [{ staffId: 's-a', count: 1, remaining: 15 }],
      byDate: [{ date: '2026-09-05', count: 1 }],
    }
  }

  it('登記：加入 entries、更新該人與該日計數', () => {
    const reg = registration()
    const result: BlockedDayMutationResult = { staffTotals: { count: 2, remaining: 14 }, dateTotals: { count: 1 } }
    applyBlockedDayMutation(reg, 's-a', '2026-09-06', true, result)
    expect(reg.entries).toEqual([
      { staffId: 's-a', date: '2026-09-05' },
      { staffId: 's-a', date: '2026-09-06' },
    ])
    expect(reg.byStaff[0]).toEqual({ staffId: 's-a', count: 2, remaining: 14 })
    expect(reg.byDate.find((d) => d.date === '2026-09-06')).toEqual({ date: '2026-09-06', count: 1 })
  })

  it('清除：移出 entries；該日計數歸零時從 byDate 移除', () => {
    const reg = registration()
    const result: BlockedDayMutationResult = { staffTotals: { count: 0, remaining: 16 }, dateTotals: { count: 0 } }
    applyBlockedDayMutation(reg, 's-a', '2026-09-05', false, result)
    expect(reg.entries).toEqual([])
    expect(reg.byStaff[0]).toEqual({ staffId: 's-a', count: 0, remaining: 16 })
    expect(reg.byDate).toEqual([])
  })

  it('對已經是目標狀態的格子重複套用不會產生重複 entry', () => {
    const reg = registration()
    const result: BlockedDayMutationResult = { staffTotals: { count: 1, remaining: 15 }, dateTotals: { count: 1 } }
    applyBlockedDayMutation(reg, 's-a', '2026-09-05', true, result)
    expect(reg.entries).toEqual([{ staffId: 's-a', date: '2026-09-05' }])
  })
})
