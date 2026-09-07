/**
 * ============================================================================
 *  全專案唯一有平台分支的檔案。
 * ============================================================================
 *
 * 正式版（WPF + WebView2 殼）：C# 端以 CoreWebView2.PostWebMessageAsJson 推送進度。
 * 開發期 / 未來雲端版：Scheduler.Api 以 SSE 推送。
 *
 * 除了這裡，前端其他地方一律寫標準的 fetch('/api/...')，兩種 transport 逐字相同。
 * 任何新的平台分支都應該被塞進這個檔案，不要散出去。
 *
 * 見 docs/ARCHITECTURE.md §4.4。
 */

/** 求解進度事件。實際欄位待 api-contract.yaml 定案後改為由 schema 生成。 */
export interface Progress {
  jobId: string
  phase: string
  /** 0–100，求解器無法估計時為 null */
  percent: number | null
  [key: string]: unknown
}

/** 取消訂閱。呼叫端離開畫面時務必呼叫，SSE 連線不會自己收掉。 */
export type Unsubscribe = () => void

export function isWebView2Host(): boolean {
  return typeof window !== 'undefined' && Boolean(window.chrome?.webview)
}

export function subscribe(
  jobId: string,
  onEvent: (event: Progress) => void,
): Unsubscribe {
  const host = window.chrome?.webview

  if (host) {
    // --- 正式版：WebView2 host message ---
    // 單一訊息通道，所有 job 共用，故需自行過濾 jobId。
    const listener = (event: { data: unknown }) => {
      const payload = normalize(event.data)
      if (payload && payload.jobId === jobId) onEvent(payload)
    }
    host.addEventListener('message', listener)
    return () => host.removeEventListener('message', listener)
  }

  // --- 開發期 / 雲端版：SSE ---
  const source = new EventSource(`/api/solver-jobs/${jobId}/stream`)
  source.onmessage = (event) => {
    const payload = normalize(JSON.parse(event.data))
    if (payload) onEvent(payload)
  }
  return () => source.close()
}

/**
 * PostWebMessageAsJson 傳來的可能是已解析的物件，也可能是 JSON 字串
 * （取決於 C# 端用的是 PostWebMessageAsJson 還是 PostWebMessageAsString）。
 * 兩者都吞下，把差異擋在這個檔案裡。
 */
function normalize(data: unknown): Progress | null {
  const value = typeof data === 'string' ? safeParse(data) : data
  if (value && typeof value === 'object' && 'jobId' in value) {
    return value as Progress
  }
  return null
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}
