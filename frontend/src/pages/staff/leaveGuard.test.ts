import { describe, expect, it, vi } from 'vitest'
import { confirmLeaveIfDirty } from './leaveGuard'

describe('confirmLeaveIfDirty', () => {
  it('沒有未儲存變更時直接放行，不開對話框', async () => {
    const confirm = vi.fn()
    expect(await confirmLeaveIfDirty(false, confirm)).toBe(true)
    expect(confirm).not.toHaveBeenCalled()
  })

  it('有未儲存變更時先確認，按離開才放行', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    expect(await confirmLeaveIfDirty(true, confirm)).toBe(true)
    expect(confirm).toHaveBeenCalledTimes(1)
    expect(confirm.mock.calls[0][0]).toMatchObject({ title: '有未儲存的變更', confirmText: '離開', cancelText: '留在此頁' })
  })

  it('有未儲存變更且選擇留在此頁時取消導覽', async () => {
    const confirm = vi.fn().mockResolvedValue(false)
    expect(await confirmLeaveIfDirty(true, confirm)).toBe(false)
  })
})
