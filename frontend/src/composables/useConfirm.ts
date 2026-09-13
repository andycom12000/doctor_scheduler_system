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
    // 連續呼叫兩次 confirm()：不先把前一個 pending 結掉就直接覆蓋
    // `pendingConfirm.value`，前一個呼叫端的 Promise 永遠不會 settle，
    // 而且 ConfirmDialog.vue 對已經 open 的 `<dialog>` 再呼叫一次
    // `showModal()` 會丟 `InvalidStateError`。覆蓋前先幫前一個回報「取消」。
    settlePendingConfirm(false)
    return new Promise((resolve) => {
      pendingConfirm.value = { ...options, resolve }
    })
  }

  return { confirm }
}
