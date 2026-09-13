import { describe, expect, it } from 'vitest'
import { daysOf, indexCalendar } from './useCalendar'
import type { Calendar, CalendarDay } from '@/api/types'

function day(overrides: Partial<CalendarDay> & { date: string }): CalendarDay {
  return {
    weekday: 0,
    isHoliday: false,
    isPublicHoliday: false,
    isMakeUpWorkday: false,
    holidayName: null,
    quotaPointValue: 1,
    overridden: false,
    ...overrides,
  }
}

const calendar: Calendar = {
  year: 2026,
  days: [
    day({ date: '2026-08-31', weekday: 1, quotaPointValue: 1 }),
    day({ date: '2026-09-01', weekday: 2, quotaPointValue: 1 }),
    day({ date: '2026-09-06', weekday: 0, isHoliday: true, quotaPointValue: 2 }),
    day({ date: '2026-09-10', weekday: 4, isHoliday: false, quotaPointValue: 1 }),
    day({ date: '2026-10-01', weekday: 4, quotaPointValue: 1 }),
  ],
}

describe('indexCalendar', () => {
  it('依 date 攤成 Map', () => {
    const index = indexCalendar(calendar)
    expect(index.size).toBe(5)
    expect(index.get('2026-09-06')?.isHoliday).toBe(true)
  })

  it('null／undefined 回空 Map', () => {
    expect(indexCalendar(null).size).toBe(0)
    expect(indexCalendar(undefined).size).toBe(0)
  })
})

describe('daysOf', () => {
  const index = indexCalendar(calendar)

  it('只回該年月的日子，依日期排序', () => {
    const days = daysOf(index, '2026-09')
    expect(days.map((d) => d.date)).toEqual(['2026-09-01', '2026-09-06', '2026-09-10'])
  })

  it('跨月不會漏進來（8 月尾巴、10 月第一天不算進 9 月）', () => {
    const days = daysOf(index, '2026-09')
    expect(days.some((d) => d.date === '2026-08-31')).toBe(false)
    expect(days.some((d) => d.date === '2026-10-01')).toBe(false)
  })

  it('沒有資料的月份回空陣列', () => {
    expect(daysOf(index, '2026-12')).toEqual([])
  })
})
