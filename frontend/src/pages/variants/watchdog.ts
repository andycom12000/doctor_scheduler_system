/**
 * SCREEN 04 求解進度的看門狗（issue #46）：覆蓋層可見時每 5 秒 `GET /solver-jobs/{id}` 一次，
 * 把「工作剛好在 GET 與 `subscribe` 之間結束、SSE／host message 沒有補送」的終態撈回來。
 *
 * 抽成小工廠、計時器由外面注入（`{ setInterval, clearInterval }`），測試不必真的等 5 秒，
 * 啟停與終態守衛都能直接驗。**只補終態**：非終態的數字交給 SSE，輪詢與 SSE 沒有先後保證，
 * 用較舊的輪詢回應覆蓋較新的進度會讓畫面數字往回跳。
 */
import type { SolverJob, SolverJobStatus } from '@/api/types'
import { acceptPolledSnapshot, isTerminalStatus } from './variantView'

export interface WatchdogTimers {
  setInterval: (handler: () => void, ms: number) => unknown
  clearInterval: (handle: never) => void
}

/** 預設用瀏覽器計時器；用 `globalThis` 延遲取用，測試換掉全域計時器時也跟著換。 */
export const browserTimers: WatchdogTimers = {
  setInterval: (handler, ms) => globalThis.setInterval(handler, ms),
  clearInterval: (handle) => globalThis.clearInterval(handle as unknown as number),
}

export const WATCHDOG_INTERVAL_MS = 5000

export interface WatchdogOptions {
  /** 抓一次工作快照（`GET /solver-jobs/{id}`）。 */
  fetchSnapshot: () => Promise<SolverJob>
  /** 回應回來時畫面還對得上這份工作（沒換月份、沒換工作、工作還在）才採用。 */
  isCurrent: () => boolean
  /** 畫面目前顯示的工作狀態；`null` = 沒有工作可比對。 */
  currentStatus: () => SolverJobStatus | null
  /** 撈到終態且尚未確立時呼叫；呼叫端負責換快照、拆訂閱、載變體。 */
  onTerminal: (snapshot: SolverJob) => void | Promise<void>
  timers?: WatchdogTimers
  intervalMs?: number
}

export interface Watchdog {
  /** 開始輪詢；已在跑就先停掉舊的再開（不會疊出兩個計時器）。 */
  start: () => void
  stop: () => void
  isRunning: () => boolean
  /** 立刻跑一輪（計時器每次觸發就是呼叫它）；測試直接叫它。 */
  tick: () => Promise<void>
}

export function createWatchdog(options: WatchdogOptions): Watchdog {
  const timers = options.timers ?? browserTimers
  const intervalMs = options.intervalMs ?? WATCHDOG_INTERVAL_MS
  let handle: unknown = null
  let running = false

  async function tick(): Promise<void> {
    try {
      const snapshot = await options.fetchSnapshot()
      if (!options.isCurrent()) return
      if (!isTerminalStatus(snapshot.status)) return // 非終態不採用，交給 SSE
      const current = options.currentStatus()
      // 終態是單向門：畫面已經是終態（SSE 或中止先處理過）就不重複刷新。
      if (current === null || !acceptPolledSnapshot(current)) return
      await options.onTerminal(snapshot)
    } catch {
      // 看門狗容忍暫時性錯誤，等下一次 tick 再試，不覆蓋目前顯示的畫面。
    }
  }

  function stop(): void {
    if (!running) return
    timers.clearInterval(handle as never)
    handle = null
    running = false
  }

  function start(): void {
    stop()
    handle = timers.setInterval(() => void tick(), intervalMs)
    running = true
  }

  return { start, stop, isRunning: () => running, tick }
}
