/**
 * 行事曆自動更新的「畫面閘門」（#112）。後端啟動後在背景更新行事曆，期間所有寫入都被 409 擋下，
 * 所以前端要用全畫面遮罩把操作鎖住，直到同步結束：
 *
 *   checking     剛開、還沒拿到第一次狀態（遮罩透明但擋操作；超過幾秒才顯示文字）
 *   running      同步中 → 「正在更新行事曆…」
 *   writeFailed  已拿到官方資料、但寫入資料庫失敗 → 只能「重試」，成功才解鎖
 *   unavailable  取不到可用的資料（斷網、HTTP 錯誤、封鎖頁、格式不符…）→ 「重試」或「先用現有資料」
 *   stale        使用者選了「先用現有資料」→ 解鎖，畫面頂部持續提醒，下次啟動再試
 *   open         解鎖（沒開自動更新、或同步成功）
 *
 * 有實際更新而且沒有失敗時，用 toast 說一句話（已發布月份受影響會多一句），並讓相關快取失效。
 * 部分成功、部分失敗時不 toast（遮罩會照實說哪些年份已更新、哪些失敗），但快取照樣失效。
 * `enabled=false`（dev、mock、Debug Shell）時一次查詢就解鎖，完全不鎖。
 */
import { ref, type Ref } from 'vue'
import { getCalendarSyncStatus, startCalendarSync } from '@/api/calendars'
import type { CalendarSyncStatus } from '@/api/types'
import { invalidate } from './useResource'
import { useToast } from './useToast'

export const SYNC_POLL_INTERVAL_MS = 1500
/** 狀態查詢的逾時：端點卡住不能讓使用者看著透明遮罩乾等。 */
export const SYNC_STATUS_TIMEOUT_MS = 5000
/** 連續幾次查不到狀態就放棄、解鎖（狀態端點壞了不能把使用者永遠鎖在畫面外）。 */
export const SYNC_MAX_STATUS_ERRORS = 2

export type GatePhase = 'checking' | 'running' | 'writeFailed' | 'unavailable' | 'stale' | 'open'

/** 遮罩要不要擋住畫面。 */
export function isLocked(phase: GatePhase): boolean {
  return phase === 'checking' || phase === 'running' || phase === 'writeFailed' || phase === 'unavailable'
}

/** 狀態 → 閘門階段（純函式）。`continuedWithStale`：使用者已選「先用現有資料」。 */
export function phaseOf(status: CalendarSyncStatus, continuedWithStale: boolean): GatePhase {
  if (!status.enabled) return 'open'
  if (status.running) return 'running'
  if (status.failureKind === 'writeFailed') return 'writeFailed'
  if (status.failureKind === 'unavailable') return continuedWithStale ? 'stale' : 'unavailable'
  return 'open'
}

export function syncToastText(years: number[], affectedPublishedMonths: string[] = []): string {
  const base = `已更新 ${years.join('、')} 年行事曆`
  return affectedPublishedMonths.length > 0 ? `${base}。已發布月份的額度點數或公平性點數可能改變` : base
}

/** 遮罩上「這一輪的結果」那一句：照實說哪些年份已更新、哪些沒有。 */
export function failureDetail(status: CalendarSyncStatus): string {
  const updated = status.updatedYears.length > 0 ? `已更新 ${status.updatedYears.join('、')} 年。` : ''
  const summary = status.lastError ? `${status.lastError}。` : ''
  if (status.failureKind === 'unavailable' && status.updatedYears.length === 0) return `${summary}行事曆沒有被改動。`
  return `${updated}${summary}`
}

export interface GateDeps {
  fetchStatus: () => Promise<CalendarSyncStatus>
  startSync: () => Promise<CalendarSyncStatus>
  sleep: (ms: number) => Promise<void>
  notify: (text: string) => void
  refresh: () => Promise<void>
}

const defaultDeps: GateDeps = {
  fetchStatus: () => getCalendarSyncStatus(AbortSignal.timeout(SYNC_STATUS_TIMEOUT_MS)),
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
  /** 「先用現有資料」：只在 unavailable 時有效。 */
  continueWithStale: () => void
}

export function createCalendarSyncGate(overrides: Partial<GateDeps> = {}): CalendarSyncGate {
  const deps = { ...defaultDeps, ...overrides }
  const phase = ref<GatePhase>('checking')
  const status = ref<CalendarSyncStatus | null>(null)
  let continuedWithStale = false
  let polling = false
  let refreshed = false

  async function apply(s: CalendarSyncStatus): Promise<void> {
    status.value = s
    phase.value = phaseOf(s, continuedWithStale)
    if (!s.running && s.updatedYears.length > 0 && !refreshed) {
      refreshed = true
      await deps.refresh()
      // 有失敗時不 toast：遮罩已說明哪些年份更新、哪些失敗，不要兩個訊息打架
      if (!s.failureKind) deps.notify(syncToastText(s.updatedYears, s.affectedPublishedMonths))
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
    refreshed = false
    phase.value = 'running'
    try {
      await apply(await deps.startSync())
    } catch {
      phase.value = before === 'stale' ? 'unavailable' : before // 觸發失敗：留在原本的錯誤畫面
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
      if (phase.value !== 'unavailable') return
      continuedWithStale = true
      phase.value = 'stale'
    },
  }
}

/** App 層共用的那一個閘門。 */
export const calendarSyncGate = createCalendarSyncGate()
