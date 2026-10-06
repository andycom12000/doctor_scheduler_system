import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { bytesToBase64, downloadBlob, parseSaveReply, saveFile } from './realtime'

type Listener = (event: { data: unknown }) => void

/** 假的 WebView2 host：記下 postMessage，測試自己決定何時回覆。 */
function stubHost() {
  const listeners = new Set<Listener>()
  const posted: Array<Record<string, unknown>> = []
  const host = {
    addEventListener: (_: 'message', l: Listener) => void listeners.add(l),
    removeEventListener: (_: 'message', l: Listener) => void listeners.delete(l),
    postMessage: (m: unknown) => void posted.push(m as Record<string, unknown>),
  }
  vi.stubGlobal('window', { chrome: { webview: host } })
  return { listeners, posted, reply: (data: unknown) => [...listeners].forEach((l) => l({ data })) }
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('bytesToBase64', () => {
  it('與 Buffer 的 base64 相同，含大於分段大小的輸入', () => {
    const small = new Uint8Array([0x50, 0x4b, 3, 4, 255, 0])
    expect(bytesToBase64(small)).toBe(Buffer.from(small).toString('base64'))
    const big = Uint8Array.from({ length: 0x8000 * 3 + 5 }, (_, i) => i % 256)
    expect(bytesToBase64(big)).toBe(Buffer.from(big).toString('base64'))
  })
})

describe('parseSaveReply', () => {
  it('認得殼的回覆（物件或 JSON 字串），其他訊息（求解進度）回 null', () => {
    const reply = { type: 'save-file-result', id: 'a', status: 'saved' }
    expect(parseSaveReply(reply)).toEqual({ id: 'a', status: 'saved', message: undefined })
    expect(parseSaveReply(JSON.stringify({ ...reply, status: 'error', message: '磁碟已滿' }))).toEqual({
      id: 'a',
      status: 'error',
      message: '磁碟已滿',
    })
    expect(parseSaveReply({ jobId: 'j', status: 'running' })).toBeNull()
    expect(parseSaveReply('not json')).toBeNull()
    expect(parseSaveReply(null)).toBeNull()
  })
})

describe('saveFile（WebView2 殼）', () => {
  beforeEach(() => {
    // 殼內絕不能走 <a download>：任何 document 存取都算錯
    vi.stubGlobal('document', {
      createElement: () => {
        throw new Error('殼內不該觸發瀏覽器下載')
      },
    })
  })

  it('把檔名與 base64 位元組 postMessage 給殼，收到 saved 就 resolve 並退訂', async () => {
    const host = stubHost()
    const bytes = new Uint8Array([0x50, 0x4b, 3, 4])
    const promise = saveFile(new Blob([bytes]), 'duty-2026-11.xlsx')
    await vi.waitFor(() => expect(host.posted).toHaveLength(1))

    const message = host.posted[0]!
    expect(message.type).toBe('save-file')
    expect(message.fileName).toBe('duty-2026-11.xlsx')
    expect(message.base64).toBe(Buffer.from(bytes).toString('base64'))

    host.reply({ type: 'save-file-result', id: message.id, status: 'saved' })
    await expect(promise).resolves.toBe('saved')
    expect(host.listeners.size).toBe(0)
  })

  it('使用者取消 → cancelled', async () => {
    const host = stubHost()
    const promise = saveFile(new Blob(['x']), 'a.xlsx')
    await vi.waitFor(() => expect(host.posted).toHaveLength(1))
    host.reply({ type: 'save-file-result', id: host.posted[0]!.id, status: 'cancelled' })
    await expect(promise).resolves.toBe('cancelled')
  })

  it('殼回報失敗 → reject 並帶訊息；別人的 id 與求解進度訊息不影響等待', async () => {
    const host = stubHost()
    const promise = saveFile(new Blob(['x']), 'a.xlsx')
    await vi.waitFor(() => expect(host.posted).toHaveLength(1))
    const id = host.posted[0]!.id

    host.reply({ jobId: 'j1', status: 'running' })
    host.reply({ type: 'save-file-result', id: 'someone-else', status: 'saved' })
    expect(host.listeners.size).toBe(1)

    host.reply({ type: 'save-file-result', id, status: 'error', message: '磁碟已滿' })
    await expect(promise).rejects.toThrow('磁碟已滿')
    expect(host.listeners.size).toBe(0)
  })

  it('postMessage 丟例外 → reject 並退訂', async () => {
    const host = stubHost()
    vi.stubGlobal('window', {
      chrome: {
        webview: {
          addEventListener: (_: 'message', l: Listener) => void host.listeners.add(l),
          removeEventListener: (_: 'message', l: Listener) => void host.listeners.delete(l),
          postMessage: () => {
            throw new Error('boom')
          },
        },
      },
    })
    await expect(saveFile(new Blob(['x']), 'a.xlsx')).rejects.toThrow('boom')
    expect(host.listeners.size).toBe(0)
  })

  it('同時兩個存檔以 id 分開', async () => {
    const host = stubHost()
    const a = saveFile(new Blob(['a']), 'a.xlsx')
    const b = saveFile(new Blob(['b']), 'b.xlsx')
    await vi.waitFor(() => expect(host.posted).toHaveLength(2))
    const [ma, mb] = host.posted
    expect(ma!.id).not.toBe(mb!.id)
    host.reply({ type: 'save-file-result', id: mb!.id, status: 'cancelled' })
    host.reply({ type: 'save-file-result', id: ma!.id, status: 'saved' })
    await expect(a).resolves.toBe('saved')
    await expect(b).resolves.toBe('cancelled')
  })
})

describe('saveFile（瀏覽器開發期）', () => {
  it('沒有 WebView2 host 時走 <a download>，回 downloaded', async () => {
    vi.stubGlobal('window', {})
    const clicked: string[] = []
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:dev-1')
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    vi.stubGlobal('document', {
      createElement: () => {
        const anchor = { href: '', download: '', style: {}, click: () => clicked.push(anchor.download), remove: () => {} }
        return anchor
      },
      body: { appendChild: () => {} },
    })
    await expect(saveFile(new Blob(['x']), 'duty-2026-11.xlsx')).resolves.toBe('downloaded')
    expect(clicked).toEqual(['duty-2026-11.xlsx'])
  })
})

describe('downloadBlob', () => {
  let created: string[]
  let revoked: string[]
  let clicked: string[]
  // URL 編號跨測試遞增：downloadBlob 會留住上一個測試的 URL，編號重複就分不出是誰被收回
  let n = 0

  beforeEach(() => {
    vi.useFakeTimers()
    created = []
    revoked = []
    clicked = []
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:test-${++n}`
      created.push(url)
      return url
    })
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url) => void revoked.push(url))
    vi.stubGlobal('document', {
      createElement: () => {
        const anchor = { href: '', download: '', style: {} as Record<string, string>, click: () => clicked.push(anchor.download), remove: () => {} }
        return anchor
      },
      body: { appendChild: () => {} },
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('觸發下載後不在計時器上收回 URL：瀏覽器的存檔對話框開著時還要讀它（#33）', () => {
    downloadBlob(new Blob(['x']), 'duty-2026-11.xlsx')
    expect(clicked).toEqual(['duty-2026-11.xlsx'])
    // 前一個測試留下的 URL 會在這次呼叫時被收回，那不是這裡要驗的
    revoked = []
    vi.advanceTimersByTime(10 * 60 * 1000)
    expect(revoked).toEqual([])
  })

  it('下一次下載才收回上一個 URL，同時只留一個', () => {
    downloadBlob(new Blob(['a']), 'a.xlsx')
    revoked = []
    downloadBlob(new Blob(['b']), 'b.xlsx')
    expect(revoked).toEqual([created[0]])
    downloadBlob(new Blob(['c']), 'c.xlsx')
    expect(revoked).toEqual([created[0], created[1]])
  })
})
