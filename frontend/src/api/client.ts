/**
 * 所有後端呼叫的唯一入口。
 *
 * 一律使用相對路徑 '/api/...'：
 *   - 開發期由 Vite dev server proxy 到 http://localhost:5080
 *   - 正式版由 WebView2 的 WebResourceRequested 攔截（SPA 從 https://app.local/ 載入，
 *     相對路徑會自然解析到被攔截的位址）
 *   - 未來雲端版同源部署
 * 三種情境下這個檔案不需要任何改動。
 *
 * 唯一的例外是即時進度推送，見 src/realtime.ts。
 */

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, body: unknown, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

interface RequestOptions {
  signal?: AbortSignal
}

export async function apiGet<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return request<T>('GET', path, undefined, options)
}

export async function apiPost<T>(
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<T> {
  return request<T>('POST', path, body, options)
}

export async function apiDelete<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return request<T>('DELETE', path, undefined, options)
}

async function request<T>(
  method: string,
  path: string,
  body: unknown,
  { signal }: RequestOptions,
): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method,
    signal,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const payload = await readBody(response)

  if (!response.ok) {
    throw new ApiError(response.status, payload, `${method} /api${path} → ${response.status}`)
  }

  return payload as T
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204) return null
  const text = await response.text()
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

// ----------------------------------------------------------------------------
// 以下是基礎 PR（#26）新增的函式，寫在既有內容之後，不動一行上面的程式碼：
// 五份設定文件的 PUT／逐月覆寫 PUT／行事曆覆寫 PATCH 需要 apiPut／apiPatch，
// 匯出端點回的是位元組不是 JSON，需要獨立的 apiGetBlob（不能複用 request() 的
// readBody()，那個只認得 JSON 與文字）。
// ----------------------------------------------------------------------------

export async function apiPut<T>(
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<T> {
  return request<T>('PUT', path, body, options)
}

export async function apiPatch<T>(
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<T> {
  return request<T>('PATCH', path, body, options)
}

export interface BlobResponse {
  blob: Blob
  /** 從 `Content-Disposition` 解出的檔名；解不到時為 null。 */
  filename: string | null
}

/** 匯出端點專用：直接回檔案位元組，不經過 readBody() 的 JSON／文字判斷。 */
export async function apiGetBlob(path: string, options: RequestOptions = {}): Promise<BlobResponse> {
  const response = await fetch(`/api${path}`, { method: 'GET', signal: options.signal })

  if (!response.ok) {
    const payload = await readBody(response)
    throw new ApiError(response.status, payload, `GET /api${path} → ${response.status}`)
  }

  const disposition = response.headers.get('Content-Disposition')
  const filename = disposition?.match(/filename\*?=(?:UTF-8''|")?([^";]+)"?/i)?.[1] ?? null
  const blob = await response.blob()
  return { blob, filename }
}
