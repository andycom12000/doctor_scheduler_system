import { describe, expect, it } from 'vitest'
import { holidayNote, printHeading, printTabFor } from './printSheet'
import type { DayColumn } from './scheduleGrid'

const col = (over: Partial<DayColumn>): DayColumn => ({
  date: '2026-10-01',
  dd: '01',
  weekday: '四',
  isHoliday: false,
  isPublicHoliday: false,
  holidayName: null,
  quotaPointValue: 1,
  ...over,
})

describe('printTabFor', () => {
  it('兩種格線照目前檢視印；單日詳表沒有列印版面，改印日 × 人（案主指定格式）', () => {
    expect(printTabFor('area-by-day')).toBe('area-by-day')
    expect(printTabFor('day-by-staff')).toBe('day-by-staff')
    expect(printTabFor('day-detail')).toBe('day-by-staff')
  })
})

describe('printHeading', () => {
  it('紙本要看得出是草稿還是哪一版定版', () => {
    expect(printHeading('2026-10', 'draft', 0)).toBe('2026 年 10 月值班表（草稿）')
    expect(printHeading('2026-10', 'published', 3)).toBe('2026 年 10 月值班表（已發布 v3）')
  })

  it('已發布但沒有版本號時只寫已發布，跟狀態 badge 一致', () => {
    expect(printHeading('2026-01', 'published', 0)).toBe('2026 年 1 月值班表（已發布）')
  })
})

describe('holidayNote', () => {
  it('列出當月國定假日的日期與名稱；週末假日不列', () => {
    const days = [
      col({ date: '2026-10-04', dd: '04', isHoliday: true }),
      col({ date: '2026-10-09', dd: '09', isHoliday: true, isPublicHoliday: true, holidayName: '國慶日補假' }),
      col({ date: '2026-10-10', dd: '10', isHoliday: true, isPublicHoliday: true, holidayName: '國慶日' }),
    ]
    expect(holidayNote(days, 'public')).toBe('10/09 國慶日補假、10/10 國慶日')
  })

  it('沒有名稱只印日期；當月沒有國定假日回 null', () => {
    expect(holidayNote([col({ date: '2026-10-25', dd: '25', isHoliday: true, isPublicHoliday: true })], 'public')).toBe(
      '10/25',
    )
    expect(holidayNote([col({ isHoliday: true })], 'public')).toBeNull()
  })

  it('國定假日以外的假日只列有名稱的（逐日覆寫的臨時放假），一般週末不列；國定假日不重複列', () => {
    const days = [
      col({ date: '2026-10-03', dd: '03', isHoliday: true }),
      col({ date: '2026-10-07', dd: '07', isHoliday: true, holidayName: '颱風假' }),
      col({ date: '2026-10-10', dd: '10', isHoliday: true, isPublicHoliday: true, holidayName: '國慶日' }),
      col({ date: '2026-10-08', dd: '08', holidayName: '補班' }),
    ]
    expect(holidayNote(days, 'other')).toBe('10/07 颱風假')
    expect(holidayNote([col({ isHoliday: true })], 'other')).toBeNull()
  })
})
