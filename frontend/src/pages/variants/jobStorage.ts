/**
 * 契約沒有「列出求解工作」端點，重新整理或重開程式會丟 `jobId`：記每個月最近一次
 * 的 `jobId` 到 `localStorage`（frontend-plan.md §3.5、§6）。使用者資料夾在
 * `data/wv2data`，不違反 portable 的限制。
 *
 * 讀寫都包 try/catch——WebView2 的使用者資料目錄若還沒就緒或被鎖住，
 * `localStorage` 可能丟例外，不該因此擋住畫面，退化成「這次沒記住」而已。
 */
export interface KeyValueStore {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

export function lastJobStorageKey(ym: string): string {
  return `solver:lastJob:${ym}`
}

function defaultStorage(): KeyValueStore | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

export function readLastJobId(ym: string, storage: KeyValueStore | null = defaultStorage()): string | null {
  try {
    return storage?.getItem(lastJobStorageKey(ym)) ?? null
  } catch {
    return null
  }
}

export function rememberJobId(ym: string, jobId: string, storage: KeyValueStore | null = defaultStorage()): void {
  try {
    storage?.setItem(lastJobStorageKey(ym), jobId)
  } catch {
    // 見檔案頂端說明：寫入失敗不影響畫面。
  }
}

/** 路由 query 的 `job` 優先；沒有的話退回 `localStorage` 記的那一個。 */
export function resolveJobId(
  queryJob: string | string[] | undefined,
  ym: string,
  storage: KeyValueStore | null = defaultStorage(),
): string | null {
  const fromQuery = Array.isArray(queryJob) ? queryJob[0] : queryJob
  if (fromQuery) return fromQuery
  return readLastJobId(ym, storage)
}
