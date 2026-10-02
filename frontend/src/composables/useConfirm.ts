/**
 * 共用確認對話框的狀態。`ConfirmDialog.vue` 掛一次在 App.vue 裡，讀這裡的 `pendingConfirm`
 * 決定要不要開 `<dialog>`；任何地方呼叫 `useConfirm().confirm(...)` 都是同一個對話框。
 *
 * 需要三個出口的情境（例如「先發布再匯出／直接匯出／取消」，issue #34）用 `choose(...)`，
 * 多帶 `alternateText` 就會多出中間那顆按鈕；`confirm()` 的回傳仍是 boolean，既有呼叫端不受影響。
 */
import { ref, type Ref } from 'vue'

export interface ConfirmOptions {
  title: string
  message: string
  confirmText?: string
  cancelText?: string
}

export interface ChooseOptions extends ConfirmOptions {
  /** 第三個出口的按鈕文字（排在取消與確認之間）。 */
  alternateText: string
}

export type ConfirmChoice = 'confirm' | 'alternate' | 'cancel'

interface PendingConfirm extends ConfirmOptions {
  alternateText?: string
  resolve: (result: ConfirmChoice) => void
}

const pendingConfirm: Ref<PendingConfirm | null> = ref(null)

/** `ConfirmDialog.vue` 專用：讀取目前待確認的內容。 */
export function usePendingConfirm(): Ref<PendingConfirm | null> {
  return pendingConfirm
}

/** `ConfirmDialog.vue` 專用：使用者按下確認／取消／關閉對話框後回報結果（`true` = 確認）。 */
export function settlePendingConfirm(result: boolean | 'alternate'): void {
  const choice: ConfirmChoice = result === 'alternate' ? 'alternate' : result ? 'confirm' : 'cancel'
  pendingConfirm.value?.resolve(choice)
  pendingConfirm.value = null
}

export interface UseConfirmResult {
  confirm: (options: ConfirmOptions) => Promise<boolean>
  choose: (options: ChooseOptions) => Promise<ConfirmChoice>
}

export function useConfirm(): UseConfirmResult {
  function open(options: ConfirmOptions & { alternateText?: string }): Promise<ConfirmChoice> {
    // 連續呼叫兩次：不先把前一個 pending 結掉就直接覆蓋
    // `pendingConfirm.value`，前一個呼叫端的 Promise 永遠不會 settle，
    // 而且 ConfirmDialog.vue 對已經 open 的 `<dialog>` 再呼叫一次
    // `showModal()` 會丟 `InvalidStateError`。覆蓋前先幫前一個回報「取消」。
    settlePendingConfirm(false)
    return new Promise((resolve) => {
      pendingConfirm.value = { ...options, resolve }
    })
  }

  async function confirm(options: ConfirmOptions): Promise<boolean> {
    return (await open(options)) === 'confirm'
  }

  function choose(options: ChooseOptions): Promise<ConfirmChoice> {
    return open(options)
  }

  return { confirm, choose }
}
