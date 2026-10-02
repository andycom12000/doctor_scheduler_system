/**
 * SCREEN 01 寫入流程（issue #34）的純函式：發布／匯出／已發布確認／拖拉鍵。
 * 流程本身（呼叫 API、跳確認）在 index.vue，這裡只放能脫離 DOM 與網路單獨測的判斷。
 */
import { ApiError } from '@/api/client'
import type { ExportLayout, ScheduleStatus } from '@/api/types'

/** `ApiError` 且本體是契約的 `ErrorResponse`、`error.code` 吻合時為 true。 */
function hasErrorCode(err: unknown, status: number, code: string): boolean {
  if (!(err instanceof ApiError) || err.status !== status) return false
  const body = err.body
  if (!body || typeof body !== 'object' || !('error' in body)) return false
  const error = (body as { error?: unknown }).error
  return !!error && typeof error === 'object' && (error as { code?: unknown }).code === code
}

/** 發布被擋：還有硬約束違規，需使用者明確確認才能帶 `acknowledgeViolations: true` 重發。 */
export function isHardViolationsPresent(err: unknown): boolean {
  return hasErrorCode(err, 409, 'HARD_VIOLATIONS_PRESENT')
}

/** 已發布值班表的「本次進入畫面後第一次修改」要確認一次；確認過、或草稿，就不再問。 */
export function needsPublishedEditConfirm(status: ScheduleStatus | undefined, confirmedThisVisit: boolean): boolean {
  return status === 'published' && !confirmedThisVisit
}

/** 匯出版面跟著目前檢視；單日詳表沒有對應版面，用區域 × 日。 */
export function exportLayoutFor(tab: 'area-by-day' | 'day-by-staff' | 'day-detail'): ExportLayout {
  return tab === 'day-by-staff' ? 'day-by-staff' : 'area-by-day'
}

/**
 * 下載檔名：優先用回應 `Content-Disposition` 解出的檔名（`filename*=UTF-8''...` 是百分比編碼，
 * 解得開就解），沒有或解壞才退回 `duty-{ym}.xlsx`。
 */
export function exportFileName(responseFilename: string | null, ym: string): string {
  const fallback = `duty-${ym}.xlsx`
  if (!responseFilename) return fallback
  try {
    const decoded = decodeURIComponent(responseFilename).trim()
    return decoded === '' ? fallback : decoded
  } catch {
    return responseFilename.trim() || fallback
  }
}

/** 拖拉用的格子鍵：區域 × 日的一格就是 CellRef（areaId + date）。 */
export function swapCellKey(areaId: string, date: string): string {
  return `${areaId}|${date}`
}

export function parseSwapCellKey(key: string): { areaId: string; date: string } | null {
  const separator = key.lastIndexOf('|')
  if (separator <= 0 || separator === key.length - 1) return null
  return { areaId: key.slice(0, separator), date: key.slice(separator + 1) }
}
