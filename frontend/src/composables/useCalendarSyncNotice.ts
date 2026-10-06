/**
 * 行事曆自動更新的結果通知（#112）。後端在啟動後於背景更新行事曆，前端啟動時輪詢
 * `GET /calendars/sync-status`，等背景工作結束，有實際更新的年份才用 toast 說一句話；
 * 沒更新、沒開自動更新、斷網失敗一律安靜（失敗原因在後端的 data/calendar-sync.log）。
 *
 * 更新後 `calendars`、`schedules` 的快取都失效：行事曆影響額度點數與公平性點數，
 * 已載入的值班表與點數看板要重抓。
 */
import { getCalendarSyncStatus } from '@/api/calendars'
import { invalidate } from './useResource'
import { useToast } from './useToast'

export const SYNC_POLL_INTERVAL_MS = 3000
/** 輪詢上限：3 秒 × 40 = 2 分鐘；超過就不再等（背景工作自己還是會跑完）。 */
export const SYNC_MAX_POLLS = 40

export function syncToastText(years: number[]): string {
  return `已更新 ${years.join('、')} 年行事曆`
}

export interface WatchDeps {
  fetchStatus: typeof getCalendarSyncStatus
  sleep: (ms: number) => Promise<void>
  notify: (text: string) => void
  refresh: () => Promise<void>
}

const defaultDeps: WatchDeps = {
  fetchStatus: getCalendarSyncStatus,
  sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  notify: (text) => void useToast().info(text),
  refresh: async () => {
    await Promise.all([invalidate('calendars'), invalidate('schedules')])
  },
}

/** 回傳通知過的年份（沒通知為空陣列）；任何錯誤都吞掉，不影響使用。 */
export async function watchCalendarSync(deps: Partial<WatchDeps> = {}): Promise<number[]> {
  const d = { ...defaultDeps, ...deps }
  try {
    for (let i = 0; i < SYNC_MAX_POLLS; i++) {
      const status = await d.fetchStatus()
      if (!status.enabled) return []
      if (!status.running) {
        if (status.updatedYears.length === 0) return []
        await d.refresh()
        d.notify(syncToastText(status.updatedYears))
        return status.updatedYears
      }
      await d.sleep(SYNC_POLL_INTERVAL_MS)
    }
  } catch {
    // 狀態端點只是附加資訊
  }
  return []
}
