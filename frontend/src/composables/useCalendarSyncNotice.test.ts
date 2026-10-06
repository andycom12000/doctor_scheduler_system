import { describe, expect, it, vi } from 'vitest'
import type { CalendarSyncStatus } from '@/api/types'
import {
  SYNC_MAX_STATUS_ERRORS,
  createCalendarSyncGate,
  isLocked,
  phaseOf,
  syncToastText,
} from './useCalendarSyncNotice'
import { invalidate } from './useResource'

vi.mock('./useResource', () => ({ invalidate: vi.fn(async () => {}) }))

function status(over: Partial<CalendarSyncStatus>): CalendarSyncStatus {
  return {
    enabled: true,
    running: false,
    finishedAt: null,
    updatedYears: [],
    affectedPublishedMonths: [],
    failureKind: null,
    lastSuccessAt: null,
    lastError: null,
    years: [],
    ...over,
  }
}

/** 依序回傳 `statuses`（用完後一直回最後一個）；`startSync` 另給一串。 */
function gateWith(statuses: CalendarSyncStatus[], restarts: CalendarSyncStatus[] = []) {
  const queue = [...statuses]
  const restartQueue = [...restarts]
  const last = statuses[statuses.length - 1]
  const deps = {
    fetchStatus: vi.fn(async () => queue.shift() ?? last),
    startSync: vi.fn(async () => restartQueue.shift() ?? status({ running: true })),
    sleep: vi.fn(async () => {}),
    notify: vi.fn(),
    refresh: vi.fn(async () => {}),
  }
  return { gate: createCalendarSyncGate(deps), deps, queue }
}

describe('phaseOf／isLocked', () => {
  it('沒開自動更新：解鎖', () => {
    expect(phaseOf(status({ enabled: false, running: true }), false)).toBe('open')
  })

  it('running 鎖住；failed 與 unreachable 也鎖住；成功解鎖', () => {
    expect(phaseOf(status({ running: true }), false)).toBe('running')
    expect(phaseOf(status({ failureKind: 'failed' }), false)).toBe('failed')
    expect(phaseOf(status({ failureKind: 'unreachable' }), false)).toBe('unreachable')
    expect(phaseOf(status({}), false)).toBe('open')
    for (const p of ['checking', 'running', 'failed', 'unreachable'] as const) expect(isLocked(p)).toBe(true)
    for (const p of ['stale', 'open'] as const) expect(isLocked(p)).toBe(false)
  })

  it('failed 不能被「先用現有資料」略過；unreachable 略過後變 stale', () => {
    expect(phaseOf(status({ failureKind: 'failed' }), true)).toBe('failed')
    expect(phaseOf(status({ failureKind: 'unreachable' }), true)).toBe('stale')
  })

  it('toast 文字：一年、多年、已發布月份提醒', () => {
    expect(syncToastText([2028])).toBe('已更新 2028 年行事曆')
    expect(syncToastText([2027, 2028])).toBe('已更新 2027、2028 年行事曆')
    expect(syncToastText([2028], ['2028-01'])).toBe('已更新 2028 年行事曆。已發布月份的額度點數可能改變')
  })
})

describe('createCalendarSyncGate', () => {
  it('一開始是 checking（鎖住）；沒開自動更新問一次就解鎖，完全不鎖', async () => {
    const { gate, deps } = gateWith([status({ enabled: false })])
    expect(gate.phase.value).toBe('checking')
    await gate.start()
    expect(gate.phase.value).toBe('open')
    expect(deps.fetchStatus).toHaveBeenCalledTimes(1)
    expect(deps.notify).not.toHaveBeenCalled()
  })

  it('running → 成功：全程鎖到結束，有更新才 toast 並讓三組快取失效', async () => {
    const { gate, deps } = gateWith([
      status({ running: true }),
      status({ running: true }),
      status({ updatedYears: [2028], affectedPublishedMonths: ['2028-01'] }),
    ])
    const seen: string[] = []
    deps.sleep.mockImplementation(async () => void seen.push(gate.phase.value))
    await gate.start()
    expect(seen).toEqual(['running', 'running'])
    expect(gate.phase.value).toBe('open')
    expect(deps.refresh).toHaveBeenCalledTimes(1)
    expect(deps.notify).toHaveBeenCalledWith('已更新 2028 年行事曆。已發布月份的額度點數可能改變')
  })

  it('預設的 refresh 讓 calendars／schedules／blocked-days 失效', async () => {
    const queue = [status({ updatedYears: [2028] })]
    const gate = createCalendarSyncGate({
      fetchStatus: async () => queue[0],
      notify: () => {},
      sleep: async () => {},
    })
    await gate.start()
    for (const key of ['calendars', 'schedules', 'blocked-days']) expect(invalidate).toHaveBeenCalledWith(key)
  })

  it('成功但沒有更新：解鎖、不 toast', async () => {
    const { gate, deps } = gateWith([status({})])
    await gate.start()
    expect(gate.phase.value).toBe('open')
    expect(deps.notify).not.toHaveBeenCalled()
    expect(deps.refresh).not.toHaveBeenCalled()
  })

  it('failed：停在失敗畫面，只能重試；重試成功才解鎖', async () => {
    const { gate, deps } = gateWith(
      [status({ failureKind: 'failed' }), status({ running: true }), status({ updatedYears: [2028] })],
      [status({ running: true })],
    )
    await gate.start()
    expect(gate.phase.value).toBe('failed')

    gate.continueWithStale() // failed 不能略過
    expect(gate.phase.value).toBe('failed')

    await gate.retry()
    expect(deps.startSync).toHaveBeenCalledTimes(1)
    expect(gate.phase.value).toBe('open')
    expect(deps.notify).toHaveBeenCalledTimes(1)
  })

  it('unreachable：可以重試；也可以先用現有資料（stale，解鎖但持續提醒）', async () => {
    const { gate } = gateWith([status({ failureKind: 'unreachable' })])
    await gate.start()
    expect(gate.phase.value).toBe('unreachable')
    expect(isLocked(gate.phase.value)).toBe(true)

    gate.continueWithStale()
    expect(gate.phase.value).toBe('stale')
    expect(isLocked(gate.phase.value)).toBe(false)
  })

  it('stale 後再按重試：回到鎖住並重跑；仍連不上就回 unreachable', async () => {
    const { gate } = gateWith(
      [status({ failureKind: 'unreachable' }), status({ failureKind: 'unreachable' })],
      [status({ running: true })],
    )
    await gate.start()
    gate.continueWithStale()
    await gate.retry()
    expect(gate.phase.value).toBe('unreachable')
  })

  it('重試端點出錯：留在原本的錯誤畫面', async () => {
    const { gate, deps } = gateWith([status({ failureKind: 'failed' })])
    await gate.start()
    deps.startSync.mockRejectedValue(new Error('boom'))
    await gate.retry()
    expect(gate.phase.value).toBe('failed')
  })

  it('狀態端點連續出錯就解鎖，不把使用者永遠鎖住', async () => {
    const { gate, deps } = gateWith([status({})])
    deps.fetchStatus.mockRejectedValue(new Error('boom'))
    await gate.start()
    expect(deps.fetchStatus).toHaveBeenCalledTimes(SYNC_MAX_STATUS_ERRORS)
    expect(gate.phase.value).toBe('open')
  })

  it('重複呼叫 start 不會開第二條輪詢', async () => {
    const { gate, deps } = gateWith([status({ running: true }), status({})])
    await Promise.all([gate.start(), gate.start()])
    expect(deps.fetchStatus).toHaveBeenCalledTimes(2)
  })
})
