import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TOAST_AUTO_DISMISS_MS, clearErrorToasts, clearToasts, dismissToast, useToast, useToastList } from './useToast'

beforeEach(() => {
  vi.useFakeTimers()
  clearToasts()
})

afterEach(() => {
  clearToasts()
  vi.useRealTimers()
})

describe('useToast', () => {
  it('info 顯示後過了自動消失時間就移除', () => {
    useToast().info('已發布 v2')
    expect(useToastList().value.map((t) => [t.kind, t.text])).toEqual([['info', '已發布 v2']])

    vi.advanceTimersByTime(TOAST_AUTO_DISMISS_MS - 1)
    expect(useToastList().value).toHaveLength(1)
    vi.advanceTimersByTime(1)
    expect(useToastList().value).toHaveLength(0)
  })

  it('error 不會自動消失，要手動關', () => {
    const id = useToast().error('出事了')

    vi.advanceTimersByTime(TOAST_AUTO_DISMISS_MS * 10)
    expect(useToastList().value.map((t) => t.kind)).toEqual(['error'])

    dismissToast(id)
    expect(useToastList().value).toHaveLength(0)
  })

  it('可以同時有多則，手動關其中一則不影響其他', () => {
    const { info, error, dismiss } = useToast()
    const a = info('甲')
    error('乙')

    dismiss(a)
    expect(useToastList().value.map((t) => t.text)).toEqual(['乙'])
  })

  it('手動關掉 info 後，原本的計時器不會誤關之後的 toast', () => {
    const { info, dismiss } = useToast()
    const first = info('第一則')
    dismiss(first)
    info('第二則')

    vi.advanceTimersByTime(TOAST_AUTO_DISMISS_MS - 1)
    expect(useToastList().value.map((t) => t.text)).toEqual(['第二則'])
  })

  it('同時最多一則 error，新的取代舊的；info 不受影響', () => {
    const { info, error } = useToast()
    info('甲')
    error('錯誤一')
    error('錯誤二')

    expect(useToastList().value.map((t) => t.text)).toEqual(['甲', '錯誤二'])
  })

  it('clearErrors 只清 error；clear 全清', () => {
    const { info, error, clearErrors, clear } = useToast()
    info('甲')
    error('乙')

    clearErrors()
    expect(useToastList().value.map((t) => t.text)).toEqual(['甲'])
    error('丙')
    clearErrorToasts()
    expect(useToastList().value.map((t) => t.kind)).toEqual(['info'])
    clear()
    expect(useToastList().value).toHaveLength(0)
  })
})
