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

import type { components } from './api/schema'

/**
 * 求解進度事件，即契約的 SolverProgress（由 api-contract.yaml 生成）。
 * 沒有百分比：求解器只能給收斂資訊（ARCHITECTURE §4.7）。
 */
export type Progress = components['schemas']['SolverProgress']

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
 * 存檔結果：`saved` 是殼寫進使用者選的路徑；`cancelled` 是使用者在對話框按取消（沒寫任何東西）；
 * `downloaded` 是瀏覽器（開發期）交給瀏覽器下載，沒有存檔對話框的回報可看。
 */
export type SaveResult = 'saved' | 'cancelled' | 'downloaded'

/** 殼回報的結果型別，要與 src/Scheduler.Shell/SaveFileProtocol.cs 一致。 */
const SAVE_REQUEST_TYPE = 'save-file'
const SAVE_RESULT_TYPE = 'save-file-result'

let saveSeq = 0

/**
 * 把位元組存成使用者選的檔案。
 *
 * 正式版（WebView2 殼）：不經 Chromium 的下載機制——它會在使用者選位置之前就把內容寫進「下載」
 * 資料夾的 GUID.tmp，取消時不刪（#70）。改成 postMessage 把位元組（base64）交給殼，
 * 殼跳系統存檔對話框，只在按儲存後寫入選定路徑；這裡等殼回報 saved／cancelled／失敗（失敗會 reject）。
 * 開發期：隱藏的 `<a download>` + object URL，由瀏覽器處理。
 */
export async function saveFile(blob: Blob, fileName: string): Promise<SaveResult> {
  const host = window.chrome?.webview
  if (!host) {
    downloadBlob(blob, fileName)
    return 'downloaded'
  }

  const id = `save-${Date.now()}-${++saveSeq}`
  const base64 = bytesToBase64(new Uint8Array(await blob.arrayBuffer()))
  return new Promise<SaveResult>((resolve, reject) => {
    const listener = (event: { data: unknown }) => {
      const reply = parseSaveReply(event.data)
      if (!reply || reply.id !== id) return
      host.removeEventListener('message', listener)
      if (reply.status === 'saved' || reply.status === 'cancelled') resolve(reply.status)
      else reject(new Error(reply.message ?? '存檔失敗'))
    }
    host.addEventListener('message', listener)
    try {
      host.postMessage({ type: SAVE_REQUEST_TYPE, id, fileName, base64 })
    } catch (err) {
      host.removeEventListener('message', listener)
      reject(err)
    }
  })
}

interface SaveReply {
  id: string
  status: string
  message?: string
}

/** 殼的存檔回覆；不是這種訊息（例如求解進度）回 null。 */
export function parseSaveReply(data: unknown): SaveReply | null {
  const value = typeof data === 'string' ? safeParse(data) : data
  if (!value || typeof value !== 'object') return null
  const v = value as Record<string, unknown>
  if (v.type !== SAVE_RESULT_TYPE || typeof v.id !== 'string' || typeof v.status !== 'string') return null
  return { id: v.id, status: v.status, message: typeof v.message === 'string' ? v.message : undefined }
}

/** 分段轉換：一次 `String.fromCharCode(...大陣列)` 會爆呼叫堆疊。 */
export function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk))
  }
  return btoa(binary)
}

/** 上一次下載的 object URL；下一次下載時才收回（見 `downloadBlob`）。 */
let pendingUrl: string | null = null

/**
 * 開發期（瀏覽器）的下載：隱藏的 `<a download>` + object URL。
 * URL 不在計時器上收回：瀏覽器可能先跳存檔對話框，使用者選位置的這段時間下載還在讀這個 URL，
 * 提早收回會讓下載默默失敗（#33）。改成下一次下載時才收回上一個，同時只留一個，匯出檔只有幾 KB。
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  if (pendingUrl) URL.revokeObjectURL(pendingUrl)
  const url = URL.createObjectURL(blob)
  pendingUrl = url
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
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
