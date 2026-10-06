/**
 * 行事曆自動更新的「畫面閘門」（#112）。後端啟動後在背景更新行事曆，期間所有寫入都被 409 擋下，
 * 所以前端要用全畫面遮罩把操作鎖住，直到同步結束：
 *
 *   checking     剛開、還沒拿到第一次狀態（遮罩透明但擋操作，避免空檔被點）
 *   running      同步中 → 「正在更新行事曆…」
 *   failed       連得上、但中途失敗 → 只能「重試」，成功才解鎖
 *   unreachable  完全連不上網 → 「重試」或「先用現有資料」
 *   stale        使用者選了「先用現有資料」→ 解鎖，畫面頂部持續提醒，下次啟動再試
 *   open         解鎖（沒開自動更新、或同步成功）
 *
 * 有實際更新時用 toast 說一句話（已發布月份受影響會多一句），並讓相關快取失效。
 * `enabled=false`（dev、mock、Debug Shell）時一次查詢就解鎖，完全不鎖。
 */
import { ref, type Ref } from 'vue'
import { getCalendarSyncStatus, startCalendarSync } from '@/api/calendars'
import type { CalendarSyncStatus } from '@/api/types'
import { invalidate } from './useResource'
import { useToast } from './useToast'

export const SYNC_POLL_INTERVAL_MS = 1500
/** 連續幾次查不到狀態就放棄、解鎖（狀態端點壞了不能把使用者永遠鎖在畫面外）。 */
export const SYNC_MAX_STATUS_ERRORS = 3

export type GatePhase = 'checking' | 'running' | 'failed' | 'unreachable' | 'stale' | 'open'

/** 遮罩要不要擋住畫面。 */
export function isLocked(phase: GatePhase): boolean {
  return phase === 'checking' || phase === 'running' || phase === 'failed' || phase === 'unreachable'
}

/** 狀態 → 閘門階段（純函式）。`continuedWithStale`：使用者已選「先用現有資料」。 */
export function phaseOf(status: CalendarSyncStatus, continuedWithStale: boolean): GatePhase {
  if (!status.enabled) return 'open'
  if (status.running) return 'running'
  if (status.failureKind === 'failed') return 'failed'
  if (status.failureKind === 'unreachable') return continuedWithStale ? 'stale' : 'unreachable'
  return 'open'
}

export function syncToastText(years: number[], affectedPublishedMonths: string[] = []): string {
  const base = `已更新 ${years.join('、')} 年行事曆`
  return affectedPublishedMonths.length > 0 ? `${base}。已發布月份的額度點數可能改變` : base
}

export interface GateDeps {
  fetchStatus: () => Promise<CalendarSyncStatus>
  startSync: () => Promise<CalendarSyncStatus>
  sleep: (ms: number) => Promise<void>
  notify: (text: string) => void
  refresh: () => Promise<void>
}

const defaultDeps: GateDeps = {
  fetchStatus: () => getCalendarSyncStatus(),
  startSync: () => startCalendarSync(),
  sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  notify: (text) => void useToast().info(text),
  refresh: async () => {
    // blocked-days：可行性預警用額度點數
    await Promise.all([invalidate('calendars'), invalidate('schedules'), invalidate('blocked-days')])
  },
}

export interface CalendarSyncGate {
  phase: Ref<GatePhase>
  status: Ref<CalendarSyncStatus | null>
  /** 開始查詢並輪詢到同步結束；重複呼叫不會開第二條輪詢。 */
  start: () => Promise<void>
  /** 「重試」：觸發一輪新的同步（後端冪等）並輪詢。 */
  retry: () => Promise<void>
  /** 「先用現有資料」：只在 unreachable 時有效。 */
  continueWithStale: () => void
}

export function createCalendarSyncGate(overrides: Partial<GateDeps> = {}): CalendarSyncGate {
  const deps = { ...defaultDeps, ...overrides }
  const phase = ref<GatePhase>('checking')
  const status = ref<CalendarSyncStatus | null>(null)
  let continuedWithStale = false
  let polling = false
  let notified = false

  async function apply(s: CalendarSyncStatus): Promise<void> {
    status.value = s
    phase.value = phaseOf(s, continuedWithStale)
    if (!s.running && s.updatedYears.length > 0 && !notified) {
      notified = true
      await deps.refresh()
      deps.notify(syncToastText(s.updatedYears, s.affectedPublishedMonths))
    }
  }

  async function poll(): Promise<void> {
    if (polling) return
    polling = true
    try {
      let errors = 0
      for (;;) {
        try {
          const s = await deps.fetchStatus()
          errors = 0
          await apply(s)
          if (!s.running) return
        } catch {
          if (++errors >= SYNC_MAX_STATUS_ERRORS) {
            phase.value = 'open'
            return
          }
        }
        await deps.sleep(SYNC_POLL_INTERVAL_MS)
      }
    } finally {
      polling = false
    }
  }

  async function retry(): Promise<void> {
    const before = phase.value
    continuedWithStale = false
    notified = false
    phase.value = 'running'
    try {
      await apply(await deps.startSync())
    } catch {
      phase.value = before === 'stale' ? 'unreachable' : before // 觸發失敗：留在原本的錯誤畫面
      return
    }
    await poll()
  }

  return {
    phase,
    status,
    start: poll,
    retry,
    continueWithStale() {
      if (phase.value !== 'unreachable') return
      continuedWithStale = true
      phase.value = 'stale'
    },
  }
}

/** App 層共用的那一個閘門。 */
export const calendarSyncGate = createCalendarSyncGate()
