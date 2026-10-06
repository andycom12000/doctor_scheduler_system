/**
 * 人員維護頁離開路由的守門（issue #99）。
 *
 * 表單沒有未儲存變更就直接放行；有的話用共用確認對話框問一次，文案與設定頁的
 * `onBeforeRouteLeave` 一致。回傳值直接給 vue-router 的導覽守衛用（`true` 放行、`false` 取消）。
 */
import { useConfirm } from '@/composables/useConfirm'

export type ConfirmFn = ReturnType<typeof useConfirm>['confirm']

export async function confirmLeaveIfDirty(dirty: boolean, confirm: ConfirmFn = useConfirm().confirm): Promise<boolean> {
  if (!dirty) return true
  return confirm({
    title: '有未儲存的變更',
    message: '離開這一頁會捨棄尚未儲存的變更，確定要離開嗎？',
    confirmText: '離開',
    cancelText: '留在此頁',
  })
}
