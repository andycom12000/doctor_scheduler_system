/**
 * 全站共用的 toast 狀態。`ToastHost.vue` 掛一次在 App.vue 裡，任何地方呼叫
 * `useToast().show(...)` 都是同一組。
 *
 * 一般訊息（`info`）幾秒後自動消失；錯誤（`error`）不自動消失，要使用者自己關，
 * 避免一閃而過讀不到。兩者都可以手動關。
 */
import { ref, type Ref } from 'vue'

export type ToastKind = 'info' | 'error'

export interface ToastItem {
  id: number
  kind: ToastKind
  text: string
}

/** 一般訊息自動消失的時間（毫秒）。 */
export const TOAST_AUTO_DISMISS_MS = 5000

const toasts: Ref<ToastItem[]> = ref([])
const timers = new Map<number, ReturnType<typeof setTimeout>>()
let nextId = 1

/** `ToastHost.vue` 專用：目前顯示中的 toast。 */
export function useToastList(): Ref<ToastItem[]> {
  return toasts
}

export function dismissToast(id: number): void {
  const timer = timers.get(id)
  if (timer) clearTimeout(timer)
  timers.delete(id)
  toasts.value = toasts.value.filter((t) => t.id !== id)
}

/** 測試與換頁清場用：關掉全部。 */
export function clearToasts(): void {
  for (const timer of timers.values()) clearTimeout(timer)
  timers.clear()
  toasts.value = []
}

export interface UseToastResult {
  /** 一般訊息：自動消失。 */
  info: (text: string) => number
  /** 錯誤：不自動消失。 */
  error: (text: string) => number
  dismiss: (id: number) => void
}

function push(kind: ToastKind, text: string): number {
  const id = nextId++
  toasts.value = [...toasts.value, { id, kind, text }]
  if (kind === 'info') timers.set(id, setTimeout(() => dismissToast(id), TOAST_AUTO_DISMISS_MS))
  return id
}

export function useToast(): UseToastResult {
  return {
    info: (text) => push('info', text),
    error: (text) => push('error', text),
    dismiss: dismissToast,
  }
}
