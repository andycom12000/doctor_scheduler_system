import { describe, expect, it } from 'vitest'
import { describeSaveFailures, type SaveOutcome } from './saveReport'

const ok = (label: string): SaveOutcome => ({ label, result: { status: 'fulfilled', value: null } })
const fail = (label: string, message: string): SaveOutcome => ({
  label,
  result: { status: 'rejected', reason: new Error(message) },
})
const describe_ = (err: unknown) => (err as Error).message

describe('describeSaveFailures', () => {
  it('全部成功（或沒有任何文件）回 null', () => {
    expect(describeSaveFailures([ok('A'), ok('B')], describe_)).toBeNull()
    expect(describeSaveFailures([], describe_)).toBeNull()
  })

  it('單份失敗：講名稱與原因', () => {
    expect(describeSaveFailures([fail('資格矩陣', '請求內容有誤')], describe_)).toBe('「資格矩陣」儲存失敗：請求內容有誤')
  })

  it('多份失敗：每份各自一行，不只第一個', () => {
    const text = describeSaveFailures([fail('身分設定', '甲'), fail('點數規則', '乙')], describe_)
    expect(text).toBe('「身分設定」儲存失敗：甲\n「點數規則」儲存失敗：乙')
  })

  it('部分成功部分失敗：先說哪些已存、再列失敗各自原因', () => {
    const text = describeSaveFailures([ok('身分設定'), fail('點數規則', '乙'), ok('逐月覆寫')], describe_)
    expect(text).toBe('已儲存：身分設定、逐月覆寫\n「點數規則」儲存失敗：乙')
  })
})
