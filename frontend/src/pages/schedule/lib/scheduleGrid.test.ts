import { describe, expect, it } from 'vitest'
import {
  abbreviate,
  areaCellKey,
  buildStaffDirectory,
  dutiesByArea,
  dutiesByStaff,
  filledCountByDate,
  shortStaffCode,
  staffCellKey,
  staffFooterColumns,
  toDayColumns,
  vacancyCountLabel,
  vacancyCountMap,
  weekdayLabel,
} from './scheduleGrid'
import type { CalendarDay, Duty, PointBoardGroup, Staff } from '@/api/types'

describe('cellKey helpers', () => {
  it('area cellKey 是 area:{areaId}:{date}', () => {
    expect(areaCellKey('area-icu', '2026-09-14')).toBe('area:area-icu:2026-09-14')
  })

  it('staff cellKey 是 staff:{staffId}:{date}', () => {
    expect(staffCellKey('staff-007', '2026-09-14')).toBe('staff:staff-007:2026-09-14')
  })
})

describe('buildStaffDirectory', () => {
  const groups: PointBoardGroup[] = [
    {
      groupCode: 'JUNIOR',
      groupName: '低年級',
      rows: [
        {
          staffId: 'staff-001',
          name: '陳建宏',
          rankCode: 'PGY1',
          quotaPoints: 3,
          quotaCap: 10,
          quotaRemaining: 7,
          duties: 3,
          holidayDuties: 1,
        },
      ],
    },
  ]

  it('由點數看板攤平出 staffId → 名冊，帶組序給 --group-1..4 用', () => {
    const directory = buildStaffDirectory(groups)
    expect(directory.get('staff-001')).toEqual({
      staffId: 'staff-001',
      name: '陳建宏',
      rankCode: 'PGY1',
      groupCode: 'JUNIOR',
      groupName: '低年級',
      groupIndex: 0,
      status: 'active',
    })
  })

  it('查不到的 staffId 回 undefined，呼叫端自行 fallback', () => {
    expect(buildStaffDirectory(groups).get('staff-999')).toBeUndefined()
  })

  it('點數看板查不到的人（例如當月有班但已停用）由 staffList 補上，groupIndex 是 null', () => {
    const staffList: Staff[] = [
      { id: 'staff-001', employeeNo: 'E001', name: '陳建宏', rankCode: 'PGY1', status: 'active', eligibleAreaTypes: ['WARD'] },
      { id: 'staff-050', employeeNo: 'E050', name: '停用小明', rankCode: 'R2', status: 'inactive', eligibleAreaTypes: ['WARD'] },
    ]
    const directory = buildStaffDirectory(groups, staffList)
    // 點數看板已有的人不被 staffList 覆蓋
    expect(directory.get('staff-001')?.groupIndex).toBe(0)
    expect(directory.get('staff-050')).toEqual({
      staffId: 'staff-050',
      name: '停用小明',
      rankCode: 'R2',
      groupCode: '',
      groupName: '',
      groupIndex: null,
      status: 'inactive',
    })
  })
})

describe('shortStaffCode', () => {
  it('取 staffId 最後一段', () => {
    expect(shortStaffCode('staff-001')).toBe('001')
  })

  it('沒有連字號時原樣回傳', () => {
    expect(shortStaffCode('abc')).toBe('abc')
  })
})

describe('abbreviate', () => {
  it('中文姓名取前兩碼', () => {
    expect(abbreviate('陳建宏')).toBe('陳建')
  })

  it('兩碼以內的名字原樣回傳', () => {
    expect(abbreviate('王')).toBe('王')
  })
})

describe('dutiesByArea／dutiesByStaff', () => {
  const duties: Duty[] = [
    { areaId: 'area-a', date: '2026-09-01', staffId: 'staff-001' },
    { areaId: 'area-icu', date: '2026-09-01', staffId: 'staff-002' },
  ]

  it('dutiesByArea 用 areaId|date 查 staffId', () => {
    const map = dutiesByArea(duties)
    expect(map.get('area-a|2026-09-01')).toBe('staff-001')
    expect(map.get('area-icu|2026-09-01')).toBe('staff-002')
  })

  it('dutiesByStaff 用 staffId|date 查 areaId[]', () => {
    const map = dutiesByStaff(duties)
    expect(map.get('staff-001|2026-09-01')).toEqual(['area-a'])
  })

  it('dutiesByStaff 同人同日兩區（X1）兩個都留著，依 areaId 排序', () => {
    const map = dutiesByStaff([
      { areaId: 'area-b', date: '2026-09-01', staffId: 'staff-001' },
      { areaId: 'area-a', date: '2026-09-01', staffId: 'staff-001' },
    ])
    expect(map.get('staff-001|2026-09-01')).toEqual(['area-a', 'area-b'])
  })
})

describe('weekdayLabel', () => {
  it('0 是週日、6 是週六', () => {
    expect(weekdayLabel(0)).toBe('日')
    expect(weekdayLabel(6)).toBe('六')
  })
})

describe('toDayColumns', () => {
  it('攤平行事曆日為表頭需要的欄位', () => {
    const days: CalendarDay[] = [
      {
        date: '2026-09-06',
        weekday: 0,
        isHoliday: true,
        isPublicHoliday: false,
        isMakeUpWorkday: false,
        holidayName: null,
        quotaPointValue: 2,
        overridden: false,
      },
    ]
    expect(toDayColumns(days)).toEqual([
      { date: '2026-09-06', dd: '06', weekday: '日', isHoliday: true, quotaPointValue: 2 },
    ])
  })
})

describe('vacancyCountMap／filledCountByDate', () => {
  it('依日期彙總空缺數，再推出已填補數', () => {
    const byDate = [
      { date: '2026-09-10', areaIds: ['area-c'], count: 1 },
      { date: '2026-09-22', areaIds: ['area-b'], count: 1 },
    ]
    const counts = vacancyCountMap(byDate)
    expect(filledCountByDate(5, counts, '2026-09-10')).toBe(4)
    expect(filledCountByDate(5, counts, '2026-09-01')).toBe(5)
  })
})

describe('vacancyCountLabel', () => {
  it('0 顯示空白，非 0 印數字', () => {
    expect(vacancyCountLabel(0)).toBe('')
    expect(vacancyCountLabel(2)).toBe('2')
  })
})

describe('staffFooterColumns', () => {
  const groups: PointBoardGroup[] = [
    {
      groupCode: 'JUNIOR',
      groupName: '低年級',
      rows: [
        {
          staffId: 'staff-001',
          name: '陳建宏',
          rankCode: 'PGY1',
          quotaPoints: 6,
          quotaCap: 10,
          quotaRemaining: 4,
          duties: 8,
          holidayDuties: 1,
        },
      ],
    },
    {
      groupCode: 'NP',
      groupName: 'NP',
      rows: [
        {
          staffId: 'staff-np',
          name: '游芷若',
          rankCode: 'NP',
          quotaPoints: 0,
          quotaCap: null,
          quotaRemaining: null,
          duties: 3,
          holidayDuties: 0,
        },
      ],
    },
  ]

  it('在職人員印已排／上限，NP（quotaCap 為 null）印 —，帶「NP 不計額度」的 title', () => {
    const columns = staffFooterColumns(groups, [], new Map())
    expect(columns).toEqual([
      { staffId: 'staff-001', duties: 8, quotaLabel: '6/10', quotaTitle: undefined, quotaAtCap: false },
      { staffId: 'staff-np', duties: 3, quotaLabel: '—', quotaTitle: 'NP 不計額度', quotaAtCap: false },
    ])
  })

  it('額度已排 ≥ 上限時 quotaAtCap 為 true（設計稿要求上色提醒）', () => {
    const atCapGroups: PointBoardGroup[] = [
      {
        groupCode: 'JUNIOR',
        groupName: '低年級',
        rows: [
          {
            staffId: 'staff-002',
            name: '林小美',
            rankCode: 'PGY2',
            quotaPoints: 9,
            quotaCap: 9,
            quotaRemaining: 0,
            duties: 9,
            holidayDuties: 0,
          },
        ],
      },
    ]
    const columns = staffFooterColumns(atCapGroups, [], new Map())
    expect(columns[0].quotaAtCap).toBe(true)
  })

  it('extraStaff 同人同日兩區算兩班，不少算', () => {
    const map = new Map([['staff-050|2026-09-01', ['area-a', 'area-b']]])
    const columns = staffFooterColumns(groups, [{ staffId: 'staff-050' }], map)
    expect(columns.find((c) => c.staffId === 'staff-050')?.duties).toBe(2)
  })

  it('當月有班但已停用的人（extraStaff）額度印 —、帶「不在點數看板」的 title，班數由值班表數出來', () => {
    const dutiesByStaffMap = new Map([
      ['staff-050|2026-09-01', ['area-a']],
      ['staff-050|2026-09-05', ['area-b']],
      ['staff-001|2026-09-02', ['area-a']],
    ])
    const columns = staffFooterColumns(groups, [{ staffId: 'staff-050' }], dutiesByStaffMap)
    expect(columns.find((c) => c.staffId === 'staff-050')).toEqual({
      staffId: 'staff-050',
      duties: 2,
      quotaLabel: '—',
      quotaTitle: '不在點數看板',
      quotaAtCap: false,
    })
  })
})
