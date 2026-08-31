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
