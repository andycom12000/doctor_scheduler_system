import { beforeEach, describe, expect, it } from 'vitest'
import { settlePendingConfirm, usePendingConfirm } from '@/composables/useConfirm'
import { promptDraftOutput } from './draftOutput'

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
