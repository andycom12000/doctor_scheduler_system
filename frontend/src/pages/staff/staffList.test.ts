import { describe, expect, it } from 'vitest'
import { ApiError } from '@/api/client'
import type { AreaType, PointBoardGroup, Rank, RankGroup, Staff } from '@/api/types'
import {
  areaTypeChips,
  areaTypeNames,
  describeQuotaLoad,
  errorCodeOf,
  filterStaff,
  findPointBoardRow,
  groupCodeOfRank,
  groupNameOf,
  isNotFoundError,
  rankNameOf,
  sortByRankGroup,
  staffCountsLabel,
  visibleStaff,
} from './staffList'

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
  { code: 'PTR', name: '打工R', groupCode: 'JUNIOR', quotaCap: 6, pointType: 'A' },
  { code: 'NP', name: 'NP', groupCode: 'NP', quotaCap: null, pointType: null },
]

const areaTypes: AreaType[] = [
  { code: 'WARD', name: '一般病房' },
  { code: 'ICU', name: '加護病房' },
  { code: 'CHIEF', name: '總值' },
]

function staff(overrides: Partial<Staff> & { id: string }): Staff {
  return {
    employeeNo: 'E000',
    name: '無名',
    rankCode: 'PGY1',
    status: 'active',
    eligibleAreaTypes: ['WARD'],
    ...overrides,
  }
}

const roster: Staff[] = [
  staff({ id: 's-r4', employeeNo: 'E010', name: 'Delta', rankCode: 'R4', eligibleAreaTypes: ['ICU', 'CHIEF'] }),
  staff({ id: 's-ptr', employeeNo: 'E020', name: 'Charlie', rankCode: 'PTR', status: 'inactive' }),
  staff({ id: 's-pgy1', employeeNo: 'E030', name: 'Alpha', rankCode: 'PGY1' }),
  staff({ id: 's-r2', employeeNo: 'E040', name: 'Bravo', rankCode: 'R2', eligibleAreaTypes: ['WARD', 'ICU'] }),
  staff({ id: 's-unknown', employeeNo: 'E999', name: 'Zulu', rankCode: 'NOPE' }),
]

describe('groupCodeOfRank', () => {
  it('依 rankCode 查 groupCode', () => {
    expect(groupCodeOfRank(ranks, 'R2')).toBe('MID')
  })

  it('查不到時回 null', () => {
    expect(groupCodeOfRank(ranks, 'NOPE')).toBeNull()
  })
})

describe('rankNameOf', () => {
  it('回身分顯示名稱', () => {
    expect(rankNameOf(ranks, 'PTR')).toBe('打工R')
  })

  it('查不到時退回代碼本身', () => {
    expect(rankNameOf(ranks, 'NOPE')).toBe('NOPE')
  })
})

describe('groupNameOf', () => {
  it('回身分組顯示名稱', () => {
    expect(groupNameOf(groups, 'SENIOR')).toBe('資深')
  })

  it('null 回 —', () => {
    expect(groupNameOf(groups, null)).toBe('—')
  })

  it('查不到代碼時退回代碼本身', () => {
    expect(groupNameOf(groups, 'GHOST')).toBe('GHOST')
  })
})

describe('areaTypeNames', () => {
  it('轉成顯示名稱並以頓號連接', () => {
    expect(areaTypeNames(['ICU', 'CHIEF'], areaTypes)).toBe('加護病房、總值')
  })

  it('空陣列回 —', () => {
    expect(areaTypeNames([], areaTypes)).toBe('—')
  })

  it('查不到名稱時退回代碼', () => {
    expect(areaTypeNames(['GHOST'], areaTypes)).toBe('GHOST')
  })
})

describe('filterStaff', () => {
  it('狀態 all 時在職與停用都通過', () => {
    const result = filterStaff(roster, ranks, { search: '', group: 'all', status: 'all' })
    expect(result).toHaveLength(roster.length)
  })

  it('依狀態篩選', () => {
    const result = filterStaff(roster, ranks, { search: '', group: 'all', status: 'inactive' })
    expect(result.map((s) => s.id)).toEqual(['s-ptr'])
  })

  it('依身分組篩選', () => {
    const result = filterStaff(roster, ranks, { search: '', group: 'JUNIOR', status: 'all' })
    expect(result.map((s) => s.id).sort()).toEqual(['s-pgy1', 's-ptr'])
  })

  it('搜尋姓名，不分大小寫', () => {
    const result = filterStaff(roster, ranks, { search: 'delta', group: 'all', status: 'all' })
    expect(result.map((s) => s.id)).toEqual(['s-r4'])
  })

  it('搜尋員編，不分大小寫、部分比對', () => {
    const result = filterStaff(roster, ranks, { search: 'e04', group: 'all', status: 'all' })
    expect(result.map((s) => s.id)).toEqual(['s-r2'])
  })

  it('三個條件同時套用', () => {
    const result = filterStaff(roster, ranks, { search: 'charlie', group: 'JUNIOR', status: 'inactive' })
    expect(result.map((s) => s.id)).toEqual(['s-ptr'])
  })
})

describe('sortByRankGroup', () => {
  it('依身分組既定順序、組內依身分既定順序排列', () => {
    const result = sortByRankGroup(roster, ranks, groups)
    // JUNIOR（PGY1, PTR）→ MID（R2）→ SENIOR（R4）→ NP → 查不到組別的排最後
    expect(result.map((s) => s.id)).toEqual(['s-pgy1', 's-ptr', 's-r2', 's-r4', 's-unknown'])
  })

  it('查不到身分順序的人員排在最後', () => {
    const result = sortByRankGroup(roster, ranks, groups)
    expect(result.at(-1)?.id).toBe('s-unknown')
  })

  it('不改動輸入陣列', () => {
    const copy = [...roster]
    sortByRankGroup(roster, ranks, groups)
    expect(roster).toEqual(copy)
  })
})

describe('visibleStaff', () => {
  it('篩選再排序一次做完', () => {
    const result = visibleStaff(roster, ranks, groups, { search: '', group: 'JUNIOR', status: 'all' })
    expect(result.map((s) => s.id)).toEqual(['s-pgy1', 's-ptr'])
  })
})

describe('staffCountsLabel', () => {
  it('組出「共 X 人 · 在職 Y · 停用 Z · 無分頁」', () => {
    expect(staffCountsLabel({ active: 34, inactive: 0 })).toBe('共 34 人 · 在職 34 · 停用 0 · 無分頁')
  })

  it('停用不是 0 時也算進共計', () => {
    expect(staffCountsLabel({ active: 31, inactive: 2 })).toBe('共 33 人 · 在職 31 · 停用 2 · 無分頁')
  })
})

const pointBoardGroups: PointBoardGroup[] = [
  {
    groupCode: 'JUNIOR',
    groupName: '低年級',
    rows: [
      {
        staffId: 's-pgy1',
        name: 'Alpha',
        rankCode: 'PGY1',
        quotaPoints: 3,
        quotaCap: 10,
        quotaRemaining: 7,
        carryOverApplied: 0,
        fairnessPoints: null,
        duties: 3,
        holidayDuties: 1,
      },
    ],
  },
  {
    groupCode: 'NP',
    groupName: 'NP',
    rows: [
      {
        staffId: 's-np',
        name: 'NP1',
        rankCode: 'NP',
        quotaPoints: 5,
        quotaCap: null,
        quotaRemaining: null,
        carryOverApplied: 0,
        fairnessPoints: null,
        duties: 5,
        holidayDuties: 2,
      },
    ],
  },
]

describe('findPointBoardRow', () => {
  it('跨組找到對應人員的那一列', () => {
    expect(findPointBoardRow(pointBoardGroups, 's-np')?.name).toBe('NP1')
  })

  it('查不到回 undefined（停用者不列入點數看板，也會落到這個分支）', () => {
    expect(findPointBoardRow(pointBoardGroups, 's-ghost')).toBeUndefined()
  })
})

describe('describeQuotaLoad', () => {
  it('一般身分：年月 · 額度點數已排/上限 · 餘額 · 假日班數', () => {
    expect(describeQuotaLoad(pointBoardGroups[0].rows[0], '2026-09')).toBe('2026-09 額度點數 3/10 · 餘 7 · 假日 1 班')
  })

  it('quotaCap 為 null（NP）改印值班天數，額度點數標「不計」而非印點數', () => {
    expect(describeQuotaLoad(pointBoardGroups[1].rows[0], '2026-09')).toBe('2026-09 5 天 · 額度點數不計 · 假日 2 班')
  })

  it('查不到列回 null', () => {
    expect(describeQuotaLoad(undefined, '2026-09')).toBeNull()
  })
})

describe('isNotFoundError', () => {
  it('404 的 ApiError 回 true', () => {
    expect(isNotFoundError(new ApiError(404, { error: { code: 'SCHEDULE_NOT_FOUND', message: '無' } }, 'boom'))).toBe(true)
  })

  it('其他狀態碼回 false', () => {
    expect(isNotFoundError(new ApiError(500, {}, 'boom'))).toBe(false)
  })

  it('非 ApiError 回 false', () => {
    expect(isNotFoundError(new Error('boom'))).toBe(false)
  })
})

describe('areaTypeChips', () => {
  it('三個區域類型全部列出，依 eligible 標記亮暗', () => {
    expect(areaTypeChips(areaTypes, ['ICU', 'CHIEF'])).toEqual([
      { code: 'WARD', name: '一般病房', eligible: false },
      { code: 'ICU', name: '加護病房', eligible: true },
      { code: 'CHIEF', name: '總值', eligible: true },
    ])
  })

  it('空的可值清單全部標暗，不是空陣列', () => {
    expect(areaTypeChips(areaTypes, []).every((chip) => !chip.eligible)).toBe(true)
  })
})

describe('errorCodeOf', () => {
  it('讀出 ApiError body 裡的 error.code', () => {
    const err = new ApiError(409, { error: { code: 'EMPLOYEE_NO_TAKEN', message: '重複' } }, 'boom')
    expect(errorCodeOf(err)).toBe('EMPLOYEE_NO_TAKEN')
  })

  it('非 ApiError 回 null', () => {
    expect(errorCodeOf(new Error('boom'))).toBeNull()
  })

  it('body 形狀不對回 null', () => {
    const err = new ApiError(500, { message: 'oops' }, 'boom')
    expect(errorCodeOf(err)).toBeNull()
  })
})
