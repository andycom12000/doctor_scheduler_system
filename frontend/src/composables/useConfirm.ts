/**
 * 共用確認對話框的狀態。`ConfirmDialog.vue` 掛一次在 App.vue 裡，讀這裡的 `pendingConfirm`
 * 決定要不要開 `<dialog>`；任何地方呼叫 `useConfirm().confirm(...)` 都是同一個對話框。
 */
import { ref, type Ref } from 'vue'

export interface ConfirmOptions {
  title: string
  message: string
  confirmText?: string
  cancelText?: string
}

interface PendingConfirm extends ConfirmOptions {
  resolve: (result: boolean) => void
}

const pendingConfirm: Ref<PendingConfirm | null> = ref(null)

/** `ConfirmDialog.vue` 專用：讀取目前待確認的內容。 */
export function usePendingConfirm(): Ref<PendingConfirm | null> {
  return pendingConfirm
}

/** `ConfirmDialog.vue` 專用：使用者按下確認／取消／關閉對話框後回報結果。 */
export function settlePendingConfirm(result: boolean): void {
  pendingConfirm.value?.resolve(result)
  pendingConfirm.value = null
}

export interface UseConfirmResult {
  confirm: (options: ConfirmOptions) => Promise<boolean>
}

export function useConfirm(): UseConfirmResult {
  function confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise((resolve) => {
      pendingConfirm.value = { ...options, resolve }
    })
  }

  return { confirm }
}
