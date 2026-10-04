import { describe, expect, it } from 'vitest'
import { scheduleStatusLabel } from './scheduleStatusLabel'

describe('scheduleStatusLabel', () => {
  it('草稿、已發布 vN、沒有版本號只寫已發布', () => {
    expect(scheduleStatusLabel('draft', 0, false)).toBe('草稿')
    expect(scheduleStatusLabel('published', 3, false)).toBe('已發布 v3')
    expect(scheduleStatusLabel('published', undefined, false)).toBe('已發布')
  })

  it('發布後有修改要標出來（#75）；草稿不會有這個狀態', () => {
    expect(scheduleStatusLabel('published', 3, true)).toBe('已發布 v3 · 有未發布的修改')
    expect(scheduleStatusLabel('draft', 0, true)).toBe('草稿')
  })
})
