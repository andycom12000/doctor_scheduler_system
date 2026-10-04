import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { settlePendingConfirm, usePendingConfirm } from '@/composables/useConfirm'
import { downloadBlob, outputPromptKind, promptDraftOutput } from './draftOutput'

beforeEach(() => settlePendingConfirm(false))

describe('promptDraftOutput', () => {
  it('文案帶入動作名稱，並有三個出口', async () => {
    const decision = promptDraftOutput('匯出')
    const pending = usePendingConfirm().value
    expect(pending?.message).toContain('先發布再匯出')
    expect(pending?.confirmText).toBe('先發布再匯出')
    expect(pending?.alternateText).toBe('直接匯出草稿')
    settlePendingConfirm(false)
    await decision
  })

  it('確認 → publish-first、中間鍵 → direct、取消／Esc → cancel', async () => {
    const a = promptDraftOutput('列印')
    settlePendingConfirm(true)
    await expect(a).resolves.toBe('publish-first')

    const b = promptDraftOutput('列印')
    settlePendingConfirm('alternate')
    await expect(b).resolves.toBe('direct')

    const c = promptDraftOutput('列印')
    settlePendingConfirm(false)
    await expect(c).resolves.toBe('cancel')
  })
})

describe('outputPromptKind', () => {
  it('草稿問「先發布」、發布後有修改問「先重新發布」、已發布沒改過不問（#75）', () => {
    expect(outputPromptKind('draft', false)).toBe('draft')
    expect(outputPromptKind('published', true)).toBe('edited')
    expect(outputPromptKind('published', false)).toBeNull()
  })
})

describe('promptDraftOutput（發布後有修改）', () => {
  it('文案講明是哪一版之後改過，出口是重新發布或直接輸出', async () => {
    const decision = promptDraftOutput('列印', { editedFrom: 3 })
    const pending = usePendingConfirm().value
    expect(pending?.title).toBe('發布後有修改')
    expect(pending?.message).toContain('v3')
    expect(pending?.confirmText).toBe('先重新發布再列印')
    expect(pending?.alternateText).toBe('直接列印')
    settlePendingConfirm(true)
    await expect(decision).resolves.toBe('publish-first')
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
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('觸發下載後不在計時器上收回 URL：WebView2 的存檔對話框開著時還要讀它（#33）', () => {
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
