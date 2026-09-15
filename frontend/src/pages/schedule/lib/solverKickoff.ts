/**
 * SCREEN 01 V02「開始求解」（issue #62）：`POST /solver-jobs` 撞到 409 `SOLVER_BUSY` 時，
 * 從錯誤本體解析目前正在跑的 jobId，直接導去變體頁 attach 那一份。跟
 * `pages/blockedDays/logic.ts` 的 `extractBusyJobId` 是同一段邏輯——兩個畫面各自獨立，
 * 不為了共用五行程式碼建立跨頁面模組相依。
 */
export function extractBusyJobId(body: unknown): string | null {
  if (!body || typeof body !== 'object' || !('error' in body)) return null
  const error = (body as { error?: unknown }).error
  if (!error || typeof error !== 'object' || !('details' in error)) return null
  const details = (error as { details?: unknown }).details
  if (!details || typeof details !== 'object') return null
  const jobId = (details as { jobId?: unknown }).jobId
  return typeof jobId === 'string' ? jobId : null
}
