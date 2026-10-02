import { beforeEach, describe, expect, it } from 'vitest'
import { settlePendingConfirm, usePendingConfirm, useConfirm } from './useConfirm'

beforeEach(() => {
  // 模組層級的 pendingConfirm 在測試之間共用，先清空避免互相汙染。
  settlePendingConfirm(false)
})

describe('useConfirm', () => {
  it('confirm() 回傳的 Promise 在 settlePendingConfirm(true) 後 resolve 成 true', async () => {
    const { confirm } = useConfirm()
    const result = confirm({ title: '標題', message: '內容' })

    expect(usePendingConfirm().value?.title).toBe('標題')
    settlePendingConfirm(true)

    await expect(result).resolves.toBe(true)
    expect(usePendingConfirm().value).toBeNull()
  })

  it('settlePendingConfirm(false) 讓 Promise resolve 成 false', async () => {
    const { confirm } = useConfirm()
    const result = confirm({ title: '標題', message: '內容' })

    settlePendingConfirm(false)

    await expect(result).resolves.toBe(false)
  })

  it('連續呼叫兩次 confirm()：第一個 Promise 不會卡住，會被覆蓋前自動回報 false', async () => {
    const { confirm } = useConfirm()
    const first = confirm({ title: '第一個', message: '不會有人回應這個' })
    const second = confirm({ title: '第二個', message: '目前顯示的是這個' })

    // 第一個 confirm() 的 Promise 應該已經自己 settle 成 false，不必等使用者操作。
    await expect(first).resolves.toBe(false)

    // pendingConfirm 現在指向第二個，使用者確認後只影響第二個的 Promise。
    expect(usePendingConfirm().value?.title).toBe('第二個')
    settlePendingConfirm(true)
    await expect(second).resolves.toBe(true)
  })

  it('沒有 pending 時呼叫 settlePendingConfirm 不會丟例外', () => {
    expect(() => settlePendingConfirm(true)).not.toThrow()
    expect(usePendingConfirm().value).toBeNull()
  })
})

describe('useConfirm().choose', () => {
  it('三個出口各自 resolve 成對應的字串', async () => {
    const { choose } = useConfirm()
    const base = { title: '標題', message: '內容', alternateText: '直接匯出' }

    const a = choose(base)
    expect(usePendingConfirm().value?.alternateText).toBe('直接匯出')
    settlePendingConfirm(true)
    await expect(a).resolves.toBe('confirm')

    const b = choose(base)
    settlePendingConfirm('alternate')
    await expect(b).resolves.toBe('alternate')

    const c = choose(base)
    settlePendingConfirm(false)
    await expect(c).resolves.toBe('cancel')
  })

  it('confirm() 遇到 alternate 以外的出口行為不變：只有確認才是 true', async () => {
    const { confirm } = useConfirm()
    const result = confirm({ title: '標題', message: '內容' })
    settlePendingConfirm('alternate')
    await expect(result).resolves.toBe(false)
  })
})
