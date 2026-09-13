import { describe, expect, it } from 'vitest'
import type {
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
  dateCountMap,
  evaluateWardSqueezeHint,
  extractBusyJobId,
  groupCapNote,
  overCapEntries,
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
  it('以 AreaType.name 找出「總值」的代碼，不寫死字串', () => {
    expect(chiefAreaTypeCode(areaTypes)).toBe('CHIEF')
  })

  it('找不到就回 null', () => {
    expect(chiefAreaTypeCode([{ code: 'X', name: 'Y' }])).toBeNull()
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
    { staffId: 's-r4', count: 17, remaining: -1 },
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

  it('超過月上限的人 overCap 為 true', () => {
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
    { staffId: 's-b', count: 17, remaining: -1 },
    { staffId: 's-c', count: 5, remaining: 11 },
  ]
  const nameById = new Map([
    ['s-a', 'A'],
    ['s-b', 'B'],
    ['s-c', 'C'],
  ])

  it('overCapEntries 只列出超過上限的人', () => {
    expect(overCapEntries(byStaff, nameById, 16)).toEqual([{ staffId: 's-b', name: 'B', count: 17 }])
  })

  it('unregisteredEntries 只列出 count 為 0 的人', () => {
    expect(unregisteredEntries(byStaff, nameById)).toEqual([{ staffId: 's-a', name: 'A', count: 0 }])
  })

  it('totalRegisteredCount 加總全部人的 count', () => {
    expect(totalRegisteredCount(byStaff)).toBe(22)
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

describe('evaluateWardSqueezeHint', () => {
  it('層數不是 3 時回 null（資格非巢狀不顯示）', () => {
    expect(evaluateWardSqueezeHint([])).toBeNull()
    expect(
      evaluateWardSqueezeHint([{ areaTypeCodes: ['CHIEF'], demandPoints: 1, supplyPoints: 1, headroom: 0 }]),
    ).toBeNull()
  })

  it('邊際供給遠大於邊際需求時不顯示', () => {
    const bySupply: FeasibilityReport['bySupply'] = [
      { areaTypeCodes: ['CHIEF'], demandPoints: 30, supplyPoints: 60, headroom: 30 },
      { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 60, supplyPoints: 120, headroom: 60 },
      { areaTypeCodes: ['CHIEF', 'ICU', 'WARD'], demandPoints: 150, supplyPoints: 400, headroom: 250 },
    ]
    const hint = evaluateWardSqueezeHint(bySupply)
    expect(hint?.show).toBe(false)
    // 邊際供給 (400-120)=280，邊際需求 (150-60)=90，比值約 3.1x。
    expect(hint?.ratio).toBeCloseTo(280 / 90, 5)
  })

  it('邊際供給只比邊際需求多不到 1.5 倍時顯示提示', () => {
    const bySupply: FeasibilityReport['bySupply'] = [
      { areaTypeCodes: ['CHIEF'], demandPoints: 30, supplyPoints: 60, headroom: 30 },
      { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 60, supplyPoints: 120, headroom: 60 },
      { areaTypeCodes: ['CHIEF', 'ICU', 'WARD'], demandPoints: 150, supplyPoints: 219, headroom: 69 },
    ]
    const hint = evaluateWardSqueezeHint(bySupply)
    // 邊際供給 (219-120)=99，邊際需求 90，比值 1.1x < 1.5。
    expect(hint?.show).toBe(true)
  })

  it('邊際需求為 0 或負值時不適用', () => {
    const bySupply: FeasibilityReport['bySupply'] = [
      { areaTypeCodes: ['CHIEF'], demandPoints: 30, supplyPoints: 60, headroom: 30 },
      { areaTypeCodes: ['CHIEF', 'ICU'], demandPoints: 60, supplyPoints: 120, headroom: 60 },
      { areaTypeCodes: ['CHIEF', 'ICU', 'WARD'], demandPoints: 60, supplyPoints: 120, headroom: 60 },
    ]
    const hint = evaluateWardSqueezeHint(bySupply)
    expect(hint?.show).toBe(false)
    expect(hint?.ratio).toBeNull()
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
