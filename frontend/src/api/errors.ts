/**
 * `ApiError` → 使用者訊息對照表。契約的 `ErrorCode` 列舉 13 個全部列出
 * （`api-contract.yaml` components.schemas.ErrorCode）。
 *
 * `HARD_VIOLATIONS_PRESENT` 與 `SCHEDULE_ALREADY_PUBLISHED` 各自還有專屬的畫面流程
 * （確認發布、套用按鈕直接停用），這裡只提供文案，流程在各畫面自己接。
 */
import { ApiError } from './client'
import type { ErrorCode } from './types'

const MESSAGES: Record<ErrorCode, string> = {
  NOT_FOUND: '找不到指定的資料，可能已被刪除或尚未建立。',
  INVALID_REQUEST: '請求內容有誤，請檢查填寫的欄位。',
  BLOCKED_DAY_CAP_EXCEEDED: '已達該人員本月不可排班日的登記上限。',
  HARD_VIOLATIONS_PRESENT: '值班表仍有硬約束違規，需明確確認才能發布。',
  SCHEDULE_ALREADY_PUBLISHED: '值班表已發布，不可整份套用變體，請逐格修改。',
  DOUBLE_BOOKING_PRESENT: '還有人同一天排在兩區，排除後才能發布、匯出或列印。',
  AREA_IN_USE: '這個區域仍被值班表引用，無法刪除。',
  AREA_TYPE_IN_USE: '這個區域類型仍被區域引用，無法刪除。',
  RANK_IN_USE: '這個身分或身分組仍被人員、資格矩陣或約束引用，無法刪除。',
  EMPLOYEE_NO_TAKEN: '這個員編已被使用，請改用其他員編。',
  STAFF_HAS_DUTIES: '這位人員已有值班紀錄，無法刪除，請改為停用。',
  SOLVER_BUSY: '已有求解工作正在執行中，請等待完成或先中止。',
  SOLVER_FAILED: '求解工作失敗，請重新求解。',
}

/** 取得可直接顯示給使用者的中文訊息。非 `ApiError`（網路中斷、程式錯誤等）回一般性訊息。 */
export function describeError(err: unknown): string {
  if (err instanceof ApiError) {
    const code = extractErrorCode(err.body)
    if (code && code in MESSAGES) return MESSAGES[code]
    return err.message
  }
  if (err instanceof Error) return err.message
  return '發生未預期的錯誤，請稍後再試。'
}

function extractErrorCode(body: unknown): ErrorCode | null {
  if (
    body &&
    typeof body === 'object' &&
    'error' in body &&
    body.error &&
    typeof body.error === 'object' &&
    'code' in body.error &&
    typeof body.error.code === 'string'
  ) {
    return body.error.code as ErrorCode
  }
  return null
}
