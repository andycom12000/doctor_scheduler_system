import { describe, expect, it, vi } from 'vitest'
import type { CalendarSyncStatus } from '@/api/types'
import { SYNC_MAX_POLLS, syncToastText, watchCalendarSync } from './useCalendarSyncNotice'

function status(over: Partial<CalendarSyncStatus>): CalendarSyncStatus {
  return {
    enabled: true,
    running: false,
    finishedAt: null,
    updatedYears: [],
    lastSuccessAt: null,
    lastError: null,
    years: [],
    ...over,
  }
}

function deps(responses: CalendarSyncStatus[]) {
  const queue = [...responses]
  return {
    fetchStatus: vi.fn(async () => queue.shift() ?? status({ running: true })),
    sleep: vi.fn(async () => {}),
    notify: vi.fn(),
    refresh: vi.fn(async () => {}),
  }
}

describe('watchCalendarSync', () => {
  it('syncToastText 一年與多年', () => {
    expect(syncToastText([2028])).toBe('已更新 2028 年行事曆')
    expect(syncToastText([2027, 2028])).toBe('已更新 2027、2028 年行事曆')
  })

  it('沒開自動更新：問一次就結束，不通知', async () => {
    const d = deps([status({ enabled: false })])
    expect(await watchCalendarSync(d)).toEqual([])
    expect(d.fetchStatus).toHaveBeenCalledTimes(1)
    expect(d.notify).not.toHaveBeenCalled()
  })

  it('背景還在跑就繼續等，結束後有更新才通知並讓快取失效', async () => {
    const d = deps([status({ running: true }), status({ running: true }), status({ updatedYears: [2028] })])
    expect(await watchCalendarSync(d)).toEqual([2028])
    expect(d.sleep).toHaveBeenCalledTimes(2)
    expect(d.refresh).toHaveBeenCalledTimes(1)
    expect(d.notify).toHaveBeenCalledWith('已更新 2028 年行事曆')
  })

  it('跑完但沒有更新（含失敗）：安靜', async () => {
    const d = deps([status({ lastError: '2028 年更新失敗' })])
    expect(await watchCalendarSync(d)).toEqual([])
    expect(d.notify).not.toHaveBeenCalled()
    expect(d.refresh).not.toHaveBeenCalled()
  })

  it('端點出錯就吞掉', async () => {
    const d = deps([])
    d.fetchStatus.mockRejectedValue(new Error('boom'))
    expect(await watchCalendarSync(d)).toEqual([])
    expect(d.notify).not.toHaveBeenCalled()
  })

  it('一直 running 最多輪詢上限次', async () => {
    const d = deps([])
    expect(await watchCalendarSync(d)).toEqual([])
    expect(d.fetchStatus).toHaveBeenCalledTimes(SYNC_MAX_POLLS)
  })
})
