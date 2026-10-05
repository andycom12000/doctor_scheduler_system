import { describe, expect, it, vi } from 'vitest'
import type { SolverJob, SolverJobStatus } from '@/api/types'
import { createWatchdog, WATCHDOG_INTERVAL_MS, type WatchdogOptions, type WatchdogTimers } from './watchdog'

/** 假計時器：記錄註冊／清除，並讓測試手動觸發。 */
function fakeTimers() {
  let nextHandle = 1
  const active = new Map<number, () => void>()
  const timers: WatchdogTimers = {
    setInterval: (handler) => {
      const handle = nextHandle++
      active.set(handle, handler)
      return handle
    },
    clearInterval: (handle) => {
      active.delete(handle as unknown as number)
    },
  }
  return {
    timers,
    activeCount: () => active.size,
    fire: () => [...active.values()].forEach((handler) => handler()),
  }
}

const job = (status: SolverJobStatus): SolverJob => ({ jobId: 'j1', yearMonth: '2026-10', status }) as SolverJob

function setup(overrides: Partial<WatchdogOptions> & { snapshot?: SolverJobStatus; current?: SolverJobStatus | null } = {}) {
  const clock = fakeTimers()
  const onTerminal = vi.fn()
  const fetchSnapshot = vi.fn(async () => job(overrides.snapshot ?? 'succeeded'))
  const watchdog = createWatchdog({
    fetchSnapshot,
    isCurrent: () => true,
    currentStatus: () => (overrides.current === undefined ? 'running' : overrides.current),
    onTerminal,
    timers: clock.timers,
    ...overrides,
  })
  return { clock, onTerminal, fetchSnapshot, watchdog }
}

describe('createWatchdog 啟停', () => {
  it('start 才註冊計時器，stop 清掉；重複 stop 無害', () => {
    const { clock, watchdog } = setup()
    expect(watchdog.isRunning()).toBe(false)
    expect(clock.activeCount()).toBe(0)

    watchdog.start()
    expect(watchdog.isRunning()).toBe(true)
    expect(clock.activeCount()).toBe(1)

    watchdog.stop()
    watchdog.stop()
    expect(watchdog.isRunning()).toBe(false)
    expect(clock.activeCount()).toBe(0)
  })

  it('重複 start 不會疊出兩個計時器', () => {
    const { clock, watchdog } = setup()
    watchdog.start()
    watchdog.start()
    expect(clock.activeCount()).toBe(1)
  })

  it('預設輪詢間隔是 5 秒，且用傳入的間隔註冊', () => {
    expect(WATCHDOG_INTERVAL_MS).toBe(5000)
    const setIntervalSpy = vi.fn(() => 1)
    const watchdog = createWatchdog({
      fetchSnapshot: async () => job('running'),
      isCurrent: () => true,
      currentStatus: () => 'running',
      onTerminal: vi.fn(),
      timers: { setInterval: setIntervalSpy, clearInterval: vi.fn() },
    })
    watchdog.start()
    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 5000)
  })

  it('計時器觸發就是跑一輪 tick', async () => {
    const { clock, fetchSnapshot, watchdog } = setup()
    watchdog.start()
    clock.fire()
    await Promise.resolve()
    expect(fetchSnapshot).toHaveBeenCalledTimes(1)
  })
})

describe('createWatchdog 終態守衛', () => {
  it('撈到終態且畫面還在跑 → 交給 onTerminal', async () => {
    const { onTerminal, watchdog } = setup({ snapshot: 'succeeded', current: 'running' })
    await watchdog.tick()
    expect(onTerminal).toHaveBeenCalledTimes(1)
    expect(onTerminal.mock.calls[0]![0]).toMatchObject({ status: 'succeeded' })
  })

  it.each<SolverJobStatus>(['failed', 'cancelled'])('%s 也算終態', async (status) => {
    const { onTerminal, watchdog } = setup({ snapshot: status, current: 'queued' })
    await watchdog.tick()
    expect(onTerminal).toHaveBeenCalledTimes(1)
  })

  it.each<SolverJobStatus>(['queued', 'running'])('撈到 %s 不採用（非終態交給 SSE，避免數字往回跳）', async (status) => {
    const { onTerminal, watchdog } = setup({ snapshot: status })
    await watchdog.tick()
    expect(onTerminal).not.toHaveBeenCalled()
  })

  it('畫面已經是終態：晚到的輪詢不重複刷新（終態是單向門）', async () => {
    const { onTerminal, watchdog } = setup({ snapshot: 'succeeded', current: 'cancelled' })
    await watchdog.tick()
    expect(onTerminal).not.toHaveBeenCalled()
  })

  it('沒有工作可比對（currentStatus 為 null）不採用', async () => {
    const { onTerminal, watchdog } = setup({ snapshot: 'succeeded', current: null })
    await watchdog.tick()
    expect(onTerminal).not.toHaveBeenCalled()
  })

  it('回應回來時畫面已換月份／換工作（isCurrent 為 false）不採用', async () => {
    const { onTerminal, watchdog } = setup({ snapshot: 'succeeded', isCurrent: () => false })
    await watchdog.tick()
    expect(onTerminal).not.toHaveBeenCalled()
  })

  it('抓取失敗（暫時性錯誤）被吞掉，不丟例外也不呼叫 onTerminal，下一輪照跑', async () => {
    const fetchSnapshot = vi
      .fn<() => Promise<SolverJob>>()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(job('succeeded'))
    const { onTerminal, watchdog } = setup({ fetchSnapshot })
    await expect(watchdog.tick()).resolves.toBeUndefined()
    expect(onTerminal).not.toHaveBeenCalled()

    await watchdog.tick()
    expect(onTerminal).toHaveBeenCalledTimes(1)
  })

  it('onTerminal 自己拆掉看門狗（stop）不會出事', async () => {
    const clock = fakeTimers()
    let dog: ReturnType<typeof createWatchdog>
    dog = createWatchdog({
      fetchSnapshot: async () => job('succeeded'),
      isCurrent: () => true,
      currentStatus: () => 'running',
      onTerminal: () => dog.stop(),
      timers: clock.timers,
    })
    dog.start()
    await dog.tick()
    expect(dog.isRunning()).toBe(false)
    expect(clock.activeCount()).toBe(0)
  })
})
