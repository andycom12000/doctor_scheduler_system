import { describe, expect, it } from 'vitest'
import {
  abbreviate,
  areaCellKey,
  buildStaffDirectory,
  dutiesByArea,
  dutiesByStaff,
  filledCountByDate,
  staffCellKey,
  toDayColumns,
  vacancyCountMap,
  weekdayLabel,
} from './scheduleGrid'
import type { CalendarDay, Duty, PointBoardGroup } from '@/api/types'

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

  it('由點數看板攤平出 staffId → 名冊', () => {
    const directory = buildStaffDirectory(groups)
    expect(directory.get('staff-001')).toEqual({
      staffId: 'staff-001',
      name: '陳建宏',
      rankCode: 'PGY1',
      groupCode: 'JUNIOR',
      groupName: '低年級',
    })
  })

  it('查不到的 staffId 回 undefined，呼叫端自行 fallback', () => {
    expect(buildStaffDirectory(groups).get('staff-999')).toBeUndefined()
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

  it('dutiesByStaff 用 staffId|date 查 areaId', () => {
    const map = dutiesByStaff(duties)
    expect(map.get('staff-001|2026-09-01')).toBe('area-a')
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
