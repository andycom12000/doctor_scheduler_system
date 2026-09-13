import { describe, expect, it } from 'vitest'
import { daysInMonth } from './dates'

describe('daysInMonth', () => {
  it('9 月有 30 天', () => {
    const days = daysInMonth('2026-09')
    expect(days).toHaveLength(30)
    expect(days[0]).toBe('2026-09-01')
    expect(days[29]).toBe('2026-09-30')
  })

  it('2 月依閏年天數', () => {
    expect(daysInMonth('2024-02')).toHaveLength(29)
    expect(daysInMonth('2026-02')).toHaveLength(28)
  })

  it('12 月有 31 天，日期補零', () => {
    const days = daysInMonth('2026-12')
    expect(days).toHaveLength(31)
    expect(days[8]).toBe('2026-12-09')
  })
})
