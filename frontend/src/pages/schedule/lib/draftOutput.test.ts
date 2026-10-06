import { beforeEach, describe, expect, it } from 'vitest'
import { settlePendingConfirm, usePendingConfirm } from '@/composables/useConfirm'
import { outputPromptKind, promptDraftOutput } from './draftOutput'

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
